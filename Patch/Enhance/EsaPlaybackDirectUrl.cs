#nullable disable

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MediaInfoKeeper.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Services;

namespace MediaInfoKeeper.Patch
{
    /// <summary>
    /// 在受保护的 canary PlaybackInfo 请求中，把可直放 STRM 的 DirectStreamUrl
    /// 改为 ESA 同域地址或原生 OP 签名地址，避免播放器的每个 Range 再经过 Emby 302。
    /// </summary>
    public static class EsaPlaybackDirectUrl
    {
        private static readonly object InitLock = new object();

        private static Harmony harmony;
        private static ILogger logger;
        private static bool isEsaEnabled;
        private static bool isOpEnabled;
        private static bool isMainEnabled;
        private static bool isPatched;
        private static string esaStreamBase;
        private static string cacheFlyStreamBase;
        private static string cacheFlyHlsBase;
        private static bool isCacheFlyHlsEnabled;
        private static string eoStreamBase;
        private static string mainStreamBase;
        private static string[] esaAllowedClients = Array.Empty<string>();
        private static string[] opAllowedClients = Array.Empty<string>();
        private static string[] mainAllowedClients = Array.Empty<string>();
        private static MethodInfo playbackInfoEntry;

        private static bool IsAnyEnabled => isEsaEnabled || isOpEnabled || isMainEnabled;

        public static bool IsReady => harmony != null && (!IsAnyEnabled || isPatched);

        public static void Initialize(
            ILogger pluginLogger,
            bool esaEnabled,
            string streamBase,
            string cacheFlyBase,
            string cacheFlyHlsPlaybackBase,
            string cacheFlyProtectServeKeyFile,
            string eoBase,
            string esaClientAllowlist,
            bool opEnabled,
            string opClientAllowlist,
            bool mainEnabled,
            string mainBase,
            string mainClientAllowlist)
        {
            lock (InitLock)
            {
                logger = pluginLogger;
                SetOptions(
                    esaEnabled,
                    streamBase,
                    cacheFlyBase,
                    cacheFlyHlsPlaybackBase,
                    cacheFlyProtectServeKeyFile,
                    eoBase,
                    esaClientAllowlist,
                    opEnabled,
                    opClientAllowlist,
                    mainEnabled,
                    mainBase,
                    mainClientAllowlist);
                if (harmony != null)
                {
                    Configure(
                        esaEnabled,
                        streamBase,
                        cacheFlyBase,
                        cacheFlyHlsPlaybackBase,
                        cacheFlyProtectServeKeyFile,
                        eoBase,
                        esaClientAllowlist,
                        opEnabled,
                        opClientAllowlist,
                        mainEnabled,
                        mainBase,
                        mainClientAllowlist);
                    return;
                }

                try
                {
                    var mediaEncoding = Assembly.Load("Emby.Server.MediaEncoding");
                    var serviceType = mediaEncoding?.GetType("Emby.Server.MediaEncoding.Api.MediaInfoService");
                    var requestType = mediaEncoding?.GetType("Emby.Server.MediaEncoding.Api.GetPostedPlaybackInfo");
                    if (serviceType == null || requestType == null)
                    {
                        PatchLog.InitFailed(logger, nameof(EsaPlaybackDirectUrl), "未找到 MediaInfoService/GetPostedPlaybackInfo 类型");
                        return;
                    }

                    playbackInfoEntry = PatchMethodResolver.Resolve(
                        serviceType,
                        mediaEncoding.GetName().Version,
                        new MethodSignatureProfile
                        {
                            Name = "esa-playback-direct-url-entry-exact",
                            MethodName = "GetPlaybackInfo",
                            BindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            ParameterTypes = new[]
                            {
                                requestType,
                                typeof(bool),
                                typeof(string),
                                typeof(CancellationToken)
                            },
                            ReturnType = typeof(Task<>).MakeGenericType(typeof(PlaybackInfoResponse))
                        },
                        logger,
                        "EsaPlaybackDirectUrl.MediaInfoService.GetPlaybackInfo");

                    if (playbackInfoEntry == null)
                    {
                        PatchLog.InitFailed(logger, nameof(EsaPlaybackDirectUrl), "未找到 GetPlaybackInfo");
                        return;
                    }

                    harmony = new Harmony("mediainfokeeper.esaplaybackdirecturl");
                    PatchLog.Patched(logger, nameof(EsaPlaybackDirectUrl), playbackInfoEntry);
                    if (IsAnyEnabled)
                    {
                        Patch();
                    }
                }
                catch (Exception ex)
                {
                    PatchLog.InitFailed(logger, nameof(EsaPlaybackDirectUrl), ex.Message);
                    logger?.Error("EsaPlaybackDirectUrl 初始化异常：{0}", ex);
                    harmony = null;
                    isPatched = false;
                }
            }
        }

