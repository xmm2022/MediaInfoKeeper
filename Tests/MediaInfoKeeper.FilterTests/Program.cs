using MediaInfoKeeper.Common;
using MediaInfoKeeper.Options.Store;
using MediaInfoKeeper.Patch;
using MediaInfoKeeper.Services;
using System.Text.Json.Nodes;

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

var esaClients = EsaPlaybackDirectUrlPolicy.ParseClients("Hills; Infuse");
AssertTrue(
    EsaPlaybackDirectUrlPolicy.IsRequestEligible(true, "1", "Hills", esaClients),
    "ESA PlaybackInfo direct URL should require the protected marker and an exact client match");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.IsRequestEligible(true, null, "Hills", esaClients),
    "ESA PlaybackInfo direct URL must not run without the protected marker");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.IsRequestEligible(true, "1", "Hills Mobile", esaClients),
    "ESA PlaybackInfo client matching must be exact, not a substring match");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.IsRequestEligible(false, "1", "Hills", esaClients),
    "ESA PlaybackInfo direct URL must honor the feature switch");

var allClients = EsaPlaybackDirectUrlPolicy.ParseClients("*");
AssertTrue(
    EsaPlaybackDirectUrlPolicy.IsRequestEligible(true, "1", null, allClients),
    "the explicit wildcard should allow every client through a protected ESA entry");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.IsRequestEligible(true, null, null, allClients),
    "the wildcard must not bypass the protected ESA marker");

AssertTrue(
    EsaPlaybackDirectUrlPolicy.ResolveMode(
        true,
        "1",
        esaClients,
        true,
        null,
        esaClients,
        "Hills") == PlaybackDirectUrlMode.Esa,
    "the ESA marker should select only the ESA output mode");
AssertTrue(
    EsaPlaybackDirectUrlPolicy.ResolveMode(
        true,
        null,
        esaClients,
        true,
        "1",
        esaClients,
        "Hills") == PlaybackDirectUrlMode.Op,
    "the OP Direct marker should select only the native OP output mode");
AssertTrue(
    EsaPlaybackDirectUrlPolicy.ResolveMode(
        true,
        "1",
        esaClients,
        true,
        "1",
        esaClients,
        "Hills") == PlaybackDirectUrlMode.None,
    "ambiguous requests carrying both protected markers must fail closed");
AssertTrue(
    EsaPlaybackDirectUrlPolicy.ResolveMode(
        true,
        "1",
        allClients,
        true,
        "1",
        esaClients,
        "AfuseKt") == PlaybackDirectUrlMode.None,
    "ambiguous marker requests must fail closed even when only one client policy matches");

AssertTrue(
    EsaPlaybackDirectUrlPolicy.TryRebaseSignedUrl(
        "https://esa-canary.822211.xyz/stream/",
        "https://op.inemby.us.ci/v1-canary/1783900000/nonce/signature/google/audit/file.mkv",
        out var rebasedEsaUrl),
    "a canonical signed op path should rebase onto the dedicated ESA stream namespace");
AssertTrue(
    rebasedEsaUrl ==
        "https://esa-canary.822211.xyz/stream/v1-canary/1783900000/nonce/signature/google/audit/file.mkv",
    "ESA URL rebasing should preserve the full signed path");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.TryRebaseSignedUrl(
        "https://esa-canary.822211.xyz/control",
        "https://op.inemby.us.ci/v1-canary/1783900000/nonce/signature/google/audit/file.mkv",
        out _),
    "ESA direct URL must be confined to the /stream namespace");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.TryRebaseSignedUrl(
        "http://esa-canary.822211.xyz/stream",
        "https://op.inemby.us.ci/v1-canary/1783900000/nonce/signature/google/audit/file.mkv",
        out _),
    "ESA direct URL must require HTTPS");

