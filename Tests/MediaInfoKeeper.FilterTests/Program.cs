using MediaInfoKeeper.Common;
using MediaInfoKeeper.Patch;
using MediaInfoKeeper.Services;

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

AssertTrue(
    GitHubUpdateSource.NormalizeRepository(string.Empty) == "xmm2022/MediaInfoKeeper",
    "default update repository should point to the fork");

AssertTrue(
    GitHubUpdateSource.NormalizeRepository(" https://github.com/xmm2022/MediaInfoKeeper/ ") == "xmm2022/MediaInfoKeeper",
    "GitHub repository URLs should normalize to owner/repo");

AssertTrue(
    GitHubUpdateSource.BuildReleaseApiUrl(string.Empty, 5, 1) ==
    "https://api.github.com/repos/xmm2022/MediaInfoKeeper/releases?per_page=5&page=1",
    "default release API URL should point to the fork releases");

AssertTrue(
    GitHubUpdateSource.BuildVersionManifestUrl("honue/MediaInfoKeeper") ==
    "https://raw.githubusercontent.com/honue/MediaInfoKeeper/master/Version.json",
    "configured repository should control the Version.json URL");

AssertFalse(
    RangeCachePrewarmRequest.TryCreate(
        "",
        "secret",
        "1",
        "ms1",
        out _),
    "range cache prewarm should be disabled without endpoint");

AssertFalse(
    RangeCachePrewarmRequest.TryCreate(
        "http://127.0.0.1:18180/internal/prewarm",
        "",
        "1",
        "ms1",
        out _),
    "range cache prewarm should be disabled without secret");

AssertTrue(
    RangeCachePrewarmRequest.TryCreate(
        "http://127.0.0.1:18180/internal/prewarm/",
        "secret",
        " 1 ",
        " ms1 ",
        out var prewarmRequest),
    "range cache prewarm request should be created for complete config");

AssertTrue(
    prewarmRequest.Url == "http://127.0.0.1:18180/internal/prewarm",
    "range cache prewarm endpoint should trim whitespace and trailing slash");

AssertTrue(
    prewarmRequest.Secret == "secret",
    "range cache prewarm secret should be preserved for header auth");

AssertTrue(
    prewarmRequest.BodyJson == "{\"itemId\":\"1\",\"mediaSourceId\":\"ms1\"}",
    "range cache prewarm body should use proxy itemId/mediaSourceId contract");

AssertTrue(
    RangeCachePrewarmTriggerPolicy.ShouldTriggerAfterItemAdded(hasMediaInfo: true, restoredMediaInfo: false),
    "item-added prewarm should trigger when MediaInfo is already available");

AssertTrue(
    RangeCachePrewarmTriggerPolicy.ShouldTriggerAfterItemAdded(hasMediaInfo: false, restoredMediaInfo: true),
    "item-added prewarm should trigger when MediaInfo was restored from JSON");

AssertFalse(
    RangeCachePrewarmTriggerPolicy.ShouldTriggerAfterItemAdded(hasMediaInfo: false, restoredMediaInfo: false),
    "item-added prewarm should wait until MediaInfo is available");
