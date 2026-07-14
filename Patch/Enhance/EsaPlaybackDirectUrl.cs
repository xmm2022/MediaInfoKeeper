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
        private static bool isPatched;
        private static string esaStreamBase;
        private static string[] esaAllowedClients = Array.Empty<string>();
        private static string[] opAllowedClients = Array.Empty<string>();
        private static MethodInfo playbackInfoEntry;

        private static bool IsAnyEnabled => isEsaEnabled || isOpEnabled;

        public static bool IsReady => harmony != null && (!IsAnyEnabled || isPatched);

        public static void Initialize(
            ILogger pluginLogger,
            bool esaEnabled,
            string streamBase,
            string esaClientAllowlist,
            bool opEnabled,
            string opClientAllowlist)
        {
            lock (InitLock)
            {
                logger = pluginLogger;
                SetOptions(
                    esaEnabled,
                    streamBase,
                    esaClientAllowlist,
                    opEnabled,
                    opClientAllowlist);
                if (harmony != null)
                {
                    Configure(
                        esaEnabled,
                        streamBase,
                        esaClientAllowlist,
                        opEnabled,
                        opClientAllowlist);
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
            string esaClientAllowlist,
            bool opEnabled,
            string opClientAllowlist)
        {
            lock (InitLock)
            {
                SetOptions(
                    esaEnabled,
                    streamBase,
                    esaClientAllowlist,
                    opEnabled,
                    opClientAllowlist);
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
            string esaClientAllowlist,
            bool opEnabled,
            string opClientAllowlist)
        {
            isEsaEnabled = esaEnabled;
            isOpEnabled = opEnabled;
            esaStreamBase = streamBase?.Trim();
            esaAllowedClients = EsaPlaybackDirectUrlPolicy.ParseClients(esaClientAllowlist);
            opAllowedClients = EsaPlaybackDirectUrlPolicy.ParseClients(opClientAllowlist);
        }

        private static void Patch()
        {
            if (isPatched || harmony == null || playbackInfoEntry == null)
            {
                return;
            }

            harmony.Patch(
                playbackInfoEntry,
                postfix: new HarmonyMethod(typeof(EsaPlaybackDirectUrl), nameof(GetPlaybackInfoPostfix)));
            isPatched = true;
        }

        private static void Unpatch()
        {
            if (!isPatched || harmony == null || playbackInfoEntry == null)
            {
                return;
            }

            harmony.Unpatch(playbackInfoEntry, HarmonyPatchType.Postfix, harmony.Id);
            isPatched = false;
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
                var mode = EsaPlaybackDirectUrlPolicy.ResolveMode(
                    isEsaEnabled,
                    requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.MarkerHeader],
                    esaAllowedClients,
                    isOpEnabled,
                    requestContext?.Headers?[EsaPlaybackDirectUrlPolicy.OpMarkerHeader],
                    opAllowedClients,
                    client);
                if (mode == PlaybackDirectUrlMode.None)
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

                var capturedBase = esaStreamBase;
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
                if (!CanRewrite(source) ||
                    !OpSignedUrlSigner.TryBuild(source.Path, out var signedUrl) ||
                    !EsaPlaybackDirectUrlPolicy.TryBuildOutputUrl(
                        mode,
                        streamBase,
                        signedUrl,
                        out var outputUrl))
                {
                    return;
                }

                source.DirectStreamUrl = outputUrl;
                source.AddApiKeyToDirectStreamUrl = false;
                logger?.Info(
                    "EsaPlaybackDirectUrl: 已注入 {0} 直链。itemId={1}, client={2}, target={3}",
                    mode,
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
            return mode == PlaybackDirectUrlMode.Esa
                ? isEsaEnabled
                : mode == PlaybackDirectUrlMode.Op && isOpEnabled;
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
    }
}