AssertTrue(
    EsaPlaybackDirectUrlPolicy.TryBuildOutputUrl(
        PlaybackDirectUrlMode.Op,
        null,
        "https://op.inemby.us.ci/v1-canary/1783900000/nonce/signature/google/audit/file.mkv",
        out var directOpUrl),
    "OP Direct mode should accept a canonical HTTPS signed path");
AssertTrue(
    directOpUrl ==
        "https://op.inemby.us.ci/v1-canary/1783900000/nonce/signature/google/audit/file.mkv",
    "OP Direct mode must preserve the signer output without rebasing it");
AssertFalse(
    EsaPlaybackDirectUrlPolicy.TryBuildOutputUrl(
        PlaybackDirectUrlMode.Op,
        null,
        "https://op.inemby.us.ci/not-signed/file.mkv",
        out _),
    "OP Direct mode must reject paths outside the signed namespace");

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

var legacyEnhanceRoot = JsonNode.Parse(
    "{\"Enhance\":{\"EnableStrmDirectRedirect\":true,\"StrmDirectRedirectFollow302\":false}}")!.AsObject();
PluginOptionsJsonMigration.MigrateLegacyEnhanceOptions(legacyEnhanceRoot);
var migratedEnhance = legacyEnhanceRoot["Enhance"]!.AsObject();

AssertTrue(
    migratedEnhance["EnableStrmVideoDirectRedirect"]!.GetValue<bool>(),
    "legacy unified STRM redirect enable should migrate to video direct redirect");

AssertTrue(
    migratedEnhance["EnableStrmAudioDirectRedirect"]!.GetValue<bool>(),
    "legacy unified STRM redirect enable should migrate to audio direct redirect");

AssertFalse(
    migratedEnhance["StrmVideoDirectRedirectFollow302"]!.GetValue<bool>(),
    "legacy unified STRM follow-302 setting should migrate to video direct redirect");

AssertFalse(
    migratedEnhance["StrmAudioDirectRedirectFollow302"]!.GetValue<bool>(),
    "legacy unified STRM follow-302 setting should migrate to audio direct redirect");

var mixedEnhanceRoot = JsonNode.Parse(
    "{\"Enhance\":{\"EnableStrmDirectRedirect\":true,\"EnableStrmVideoDirectRedirect\":false,\"StrmDirectRedirectFollow302\":false,\"StrmVideoDirectRedirectFollow302\":true}}")!.AsObject();
PluginOptionsJsonMigration.MigrateLegacyEnhanceOptions(mixedEnhanceRoot);
var mixedEnhance = mixedEnhanceRoot["Enhance"]!.AsObject();

AssertFalse(
    mixedEnhance["EnableStrmVideoDirectRedirect"]!.GetValue<bool>(),
    "legacy STRM migration should not overwrite an explicit video direct redirect setting");

AssertTrue(
    mixedEnhance["EnableStrmAudioDirectRedirect"]!.GetValue<bool>(),
    "legacy STRM migration should still populate missing audio direct redirect setting");

AssertTrue(
    mixedEnhance["StrmVideoDirectRedirectFollow302"]!.GetValue<bool>(),
    "legacy STRM migration should not overwrite an explicit video follow-302 setting");

var signingKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
var signingNonce = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();

AssertTrue(
    OpSignedUrlSigner.Builder.TryCreate(
        signingKey,
        "https://op.inemby.us.ci",
        "src.inemby.us.ci:18080",
        21600,
        out var opBuilder,
        out var opBuilderError),
    "valid op signer configuration should be accepted: " + opBuilderError);

const string goldenLegacyUrl =
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/%E4%B8%AD%E6%96%87%20a%2Bb%2525.mkv";
const string goldenSignedUrl =
    "https://op.inemby.us.ci/v1-canary/1783900000/000102030405060708090a0b0c0d0e0f/" +
    "41157c0a4309dca49c2168ba5a5476746015eeebee41524000572d383f3208ac/" +
    "google/audit/%E4%B8%AD%E6%96%87%20a%2Bb%2525.mkv";

