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
    /// Builds an Optional ProtectServe directory URL for an Emby HLS rendition.
    /// The rendition hash excludes session/auth parameters so later sessions use
    /// the same segment cache key while content-affecting parameters remain
    /// isolated in the path.
    /// </summary>
    internal static class CacheFlyProtectServeSigner
    {
        private const int MaximumTtlSeconds = 6 * 60 * 60;
        private const int RenditionHashBytes = 16;
        private static readonly char[] Base64Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".ToCharArray();
        private static Builder current;

        internal sealed class Builder
        {
            private readonly byte[] key;
            private readonly string publicOrigin;
            private readonly string hlsPath;
            private readonly int ttlSeconds;

            private Builder(byte[] keyBytes, string origin, string path, int ttl)
            {
                key = (byte[])keyBytes.Clone();
                publicOrigin = origin;
                hlsPath = path;
                ttlSeconds = ttl;
            }

            internal static bool TryCreate(
                string keyText,
                string publicBaseText,
                int ttl,
                out Builder builder,
                out string error)
            {
                builder = null;
                error = null;
                var trimmedKey = keyText?.Trim();
                if (string.IsNullOrEmpty(trimmedKey) ||
                    trimmedKey.Length > 32 ||
                    trimmedKey.Any(character => Array.IndexOf(Base64Alphabet, character) < 0))
                {
                    error = "ProtectServe key 必须是 1 到 32 个 base64 字符";
                    return false;
                }

                if (!TryNormalizeHlsBase(publicBaseText, out var origin, out var path))
                {
                    error = "CacheFly HLS base 必须是 HTTPS origin 下的 /cachefly-hls 路径";
                    return false;
                }

                if (ttl <= 0 || ttl > MaximumTtlSeconds)
                {
                    error = "ProtectServe TTL 必须在 1 到 21600 秒之间";
                    return false;
                }

                builder = new Builder(Encoding.UTF8.GetBytes(trimmedKey), origin, path, ttl);
                return true;
            }

            internal bool TryBuild(
                string transcodingUrl,
                string itemId,
                long nowUnixSeconds,
                out string signedUrl,
                out string renditionHash)
            {
                signedUrl = null;
                renditionHash = null;
                if (!TryParseMasterUrl(
                    transcodingUrl,
                    itemId,
                    out var originalQuery,
                    out var normalizedRendition))
                {
                    return false;
                }

                long expiry;
                try
                {
                    expiry = checked(nowUnixSeconds + ttlSeconds);
                }
                catch (OverflowException)
                {
                    return false;
                }

                renditionHash = HashRendition(normalizedRendition);
                var escapedItemId = Uri.EscapeDataString(itemId);
                var pathToHash = hlsPath.TrimStart('/') +
                    "/r/" + renditionHash +
                    "/videos/" + escapedItemId + "/";
                var rules = "expiretime=" + expiry.ToString(CultureInfo.InvariantCulture) +
                    ";dirmatch=true;sp=1";
                var message = Encoding.UTF8.GetBytes(rules + pathToHash);

                byte[] digest;
                using (var hmac = new HMACSHA256(key))
                {
                    digest = hmac.ComputeHash(message);
                }

                signedUrl = publicOrigin +
                    "/Protected/" + rules +
                    "/" + ToLowerHex(digest) +
                    "/" + pathToHash +
                    "master.m3u8" + originalQuery;
                return true;
            }

            private static bool TryParseMasterUrl(
                string text,
                string itemId,
                out string originalQuery,
                out string normalizedRendition)
            {
                originalQuery = null;
                normalizedRendition = null;
                if (string.IsNullOrWhiteSpace(itemId) ||
                    string.IsNullOrWhiteSpace(text) ||
                    !text.StartsWith("/", StringComparison.Ordinal) ||
                    !Uri.TryCreate("https://emby.invalid" + text, UriKind.Absolute, out var uri) ||
                    !string.Equals(
                        uri.AbsolutePath,
                        "/videos/" + Uri.EscapeDataString(itemId) + "/master.m3u8",
                        StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment))
                {
                    return false;
                }

                originalQuery = uri.Query;
                var stablePairs = new List<string>();
                foreach (var pair in uri.Query.TrimStart('?')
                    .Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = pair.IndexOf('=');
                    var rawName = separator < 0 ? pair : pair.Substring(0, separator);
                    string name;
                    try
                    {
                        name = Uri.UnescapeDataString(rawName.Replace("+", " "));
                    }
                    catch (UriFormatException)
                    {
                        return false;
                    }

                    if (string.Equals(name, "api_key", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "PlaySessionId", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "DeviceId", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    stablePairs.Add(pair);
                }

                if (stablePairs.Count == 0)
                {
                    return false;
                }

                stablePairs.Sort(StringComparer.Ordinal);
                normalizedRendition = uri.AbsolutePath + "\n" + string.Join("&", stablePairs);
                return true;
            }

            private static string HashRendition(string normalized)
            {
                byte[] digest;
                using (var sha256 = SHA256.Create())
                {
                    digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                }

                var truncated = new byte[RenditionHashBytes];
                Buffer.BlockCopy(digest, 0, truncated, 0, truncated.Length);
                return ToLowerHex(truncated);
            }

            private static bool TryNormalizeHlsBase(
                string text,
                out string origin,
                out string path)
            {
                origin = null;
                path = null;
                if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                    !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(uri.Host) ||
                    !string.IsNullOrEmpty(uri.UserInfo) ||
                    !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment) ||
                    !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/cachefly-hls", StringComparison.Ordinal))
                {
                    return false;
                }

                origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
                path = "/cachefly-hls";
                return true;
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
        }

        internal static bool Configure(
            bool enabled,
            string keyFile,
            string publicBase,
            int ttlSeconds,
            out string error)
        {
            error = null;
            if (!enabled)
            {
                Interlocked.Exchange(ref current, null);
                return true;
            }

            string keyText;
            try
            {
                keyText = File.ReadAllText(keyFile?.Trim() ?? string.Empty);
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is ArgumentException ||
                ex is NotSupportedException)
            {
                Interlocked.Exchange(ref current, null);
                error = "无法读取 ProtectServe key 文件: " + ex.GetType().Name;
                return false;
            }

            if (!Builder.TryCreate(keyText, publicBase, ttlSeconds, out var builder, out error))
            {
                Interlocked.Exchange(ref current, null);
                return false;
            }

            Interlocked.Exchange(ref current, builder);
            return true;
        }

        internal static bool TryBuild(
            string transcodingUrl,
            string itemId,
            out string signedUrl,
            out string renditionHash)
        {
            signedUrl = null;
            renditionHash = null;
            var builder = Interlocked.CompareExchange(ref current, null, null);
            return builder != null && builder.TryBuild(
                transcodingUrl,
                itemId,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                out signedUrl,
                out renditionHash);
        }
    }
}