        public static void Configure(
            bool esaEnabled,
            string streamBase,
            string cacheFlyBase,
            string cacheFlyHlsPlaybackBase,
            string cacheFlyProtectServeKeyFile,
            string eoBase,
            string esaClientAllowlist,
            bool opEnabled,
            string opClientAllowlist,
            bool mainEnabled,
            string mainBase,
            string mainClientAllowlist)
        {
            lock (InitLock)
            {
                SetOptions(
                    esaEnabled,
                    streamBase,
                    cacheFlyBase,
                    cacheFlyHlsPlaybackBase,
                    cacheFlyProtectServeKeyFile,
                    eoBase,
                    esaClientAllowlist,
                    opEnabled,
                    opClientAllowlist,
                    mainEnabled,
                    mainBase,
                    mainClientAllowlist);
                if (harmony == null)
                {
                    return;
                }

                if (IsAnyEnabled)
                {
                    Patch();
                }
                else
                {
                    Unpatch();
                }
            }
        }

        private static void SetOptions(
            bool esaEnabled,
            string streamBase,
            string cacheFlyBase,
            string cacheFlyHlsPlaybackBase,
            string cacheFlyProtectServeKeyFile,
            string eoBase,
            string esaClientAllowlist,
            bool opEnabled,
            string opClientAllowlist,
            bool mainEnabled,
            string mainBase,
            string mainClientAllowlist)
        {
            isEsaEnabled = esaEnabled;
            isOpEnabled = opEnabled;
            isMainEnabled = mainEnabled;
            esaStreamBase = streamBase?.Trim();
            cacheFlyStreamBase = cacheFlyBase?.Trim();
            cacheFlyHlsBase = cacheFlyHlsPlaybackBase?.Trim();
            eoStreamBase = eoBase?.Trim();
            mainStreamBase = mainBase?.Trim();
            esaAllowedClients = EsaPlaybackDirectUrlPolicy.ParseClients(esaClientAllowlist);
            opAllowedClients = EsaPlaybackDirectUrlPolicy.ParseClients(opClientAllowlist);
            mainAllowedClients = EsaPlaybackDirectUrlPolicy.ParseClients(mainClientAllowlist);
            var hlsRequested = esaEnabled && !string.IsNullOrWhiteSpace(cacheFlyHlsBase);
            var hlsConfigured = CacheFlyProtectServeSigner.Configure(
                hlsRequested,
                cacheFlyProtectServeKeyFile,
                cacheFlyHlsBase,
                6 * 60 * 60,
                out var hlsError);
            isCacheFlyHlsEnabled = hlsRequested && hlsConfigured;
            if (!hlsConfigured && hlsRequested)
            {
                logger?.Warn("CacheFly HLS canary 已禁用: {0}", hlsError);
            }
        }

        private static void Patch()
        {
            if (isPatched || harmony == null || playbackInfoEntry == null)
            {
                return;
            }

            harmony.Patch(
                playbackInfoEntry,
                prefix: new HarmonyMethod(typeof(EsaPlaybackDirectUrl), nameof(GetPlaybackInfoPrefix)),
                postfix: new HarmonyMethod(typeof(EsaPlaybackDirectUrl), nameof(GetPlaybackInfoPostfix)));
            isPatched = true;
        }

        private static void Unpatch()
        {
            if (!isPatched || harmony == null || playbackInfoEntry == null)
            {
                return;
            }

            harmony.Unpatch(playbackInfoEntry, HarmonyPatchType.All, harmony.Id);
            isPatched = false;
        }