AssertTrue(
    opBuilder!.TryBuild(goldenLegacyUrl, 1783878400, signingNonce, out var actualGoldenSignedUrl),
    "valid src URL should produce an op signature");

AssertTrue(
    actualGoldenSignedUrl == goldenSignedUrl,
    "C# signer should match the v2 HMAC-SHA256 golden vector");

AssertTrue(
    opBuilder.TryBuild(
        "http://src.inemby.us.ci:18080/0123456789ABCDEF0123456789ABCDEF/openlist/audit/file.mkv",
        1783878400,
        signingNonce,
        out var openListSignedUrl) &&
    openListSignedUrl.Contains("/openlist/audit/file.mkv", StringComparison.Ordinal),
    "openlist resources should be signed");

foreach (var rejectedUrl in new[]
{
    "https://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/file.mkv",
    "http://src.inemby.us.ci:18081/0123456789abcdef0123456789abcdef/google/audit/file.mkv",
    "http://example.invalid:18080/0123456789abcdef0123456789abcdef/google/audit/file.mkv",
    "http://src.inemby.us.ci:18080/google/audit/file.mkv",
    "http://src.inemby.us.ci:18080/short/google/audit/file.mkv",
    "http://src.inemby.us.ci:18080/zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz/google/audit/file.mkv",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/other/audit/file.mkv",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/file.mkv?query=test-only",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/file.mkv#fragment",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/%2e%2e/file.mkv",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/a%2Fb.mkv",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/a%5Cb.mkv",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/%ZZ.mkv",
    "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/%FF.mkv",
    "https://tes.inemby.us.ci/archive/file.mkv",
    "https://op.inemby.us.ci/v1-canary/already-signed"
})
{
    AssertFalse(
        opBuilder.TryBuild(rejectedUrl, 1783878400, signingNonce, out _),
        "non-canonical or out-of-scope URL must not be signed: " +
        OpSignedUrlSigner.DescribeTarget(rejectedUrl));
}

AssertTrue(
    opBuilder.TryBuild(
        "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit//file.mkv",
        1783878400,
        signingNonce,
        out var repeatedSlashUrl) &&
    repeatedSlashUrl.EndsWith("/google/audit//file.mkv", StringComparison.Ordinal),
    "production signer preserves repeated slashes");

AssertTrue(
    opBuilder.TryBuild(
        "http://src.inemby.us.ci:18080/0123456789abcdef0123456789abcdef/google/audit/",
        1783878400,
        signingNonce,
        out var trailingSlashUrl) &&
    trailingSlashUrl.EndsWith("/google/audit/", StringComparison.Ordinal),
    "production signer preserves a trailing slash");

AssertFalse(
    OpSignedUrlSigner.Builder.TryCreate(
        new byte[31],
        "https://op.inemby.us.ci",
        "src.inemby.us.ci:18080",
        21600,
        out _,
        out _),
    "keys that are not exactly 32 bytes must be rejected");

AssertFalse(
    OpSignedUrlSigner.Builder.TryCreate(
        signingKey,
        "https://op.inemby.us.ci/path",
        "src.inemby.us.ci:18080",
        21600,
        out _,
        out _),
    "public base paths must be rejected");

AssertFalse(
    OpSignedUrlSigner.Builder.TryCreate(
        signingKey,
        "https://op.inemby.us.ci",
        "src.inemby.us.ci:18080",
        21601,
        out _,
        out _),
    "TTL above the verifier max-future limit must be rejected");

AssertTrue(
    OpSignedUrlSigner.DescribeTarget(goldenSignedUrl) == "https://op.inemby.us.ci",
    "redirect logging must omit the signed path");

AssertTrue(
    OpSignedUrlSigner.DescribeTarget("https://user:test-only@example.invalid:8443/path?query=test-only") ==
        "https://example.invalid:8443",
    "redirect logging must omit user info, path and query values");
