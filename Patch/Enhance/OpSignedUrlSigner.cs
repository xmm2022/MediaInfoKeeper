#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MediaInfoKeeper.Patch
{
    /// <summary>
    /// 为受控的内部 src URL 生成与 emby-sign-canary v2 完全兼容的短期 op URL。
    /// </summary>
    internal static class OpSignedUrlSigner
    {
        internal sealed class Builder
        {
            private const int KeyLength = 32;
            private const int NonceLength = 16;
            private const int MaximumTtlSeconds = 6 * 60 * 60;
            private const int SignedUrlReuseSeconds = 30;
            private const string Main115Host = "tes.inemby.us.ci";
            private const string Main115ResourcePrefix = "/openlist/__main_115__/vault-47f2/";
            private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

            private readonly byte[] key;
            private readonly string publicBase;
            private readonly string legacyAuthority;
            private readonly int ttlSeconds;
            private readonly object signedUrlCacheLock = new object();
            private readonly Dictionary<string, CachedSignedUrl> signedUrlCache =
                new Dictionary<string, CachedSignedUrl>(StringComparer.Ordinal);

            private sealed class CachedSignedUrl
            {
                internal string Url { get; set; }

                internal long ReuseUntilUnixSeconds { get; set; }
            }

            private Builder(byte[] keyBytes, string normalizedPublicBase, string normalizedLegacyAuthority, int ttl)
            {
                key = (byte[])keyBytes.Clone();
                publicBase = normalizedPublicBase;
                legacyAuthority = normalizedLegacyAuthority;
                ttlSeconds = ttl;
            }

            internal static bool TryCreate(
                byte[] keyBytes,
                string publicBaseText,
                string legacyHostText,
                int ttl,
                out Builder builder,
                out string error)
            {
                builder = null;
                error = null;

                if (keyBytes == null || keyBytes.Length != KeyLength)
                {
                    error = "签名 key 必须正好为 32 个原始字节";
                    return false;
                }

                if (!TryNormalizePublicBase(publicBaseText, out var normalizedPublicBase))
                {
                    error = "public base 必须是无路径、查询串或 fragment 的 HTTPS origin";
                    return false;
                }

                if (!TryNormalizeLegacyAuthority(legacyHostText, out var normalizedLegacyAuthority))
                {
                    error = "legacy host 必须是 host:port，且不能包含 scheme、路径或用户信息";
                    return false;
                }

                if (ttl <= 0 || ttl > MaximumTtlSeconds)
                {
                    error = "TTL 必须在 1 到 21600 秒之间";
                    return false;
                }

                builder = new Builder(keyBytes, normalizedPublicBase, normalizedLegacyAuthority, ttl);
                return true;
            }

            internal bool TryBuild(string legacyUrl, out string signedUrl)
            {
                signedUrl = null;
                if (!TryExtractNormalizedResource(legacyUrl, legacyAuthority, out var resource))
                {
                    return false;
                }

                return TryBuildCached(legacyUrl, resource, out signedUrl);
            }

            internal bool TryBuildMain(string legacyUrl, out string signedUrl)
            {
                signedUrl = null;
                if (TryExtractNormalizedResource(legacyUrl, legacyAuthority, out var resource))
                {
                    return TryBuildCached(legacyUrl, resource, out signedUrl);
                }

                if (!TryExtractMain115Resource(legacyUrl, legacyAuthority, out resource))
                {
                    return false;
                }

                return TryBuildCached("main-115\n" + legacyUrl, resource, out signedUrl);
            }

            private bool TryBuildCached(string cacheKey, string resource, out string signedUrl)
            {
                signedUrl = null;
                var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                lock (signedUrlCacheLock)
                {
                    if (signedUrlCache.TryGetValue(cacheKey ?? string.Empty, out var cached) &&
                        cached.ReuseUntilUnixSeconds > nowUnixSeconds)
                    {
                        signedUrl = cached.Url;
                        return true;
                    }

                    foreach (var expiredKey in signedUrlCache
                        .Where(pair => pair.Value.ReuseUntilUnixSeconds <= nowUnixSeconds)
                        .Select(pair => pair.Key)
                        .ToArray())
                    {
                        signedUrlCache.Remove(expiredKey);
                    }

                    var nonce = new byte[NonceLength];
                    using (var random = RandomNumberGenerator.Create())
                    {
                        random.GetBytes(nonce);
                    }

                    if (!TryBuildResource(resource, nowUnixSeconds, nonce, out signedUrl))
                    {
                        return false;
                    }

                    var reuseSeconds = Math.Min(SignedUrlReuseSeconds, Math.Max(0, ttlSeconds - 1));
                    if (reuseSeconds > 0)
                    {
                        signedUrlCache[cacheKey] = new CachedSignedUrl
                        {
                            Url = signedUrl,
                            ReuseUntilUnixSeconds = nowUnixSeconds + reuseSeconds
                        };
                    }

                    return true;
                }
            }

            internal bool TryBuildUnsignedResourcePath(
                string legacyUrl,
                out string resourcePath)
            {
                resourcePath = null;
                if (!TryExtractNormalizedResource(legacyUrl, legacyAuthority, out var resource))
                {
                    return false;
                }

                resourcePath = EscapeResource(resource);
                return true;
            }

            internal bool TryBuild(string legacyUrl, long nowUnixSeconds, byte[] nonce, out string signedUrl)
            {
                signedUrl = null;
                if (nonce == null || nonce.Length != NonceLength ||
                    !TryExtractNormalizedResource(legacyUrl, legacyAuthority, out var resource))
                {
                    return false;
                }

                return TryBuildResource(resource, nowUnixSeconds, nonce, out signedUrl);
            }

            internal bool TryBuildMain(
                string legacyUrl,
                long nowUnixSeconds,
                byte[] nonce,
                out string signedUrl)
            {
                signedUrl = null;
                if (nonce == null || nonce.Length != NonceLength ||
                    (!TryExtractNormalizedResource(legacyUrl, legacyAuthority, out var resource) &&
                     !TryExtractMain115Resource(legacyUrl, legacyAuthority, out resource)))
                {
                    return false;
                }

                return TryBuildResource(resource, nowUnixSeconds, nonce, out signedUrl);
            }

            private bool TryBuildResource(
                string resource,
                long nowUnixSeconds,
                byte[] nonce,
                out string signedUrl)
            {
                signedUrl = null;

                long expiry;
                try
                {
                    expiry = checked(nowUnixSeconds + ttlSeconds);
                }
                catch (OverflowException)
                {
                    return false;
                }

                var expiryText = expiry.ToString(CultureInfo.InvariantCulture);
                var nonceHex = ToLowerHex(nonce);
                var message = Encoding.UTF8.GetBytes(
                    "v2\n" + expiryText + "\n" + nonceHex + "\n" + resource);

                byte[] digest;
                using (var hmac = new HMACSHA256(key))
                {
                    digest = hmac.ComputeHash(message);
                }

                signedUrl = publicBase +
                    "/v1-canary/" + expiryText +
                    "/" + nonceHex +
                    "/" + ToLowerHex(digest) +
                    EscapeResource(resource);
                return true;
            }

            private static bool TryNormalizePublicBase(string text, out string normalized)
            {
                normalized = null;
                if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(uri.Host) ||
                    !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment) ||
                    !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
                {
                    return false;
                }

                normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
                return true;
            }

            private static bool TryNormalizeLegacyAuthority(string text, out string normalized)
            {
                normalized = null;
                var candidate = text?.Trim();
                if (string.IsNullOrWhiteSpace(candidate) ||
                    candidate.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                    candidate.IndexOfAny(new[] { '/', '?', '#', '@' }) >= 0 ||
                    !Uri.TryCreate("http://" + candidate, UriKind.Absolute, out var uri) ||
                    string.IsNullOrWhiteSpace(uri.Host) ||
                    uri.IsDefaultPort)
                {
                    return false;
                }

                normalized = uri.Authority;
                return string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase);
            }

            private static bool TryExtractNormalizedResource(
                string legacyUrl,
                string expectedAuthority,
                out string resource)
            {
                resource = null;
                if (string.IsNullOrWhiteSpace(legacyUrl))
                {
                    return false;
                }

                const string schemePrefix = "http://";
                if (!legacyUrl.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var authorityStart = schemePrefix.Length;
                var pathStart = legacyUrl.IndexOf('/', authorityStart);
                if (pathStart < 0)
                {
                    return false;
                }

                var authority = legacyUrl.Substring(authorityStart, pathStart - authorityStart);
                if (!string.Equals(authority, expectedAuthority, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var rawPath = legacyUrl.Substring(pathStart);
                if (rawPath.IndexOfAny(new[] { '?', '#' }) >= 0)
                {
                    return false;
                }

                var rawSegments = rawPath.Split('/');
                if (rawSegments.Length < 4 ||
                    rawSegments[0].Length != 0 ||
                    !IsHexToken(rawSegments[1], 32))
                {
                    return false;
                }

                var rawResource = "/" + string.Join("/", rawSegments.Skip(2));
                if (ContainsEncodedPathSeparator(rawResource) ||
                    !TryPercentDecodeUtf8(rawResource, out var decodedResource) ||
                    !IsNormalizedResource(decodedResource))
                {
                    return false;
                }

                resource = decodedResource;
                return true;
            }

            private static bool TryExtractMain115Resource(
                string legacyUrl,
                string expectedLegacyAuthority,
                out string resource)
            {
                resource = null;
                if (!Uri.TryCreate(legacyUrl?.Trim(), UriKind.Absolute, out var uri) ||
                    !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment))
                {
                    return false;
                }

                string requiredRouteSegment;
                if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(uri.Authority, expectedLegacyAuthority, StringComparison.OrdinalIgnoreCase))
                {
                    requiredRouteSegment = "115";
                }
                else if (
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    uri.IsDefaultPort &&
                    string.Equals(uri.Host, Main115Host, StringComparison.OrdinalIgnoreCase))
                {
                    requiredRouteSegment = "cd2";
                }
                else
                {
                    return false;
                }

                var rawPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
                var firstSeparator = rawPath.IndexOf('/');
                var secondSeparator = firstSeparator < 0
                    ? -1
                    : rawPath.IndexOf('/', firstSeparator + 1);
                var thirdSeparator = secondSeparator < 0
                    ? -1
                    : rawPath.IndexOf('/', secondSeparator + 1);
                if (firstSeparator != 32 || secondSeparator < 0 || thirdSeparator < 0 ||
                    !IsHexToken(rawPath.Substring(0, firstSeparator), 32) ||
                    !string.Equals(
                        rawPath.Substring(firstSeparator + 1, secondSeparator - firstSeparator - 1),
                        requiredRouteSegment,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        rawPath.Substring(secondSeparator + 1, thirdSeparator - secondSeparator - 1),
                        "vault-47f2",
                        StringComparison.Ordinal))
                {
                    return false;
                }

                var rawMediaPath = rawPath.Substring(thirdSeparator + 1);
                if (string.IsNullOrEmpty(rawMediaPath) ||
                    !TryPercentDecodeUtf8(rawMediaPath, out var decodedMediaPath) ||
                    string.IsNullOrEmpty(decodedMediaPath) ||
                    decodedMediaPath[0] == '/' ||
                    decodedMediaPath[0] == '\\')
                {
                    return false;
                }

                var normalized = Main115ResourcePrefix + decodedMediaPath;
                if (!IsNormalizedResource(normalized))
                {
                    return false;
                }

                resource = normalized;
                return true;
            }

            private static bool IsNormalizedResource(string resource)
            {
                if (string.IsNullOrEmpty(resource) ||
                    (!resource.StartsWith("/google/", StringComparison.Ordinal) &&
                     !resource.StartsWith("/openlist/", StringComparison.Ordinal)))
                {
                    return false;
                }

                var segments = resource.Split('/');
                if (segments.Length < 3 || segments[0].Length != 0)
                {
                    return false;
                }

                for (var index = 1; index < segments.Length; index++)
                {
                    var segment = segments[index];
                    if (string.Equals(segment, ".", StringComparison.Ordinal) ||
                        string.Equals(segment, "..", StringComparison.Ordinal) ||
                        segment.IndexOf('\\') >= 0 ||
                        segment.Any(char.IsControl))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool IsHexToken(string value, int expectedLength)
            {
                if (value == null || value.Length != expectedLength)
                {
                    return false;
                }

                foreach (var character in value)
                {
                    if (!TryHexValue(character, out _))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool ContainsEncodedPathSeparator(string value)
            {
                for (var index = 0; index + 2 < value.Length; index++)
                {
                    if (value[index] != '%' ||
                        !TryHexValue(value[index + 1], out var high) ||
                        !TryHexValue(value[index + 2], out var low))
                    {
                        continue;
                    }

                    var decoded = (high << 4) | low;
                    if (decoded == '/' || decoded == '\\')
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool TryPercentDecodeUtf8(string value, out string decoded)
            {
                decoded = null;
                if (value == null)
                {
                    return false;
                }

                using (var bytes = new MemoryStream(value.Length))
                {
                    for (var index = 0; index < value.Length;)
                    {
                        if (value[index] == '%')
                        {
                            if (index + 2 >= value.Length ||
                                !TryHexValue(value[index + 1], out var high) ||
                                !TryHexValue(value[index + 2], out var low))
                            {
                                return false;
                            }

                            bytes.WriteByte((byte)((high << 4) | low));
                            index += 3;
                            continue;
                        }

                        var scalarLength = char.IsSurrogate(value[index]) ? 2 : 1;
                        if (scalarLength == 2 &&
                            (index + 1 >= value.Length ||
                             !char.IsHighSurrogate(value[index]) ||
                             !char.IsLowSurrogate(value[index + 1])))
                        {
                            return false;
                        }

                        var encoded = StrictUtf8.GetBytes(value.Substring(index, scalarLength));
                        bytes.Write(encoded, 0, encoded.Length);
                        index += scalarLength;
                    }

                    try
                    {
                        decoded = StrictUtf8.GetString(bytes.ToArray());
                        return true;
                    }
                    catch (DecoderFallbackException)
                    {
                        return false;
                    }
                }
            }

            private static string EscapeResource(string resource)
            {
                var segments = resource.Split('/');
                for (var index = 1; index < segments.Length; index++)
                {
                    segments[index] = Uri.EscapeDataString(segments[index]);
                }

                return string.Join("/", segments);
            }

            private static string ToLowerHex(byte[] bytes)
            {
                var chars = new char[bytes.Length * 2];
                const string alphabet = "0123456789abcdef";
                for (var index = 0; index < bytes.Length; index++)
                {
                    chars[index * 2] = alphabet[bytes[index] >> 4];
                    chars[index * 2 + 1] = alphabet[bytes[index] & 0x0f];
                }

                return new string(chars);
            }

            private static bool TryHexValue(char value, out int result)
            {
                if (value >= '0' && value <= '9')
                {
                    result = value - '0';
                    return true;
                }

                if (value >= 'a' && value <= 'f')
                {
                    result = value - 'a' + 10;
                    return true;
                }

                if (value >= 'A' && value <= 'F')
                {
                    result = value - 'A' + 10;
                    return true;
                }

                result = 0;
                return false;
            }
        }

        private static Builder current;

        internal static bool Configure(
            bool enabled,
            string keyFile,
            string publicBase,
            string legacyHost,
            int ttlSeconds,
            out string error)
        {
            error = null;
            if (!enabled)
            {
                Interlocked.Exchange(ref current, null);
                return true;
            }

            byte[] keyBytes;
            try
            {
                keyBytes = File.ReadAllBytes(keyFile?.Trim() ?? string.Empty);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                Interlocked.Exchange(ref current, null);
                error = "无法读取签名 key 文件: " + ex.GetType().Name;
                return false;
            }

            if (!Builder.TryCreate(keyBytes, publicBase, legacyHost, ttlSeconds, out var builder, out error))
            {
                Interlocked.Exchange(ref current, null);
                return false;
            }

            Interlocked.Exchange(ref current, builder);
            return true;
        }

        internal static bool TryBuild(string legacyUrl, out string signedUrl)
        {
            signedUrl = null;
            var builder = Interlocked.CompareExchange(ref current, null, null);
            return builder != null && builder.TryBuild(legacyUrl, out signedUrl);
        }

        internal static bool TryBuildMain(string legacyUrl, out string signedUrl)
        {
            signedUrl = null;
            var builder = Interlocked.CompareExchange(ref current, null, null);
            return builder != null && builder.TryBuildMain(legacyUrl, out signedUrl);
        }

        internal static bool TryBuildUnsignedResourcePath(
            string legacyUrl,
            out string resourcePath)
        {
            resourcePath = null;
            var builder = Interlocked.CompareExchange(ref current, null, null);
            return builder != null &&
                builder.TryBuildUnsignedResourcePath(legacyUrl, out resourcePath);
        }

        internal static string DescribeTarget(string url)
        {
            if (Uri.TryCreate(url, UriKind.Relative, out var relativeUri) &&
                url.StartsWith("/", StringComparison.Ordinal) &&
                !url.StartsWith("//", StringComparison.Ordinal))
            {
                var separator = relativeUri.OriginalString.IndexOf('/', 1);
                return separator < 0
                    ? "same-origin:" + relativeUri.OriginalString
                    : "same-origin:" + relativeUri.OriginalString.Substring(0, separator);
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return "invalid";
            }

            var host = uri.Host.IndexOf(':') >= 0 ? "[" + uri.Host + "]" : uri.Host;
            return uri.Scheme + "://" + host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
        }
    }
}