        [HarmonyPrefix]
        private static void GetPlaybackInfoPrefix(
            object __instance,
            object __0)
        {
            if (!IsAnyEnabled || __instance == null || __0 == null)
            {
                return;
            }

            try
            {
                if (!GetPropertyValue<bool>(__0, "IsPlayback"))
                {
                    return;
                }

                var requestContext = GetPropertyValue<IRequest>(__instance, "Request");
                var client = ResolveClient(requestContext);
                var mode = ResolveMode(requestContext, client);
                if (mode != PlaybackDirectUrlMode.CacheFlyHls ||
                    !IsModeEnabled(mode) ||
                    !EsaPlaybackDirectUrlPolicy.IsPlaybackInfoDirectUrlCompatible(client))
                {
                    return;
                }

                var itemId = GetPropertyValue<string>(__0, "Id");
                var item = string.IsNullOrWhiteSpace(itemId)
                    ? null
                    : Plugin.LibraryManager?.GetItemById(itemId);
                var strmPath = item?.Path ?? item?.FileName;
                if (!LibraryService.IsFileShortcut(strmPath))
                {
                    return;
                }

                PrepareCacheFlyHlsRequest(__0);
                logger?.Info(
                    "EsaPlaybackDirectUrl: 已强制 CacheFly HLS remux。itemId={0}, client={1}",
                    itemId,
                    client);
            }
            catch (Exception ex)
            {
                logger?.Warn("CacheFly HLS 请求准备失败，保留客户端原能力: {0}", ex.Message);
            }
        }

        [HarmonyPostfix]
        private static void GetPlaybackInfoPostfix(
            object __instance,
            object __0,
            ref Task<PlaybackInfoResponse> __result)
        {
            if (!IsAnyEnabled || __result == null || __instance == null || __0 == null)
            {
                return;
            }

            try
            {
                if (!GetPropertyValue<bool>(__0, "IsPlayback"))
                {
                    return;
                }

                var requestContext = GetPropertyValue<IRequest>(__instance, "Request");
                var client = ResolveClient(requestContext);
                var mode = ResolveMode(requestContext, client);
                if (mode == PlaybackDirectUrlMode.None || !IsModeEnabled(mode))
                {
                    return;
                }

                if (!EsaPlaybackDirectUrlPolicy.IsPlaybackInfoDirectUrlCompatible(client))
                {
                    logger?.Info(
                        "EsaPlaybackDirectUrl: Hills Windows 兼容模式，保留 Emby 原始流地址。itemId={0}, mode={1}",
                        GetPropertyValue<string>(__0, "Id"),
                        mode);
                    return;
                }

                var itemId = GetPropertyValue<string>(__0, "Id");
                var item = string.IsNullOrWhiteSpace(itemId)
                    ? null
                    : Plugin.LibraryManager?.GetItemById(itemId);
                var strmPath = item?.Path ?? item?.FileName;
                if (!LibraryService.IsFileShortcut(strmPath))
                {
                    return;
                }

                var capturedBase = ResolveStreamBase(mode);
                __result = RewriteAsync(__result, itemId, client, capturedBase, mode);
            }
            catch (Exception ex)
            {
                logger?.Warn("EsaPlaybackDirectUrl 预判失败，保留 Emby 原播放地址: {0}", ex.Message);
            }
        }

        private static async Task<PlaybackInfoResponse> RewriteAsync(
            Task<PlaybackInfoResponse> task,
            string itemId,
            string client,
            string streamBase,
            PlaybackDirectUrlMode mode)
        {
            var response = await task.ConfigureAwait(false);
            if (!IsModeEnabled(mode) || response?.MediaSources == null)
            {
                return response;
            }

            foreach (var source in response.MediaSources)
            {
                TryRewrite(source, itemId, client, streamBase, mode);
            }

            return response;
        }

