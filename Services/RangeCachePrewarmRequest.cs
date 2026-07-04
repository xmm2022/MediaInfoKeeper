using System;
using System.Text.Json;

namespace MediaInfoKeeper.Services
{
    internal sealed class RangeCachePrewarmRequest
    {
        private RangeCachePrewarmRequest(string url, string secret, string bodyJson)
        {
            Url = url;
            Secret = secret;
            BodyJson = bodyJson;
        }

        public string Url { get; }

        public string Secret { get; }

        public string BodyJson { get; }

        public static bool TryCreate(
            string endpoint,
            string secret,
            string itemId,
            string mediaSourceId,
            out RangeCachePrewarmRequest request)
        {
            request = null!;

            var normalizedEndpoint = NormalizeEndpoint(endpoint);
            var normalizedSecret = secret?.Trim();
            var normalizedItemId = itemId?.Trim();
            var normalizedMediaSourceId = mediaSourceId?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedEndpoint) ||
                string.IsNullOrWhiteSpace(normalizedSecret) ||
                string.IsNullOrWhiteSpace(normalizedItemId) ||
                string.IsNullOrWhiteSpace(normalizedMediaSourceId))
            {
                return false;
            }

            if (!Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out var uri) ||
                (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var bodyJson = JsonSerializer.Serialize(new
            {
                itemId = normalizedItemId,
                mediaSourceId = normalizedMediaSourceId
            });
            request = new RangeCachePrewarmRequest(normalizedEndpoint, normalizedSecret, bodyJson);
            return true;
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            return string.IsNullOrWhiteSpace(endpoint)
                ? string.Empty
                : endpoint.Trim().TrimEnd('/');
        }
    }
}
