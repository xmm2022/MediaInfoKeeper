#nullable disable

using System;
using System.Linq;

namespace MediaInfoKeeper.Patch
{
    internal enum PlaybackDirectUrlMode
    {
        None,
        Esa,
        CacheFly,
        CacheFlyHls,
        Eo,
        Op,
        Main
    }

    internal static class EsaPlaybackDirectUrlPolicy
    {
        internal const string MarkerHeader = "X-Esa-Proxy-Entry";
        internal const string CacheFlyMarkerHeader = "X-CacheFly-Canary-Entry";
        internal const string CacheFlyHlsMarkerHeader = "X-CacheFly-Hls-Canary-Entry";
        internal const string EoMarkerHeader = "X-Eo-Canary-Entry";
        internal const string OpMarkerHeader = "X-Op-Direct-Entry";
        internal const string MainMarkerHeader = "X-Main-Unified-Entry";
        internal const string MarkerValue = "1";

        internal static string[] ParseClients(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                ? Array.Empty<string>()
                : text
                    .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item?.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        internal static bool IsRequestEligible(
            bool enabled,
            string markerValue,
            string client,
            string[] allowedClients)
        {
            if (!enabled ||
                !string.Equals(markerValue?.Trim(), MarkerValue, StringComparison.Ordinal) ||
                allowedClients == null)
            {
                return false;
            }

            if (allowedClients.Any(item =>
                string.Equals(item, "*", StringComparison.Ordinal)))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(client) &&
                allowedClients.Any(item =>
                    string.Equals(item, client.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        internal static PlaybackDirectUrlMode ResolveMode(
            bool esaEnabled,
            string esaMarkerValue,
            string cacheFlyMarkerValue,
            string cacheFlyHlsMarkerValue,
            string eoMarkerValue,
            string[] esaAllowedClients,
            bool opEnabled,
            string opMarkerValue,
            string[] opAllowedClients,
            string client)
        {
            return ResolveMode(
                esaEnabled,
                esaMarkerValue,
                cacheFlyMarkerValue,
                cacheFlyHlsMarkerValue,
                eoMarkerValue,
                esaAllowedClients,
                opEnabled,
                opMarkerValue,
                opAllowedClients,
                false,
                null,
                Array.Empty<string>(),
                client);
        }

        internal static PlaybackDirectUrlMode ResolveMode(
            bool esaEnabled,
            string esaMarkerValue,
            string cacheFlyMarkerValue,
            string cacheFlyHlsMarkerValue,
            string eoMarkerValue,
            string[] esaAllowedClients,
            bool opEnabled,
            string opMarkerValue,
            string[] opAllowedClients,
            bool mainEnabled,
            string mainMarkerValue,
            string[] mainAllowedClients,
            string client)
        {
            var esaMarked = esaEnabled &&
                string.Equals(esaMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var cacheFlyMarked = esaEnabled &&
                string.Equals(cacheFlyMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var cacheFlyHlsMarked = esaEnabled &&
                string.Equals(cacheFlyHlsMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var eoMarked = esaEnabled &&
                string.Equals(eoMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var opMarked = opEnabled &&
                string.Equals(opMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var mainMarked = mainEnabled &&
                string.Equals(mainMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var markedCount = (esaMarked ? 1 : 0) +
                (cacheFlyMarked ? 1 : 0) +
                (cacheFlyHlsMarked ? 1 : 0) +
                (eoMarked ? 1 : 0) +
                (opMarked ? 1 : 0) +
                (mainMarked ? 1 : 0);
            if (markedCount != 1)
            {
                return PlaybackDirectUrlMode.None;
            }

            var providerMarkerValue = esaMarked
                ? esaMarkerValue
                : cacheFlyMarked
                    ? cacheFlyMarkerValue
                    : cacheFlyHlsMarked
                        ? cacheFlyHlsMarkerValue
                        : eoMarkerValue;
            var providerEligible = IsRequestEligible(
                esaEnabled,
                providerMarkerValue,
                client,
                esaAllowedClients);
            var opEligible = IsRequestEligible(
                opEnabled,
                opMarkerValue,
                client,
                opAllowedClients);
            var mainEligible = IsRequestEligible(
                mainEnabled,
                mainMarkerValue,
                client,
                mainAllowedClients);

            var eligibleCount = (providerEligible ? 1 : 0) +
                (opEligible ? 1 : 0) +
                (mainEligible ? 1 : 0);
            if (eligibleCount != 1)
            {
                return PlaybackDirectUrlMode.None;
            }

            if (mainEligible)
            {
                return PlaybackDirectUrlMode.Main;
            }

            if (opEligible)
            {
                return PlaybackDirectUrlMode.Op;
            }

            return cacheFlyHlsMarked
                ? PlaybackDirectUrlMode.CacheFlyHls
                : cacheFlyMarked
                    ? PlaybackDirectUrlMode.CacheFly
                    : eoMarked
                        ? PlaybackDirectUrlMode.Eo
                        : PlaybackDirectUrlMode.Esa;
        }

        internal static bool TryBuildOutputUrl(
            PlaybackDirectUrlMode mode,
            string esaStreamBase,
            string signedOpUrl,
            out string outputUrl)
        {
            outputUrl = null;
            if (!TryParseSignedOpUrl(signedOpUrl, out var signedUri))
            {
                return false;
            }

            if (mode == PlaybackDirectUrlMode.Op)
            {
                outputUrl = signedOpUrl;
                return true;
            }

            return mode != PlaybackDirectUrlMode.None &&
                TryRebaseSignedUrl(esaStreamBase, signedUri.AbsoluteUri, out outputUrl);
        }

        internal static bool TryRebaseSignedUrl(
            string esaStreamBase,
            string signedOpUrl,
            out string esaUrl)
        {
            esaUrl = null;
            if (!TryNormalizeStreamBase(esaStreamBase, out var normalizedBase) ||
                !TryParseSignedOpUrl(signedOpUrl, out var signedUri))
            {
                return false;
            }

            esaUrl = normalizedBase + signedUri.AbsolutePath;
            return true;
        }

        internal static bool IsUnsignedEoStreamBase(string text)
        {
            return TryNormalizeUnsignedEoStreamBase(text, out _);
        }

        internal static bool TryBuildUnsignedEoUrl(
            string eoStreamBase,
            string resourcePath,
            out string eoUrl)
        {
            eoUrl = null;
            if (!TryNormalizeUnsignedEoStreamBase(eoStreamBase, out var normalizedBase) ||
                !IsAllowedUnsignedResourcePath(resourcePath))
            {
                return false;
            }

            eoUrl = normalizedBase + resourcePath;
            return true;
        }

        internal static bool TryBuildProtectedOriginalRedirectUrl(
            PlaybackDirectUrlMode mode,
            string eoStreamBase,
            string mainStreamBase,
            string signedOpUrl,
            string unsignedResourcePath,
            out string outputUrl)
        {
            outputUrl = null;
            if (mode == PlaybackDirectUrlMode.Eo)
            {
                return TryBuildUnsignedEoUrl(
                    eoStreamBase,
                    unsignedResourcePath,
                    out outputUrl);
            }

            return mode == PlaybackDirectUrlMode.Main &&
                TryBuildOutputUrl(
                    PlaybackDirectUrlMode.Main,
                    mainStreamBase,
                    signedOpUrl,
                    out outputUrl);
        }

        private static bool TryNormalizeStreamBase(string text, out string normalized)
        {
            normalized = null;
            if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !IsAllowedStreamPath(uri.AbsolutePath.TrimEnd('/')))
            {
                return false;
            }

            normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') +
                uri.AbsolutePath.TrimEnd('/');
            return true;
        }

        private static bool IsAllowedStreamPath(string path)
        {
            return string.Equals(path, "/stream", StringComparison.Ordinal) ||
                string.Equals(path, "/cachefly-stream", StringComparison.Ordinal) ||
                string.Equals(path, "/eo-stream", StringComparison.Ordinal) ||
                string.Equals(path, "/main-stream", StringComparison.Ordinal);
        }

        private static bool TryNormalizeUnsignedEoStreamBase(
            string text,
            out string normalized)
        {
            normalized = null;
            if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            const string prefix = "/eo-stream/u-";
            if (!path.StartsWith(prefix, StringComparison.Ordinal) ||
                path.Length != prefix.Length + 32)
            {
                return false;
            }

            for (var index = prefix.Length; index < path.Length; index++)
            {
                var value = path[index];
                if (!((value >= '0' && value <= '9') ||
                      (value >= 'a' && value <= 'f')))
                {
                    return false;
                }
            }

            normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + path;
            return true;
        }

        private static bool IsAllowedUnsignedResourcePath(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                (!path.StartsWith("/google/", StringComparison.Ordinal) &&
                 !path.StartsWith("/openlist/", StringComparison.Ordinal)) ||
                path.IndexOfAny(new[] { '?', '#', '\\' }) >= 0 ||
                path.Any(char.IsControl))
            {
                return false;
            }

            var segments = path.Split('/');
            return !segments.Any(segment =>
                string.Equals(segment, ".", StringComparison.Ordinal) ||
                string.Equals(segment, "..", StringComparison.Ordinal));
        }

        private static bool TryParseSignedOpUrl(string text, out Uri signedUri)
        {
            return Uri.TryCreate(text, UriKind.Absolute, out signedUri) &&
                string.Equals(signedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(signedUri.Host) &&
                !string.IsNullOrEmpty(signedUri.AbsolutePath) &&
                string.IsNullOrEmpty(signedUri.UserInfo) &&
                signedUri.AbsolutePath.StartsWith("/v1-canary/", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(signedUri.Query) &&
                string.IsNullOrEmpty(signedUri.Fragment);
        }
    }
}
