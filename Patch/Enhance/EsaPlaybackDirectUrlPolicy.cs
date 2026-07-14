#nullable disable

using System;
using System.Linq;

namespace MediaInfoKeeper.Patch
{
    internal enum PlaybackDirectUrlMode
    {
        None,
        Esa,
        Op
    }

    internal static class EsaPlaybackDirectUrlPolicy
    {
        internal const string MarkerHeader = "X-Esa-Proxy-Entry";
        internal const string OpMarkerHeader = "X-Op-Direct-Entry";
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
            string[] esaAllowedClients,
            bool opEnabled,
            string opMarkerValue,
            string[] opAllowedClients,
            string client)
        {
            var esaMarked = esaEnabled &&
                string.Equals(esaMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            var opMarked = opEnabled &&
                string.Equals(opMarkerValue?.Trim(), MarkerValue, StringComparison.Ordinal);
            if (esaMarked && opMarked)
            {
                return PlaybackDirectUrlMode.None;
            }

            var esaEligible = IsRequestEligible(
                esaEnabled,
                esaMarkerValue,
                client,
                esaAllowedClients);
            var opEligible = IsRequestEligible(
                opEnabled,
                opMarkerValue,
                client,
                opAllowedClients);

            if (esaEligible == opEligible)
            {
                return PlaybackDirectUrlMode.None;
            }

            return opEligible
                ? PlaybackDirectUrlMode.Op
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

            return mode == PlaybackDirectUrlMode.Esa &&
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

        private static bool TryNormalizeStreamBase(string text, out string normalized)
        {
            normalized = null;
            if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.Equals(uri.AbsolutePath.TrimEnd('/'), "/stream", StringComparison.Ordinal))
            {
                return false;
            }

            normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/stream";
            return true;
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
