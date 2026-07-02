namespace MediaInfoKeeper.Common
{
    using System;
    using System.Text.RegularExpressions;

    internal static class GitHubUpdateSource
    {
        public const string DefaultRepository = "xmm2022/MediaInfoKeeper";

        private static readonly Regex RepositoryPattern = new Regex(
            @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string NormalizeRepository(string repository)
        {
            var normalized = (repository ?? string.Empty).Trim();
            if (normalized.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("https://github.com/".Length);
            }
            else if (normalized.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("http://github.com/".Length);
            }

            normalized = normalized.Trim().Trim('/');
            if (string.IsNullOrWhiteSpace(normalized) || !RepositoryPattern.IsMatch(normalized))
            {
                return DefaultRepository;
            }

            return normalized;
        }

        public static string BuildReleaseApiUrl(string repository, int perPage, int page)
        {
            var normalized = NormalizeRepository(repository);
            return $"https://api.github.com/repos/{normalized}/releases?per_page={perPage}&page={page}";
        }

        public static string BuildVersionManifestUrl(string repository)
        {
            var normalized = NormalizeRepository(repository);
            return $"https://raw.githubusercontent.com/{normalized}/master/Version.json";
        }
    }
}
