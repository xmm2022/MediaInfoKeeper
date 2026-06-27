using System;
using System.Linq;

namespace MediaInfoKeeper.Patch
{
    internal static class StrmDirectRedirectUrlFilter
    {
        public static string[] ParsePatterns(string text)
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

        public static bool IsAllowed(string url, string[] allowlist, string[] blocklist)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (MatchesAny(url, blocklist))
            {
                return false;
            }

            return allowlist == null ||
                allowlist.Length == 0 ||
                MatchesAny(url, allowlist);
        }

        private static bool MatchesAny(string url, string[] patterns)
        {
            return patterns != null &&
                patterns.Any(pattern =>
                    !string.IsNullOrWhiteSpace(pattern) &&
                    url.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
        }
    }
}