        private static void TryRewrite(
            MediaSourceInfo source,
            string itemId,
            string client,
            string streamBase,
            PlaybackDirectUrlMode mode)
        {
            try
            {
                if (mode == PlaybackDirectUrlMode.CacheFlyHls)
                {
                    TryRewriteCacheFlyHls(source, itemId, client);
                    return;
                }

                if (!CanRewrite(source))
                {
                    return;
                }

                string outputUrl;
                var unsignedEo = mode == PlaybackDirectUrlMode.Eo &&
                    EsaPlaybackDirectUrlPolicy.IsUnsignedEoStreamBase(streamBase);
                if (unsignedEo)
                {
                    if (!OpSignedUrlSigner.TryBuildUnsignedResourcePath(
                            source.Path,
                            out var resourcePath) ||
                        !EsaPlaybackDirectUrlPolicy.TryBuildUnsignedEoUrl(
                            streamBase,
                            resourcePath,
                            out outputUrl))
                    {
                        return;
                    }
                }
                else if (!OpSignedUrlSigner.TryBuild(source.Path, out var signedUrl) ||
                         !EsaPlaybackDirectUrlPolicy.TryBuildOutputUrl(
                             mode,
                             streamBase,
                             signedUrl,
                             out outputUrl))
                {
                    return;
                }

                source.DirectStreamUrl = outputUrl;
                source.AddApiKeyToDirectStreamUrl = false;
                logger?.Info(
                    "EsaPlaybackDirectUrl: 已注入 {0} 直链。itemId={1}, client={2}, target={3}",
                    unsignedEo ? "EoUnsigned" : mode.ToString(),
                    itemId,
                    client,
                    OpSignedUrlSigner.DescribeTarget(outputUrl));
            }
            catch (Exception ex)
            {
                logger?.Warn(
                    "EsaPlaybackDirectUrl 改写失败，保留 Emby 原播放地址。itemId={0}, error={1}",
                    itemId,
                    ex.Message);
            }
        }

        private static bool IsModeEnabled(PlaybackDirectUrlMode mode)
        {
            if (mode == PlaybackDirectUrlMode.CacheFlyHls)
            {
                return isEsaEnabled && isCacheFlyHlsEnabled;
            }

            return mode == PlaybackDirectUrlMode.Esa ||
                mode == PlaybackDirectUrlMode.CacheFly ||
                mode == PlaybackDirectUrlMode.Eo
                ? isEsaEnabled
                : mode == PlaybackDirectUrlMode.Op
                    ? isOpEnabled
                    : mode == PlaybackDirectUrlMode.Main && isMainEnabled;
        }

        private static string ResolveStreamBase(PlaybackDirectUrlMode mode)
        {
            if (mode == PlaybackDirectUrlMode.CacheFlyHls)
            {
                return cacheFlyHlsBase;
            }

            return mode == PlaybackDirectUrlMode.CacheFly
                ? cacheFlyStreamBase
                : mode == PlaybackDirectUrlMode.Eo
                    ? eoStreamBase
                    : mode == PlaybackDirectUrlMode.Main
                        ? mainStreamBase
                        : esaStreamBase;
        }

