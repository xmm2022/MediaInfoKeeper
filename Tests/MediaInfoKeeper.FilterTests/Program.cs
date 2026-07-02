using MediaInfoKeeper.Patch;

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool condition, string message)
{
    AssertTrue(!condition, message);
}

var allowlist = StrmDirectRedirectUrlFilter.ParsePatterns(
    "http://82.47.35.45:5244/; http://example.test/media/");
var blocklist = StrmDirectRedirectUrlFilter.ParsePatterns(
    "http://127.0.0.1:18096/\nhttp://localhost:18096/");

AssertTrue(
    StrmDirectRedirectUrlFilter.IsAllowed(
        "http://82.47.35.45:5244/d/yidon/movie.mp4",
        allowlist,
        blocklist),
    "allowlist prefix should enable direct redirect");

AssertFalse(
    StrmDirectRedirectUrlFilter.IsAllowed(
        "http://127.0.0.1:18096/gdredir-admin/movie.mp4",
        allowlist,
        blocklist),
    "blocklist prefix should force Emby relay even when STRM direct redirect is enabled");

AssertFalse(
    StrmDirectRedirectUrlFilter.IsAllowed(
        "http://unlisted.example/movie.mp4",
        allowlist,
        blocklist),
    "non-allowlisted URL should force Emby relay when an allowlist is configured");

AssertTrue(
    StrmDirectRedirectUrlFilter.IsAllowed(
        "http://anything.example/movie.mp4",
        Array.Empty<string>(),
        Array.Empty<string>()),
    "empty allowlist and blocklist should preserve existing unrestricted redirect behavior");