        private static bool CanRewrite(MediaSourceInfo source)
        {
            return source != null &&
                source.SupportsDirectPlay &&
                source.IsRemote &&
                source.Protocol == MediaProtocol.Http &&
                !source.IsInfiniteStream &&
                !source.RequiresOpening &&
                !source.RequiresClosing &&
                (source.RequiredHttpHeaders == null || source.RequiredHttpHeaders.Count == 0) &&
                Uri.TryCreate(source.Path, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveClient(IRequest requestContext)
        {
            if (requestContext == null)
            {
                return null;
            }

            // Prefer the canonical Emby protocol header. Some clients (notably
            // AfuseKt) decorate Session.Client with platform text containing a
            // semicolon, which cannot be represented in the configured client
            // list because semicolons are valid list separators.
            var headerClient = requestContext.Headers?["X-Emby-Client"]?.Trim();
            if (!string.IsNullOrWhiteSpace(headerClient))
            {
                return headerClient;
            }

            var sessionContext = Plugin.Instance?.AppHost?.Resolve<ISessionContext>();
            var client = sessionContext?.GetSession(requestContext)?.Client?.Trim();
            return string.IsNullOrWhiteSpace(client) ? null : client;
        }

        private static PlaybackDirectUrlMode ResolveMode(IRequest requestContext, string client)
        {
            return EsaPlaybackDirectUrlPolicy.ResolveMode(
                isEsaEnabled,
                requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.MarkerHeader],
                requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.CacheFlyMarkerHeader],
                requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.CacheFlyHlsMarkerHeader],
                requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.EoMarkerHeader],
                esaAllowedClients,
                isOpEnabled,
                requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.OpMarkerHeader],
                opAllowedClients,
                isMainEnabled,
                requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.MainMarkerHeader],
                mainAllowedClients,
                client);
        }

        private static void PrepareCacheFlyHlsRequest(object request)
        {
            var requestedBitrate = GetPropertyValue<long?>(request, "MaxStreamingBitrate");
            var maxBitrate = requestedBitrate.HasValue && requestedBitrate.Value > 0
                ? Math.Min(requestedBitrate.Value, 120000000L)
                : 120000000L;
            var profile = new DeviceProfile
            {
                Name = "CacheFly HLS Canary",
                Id = "cachefly-hls-canary",
                SupportedMediaTypes = "Video",
                MaxStreamingBitrate = maxBitrate,
                DirectPlayProfiles = Array.Empty<DirectPlayProfile>(),
                TranscodingProfiles = new[]
                {
                    new TranscodingProfile
                    {
                        Container = "ts",
                        Type = DlnaProfileType.Video,
                        VideoCodec = "h264,hevc",
                        AudioCodec = "aac,ac3,eac3,mp3",
                        Protocol = "hls",
                        Context = EncodingContext.Streaming,
                        MaxAudioChannels = "8",
                        MinSegments = 2,
                        SegmentLength = 6,
                        BreakOnNonKeyFrames = true,
                        AllowInterlacedVideoStreamCopy = true
                    }
                },
                ContainerProfiles = Array.Empty<ContainerProfile>(),
                CodecProfiles = Array.Empty<CodecProfile>(),
                ResponseProfiles = Array.Empty<ResponseProfile>(),
                SubtitleProfiles = Array.Empty<SubtitleProfile>()
            };

            SetPropertyValue(request, "DeviceProfile", profile);
            SetPropertyValue(request, "MaxStreamingBitrate", (long?)maxBitrate);
            SetPropertyValue(request, "EnableDirectPlay", false);
            SetPropertyValue(request, "EnableDirectStream", true);
            SetPropertyValue(request, "EnableTranscoding", true);
            SetPropertyValue(request, "AllowVideoStreamCopy", true);
            SetPropertyValue(request, "AllowInterlacedVideoStreamCopy", true);
            SetPropertyValue(request, "AllowAudioStreamCopy", true);
        }

        private static void TryRewriteCacheFlyHls(
            MediaSourceInfo source,
            string itemId,
            string client)
        {
            if (source == null ||
                string.IsNullOrWhiteSpace(source.TranscodingUrl) ||
                !CacheFlyProtectServeSigner.TryBuild(
                    source.TranscodingUrl,
                    itemId,
                    out var signedUrl,
                    out var renditionHash))
            {
                return;
            }

            source.SupportsDirectPlay = false;
            source.SupportsDirectStream = true;
            source.SupportsTranscoding = true;
            source.DirectStreamUrl = signedUrl;
            source.TranscodingUrl = signedUrl;
            source.AddApiKeyToDirectStreamUrl = false;
            logger?.Info(
                "EsaPlaybackDirectUrl: 已注入 CacheFly HLS。itemId={0}, client={1}, rendition={2}, target={3}",
                itemId,
                client,
                renditionHash,
                OpSignedUrlSigner.DescribeTarget(signedUrl));
        }

        private static T GetPropertyValue<T>(object target, string propertyName)
        {
            if (target == null)
            {
                return default;
            }

            var property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var value = property?.GetValue(target);
            return value is T typed ? typed : default;
        }

        private static void SetPropertyValue<T>(object target, string propertyName, T value)
        {
            var property = target?.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite)
            {
                throw new MissingMemberException(target?.GetType().FullName, propertyName);
            }

            property.SetValue(target, value);
        }
    }
}
