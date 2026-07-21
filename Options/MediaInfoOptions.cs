using System.ComponentModel;
using System.IO;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Editors;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.GenericEdit;

namespace MediaInfoKeeper.Options
{
    public class MediaInfoOptions : EditableOptionsBase
    {
        public override string EditorTitle => "媒体信息";

        public override string EditorDescription => string.Empty;

        [DisplayName("入库时提取媒体信息")]
        [Description("入库时若 JSON 不存在或恢复失败，提取媒体信息并写入 JSON。")]
        public bool ExtractMediaInfoOnItemAdded { get; set; } = true;
        
        [DisplayName("条目移除时删除 JSON")]
        [Description("启用后，条目移除时删除已持久化的 JSON。")]
        public bool DeleteMediaInfoJsonOnRemove { get; set; } = false;

        [DisplayName("启用 MediaInfo 预加载")]
        [Description("播放剧集时，预加载下一集媒体信息；关闭后不再自动预加载。")]
        public bool EnableMediaInfoPrefetch { get; set; } = true;

        [DisplayName("浏览剧集提取媒体信息")]
        [Description("浏览视频或音频详情接口时，若条目没有媒体信息，则后台提取并写入 JSON。")]
        public bool ExtractMediaInfoOnItemDetail { get; set; } = false;

        [DisplayName("启用 Range Cache")]
        [Description("Range Cache 远程总开关。关闭后所有播放请求旁路缓存，并停止新建缓存和预热；不会删除已有缓存。")]
        public bool EnableRangeCache { get; set; } = true;

        [DisplayName("提取成功后触发 Range Cache 预热")]
        [Description("仅在入库、快捷菜单或计划任务提取/恢复成功后预热 head/tail；浏览详情和 MediaInfo 预加载不触发。默认关闭。")]
        public bool EnableRangeCachePrewarm { get; set; } = false;

        [DisplayName("播放时预热下一集 Range Cache")]
        [Description("开始播放剧集 5 秒后，独立预热下一集 head/tail。此功能不依赖下一集是否需要提取 MediaInfo。默认关闭。")]
        public bool EnableNextEpisodeRangeCachePrewarm { get; set; } = false;

        [DisplayName("Range Cache 预热接口")]
        [Description("range-cache-proxy 内部接口地址。默认使用本机 loopback，不经过公网入口。")]
        public string RangeCachePrewarmEndpoint { get; set; } = "http://127.0.0.1:18180/internal/prewarm";

        [DisplayName("Range Cache 控制接口")]
        [Description("用于切换远程 Range Cache 总开关。默认通过本机预热桥转发，不直接连接远端 Go 服务。")]
        public string RangeCacheControlEndpoint { get; set; } = "http://127.0.0.1:18180/internal/cache-mode";

        [DisplayName("Range Cache 预热密钥")]
        [Description("发送到 X-Range-Cache-Prewarm-Key 的内部密钥，应与 range-cache-proxy 的 prewarm_api_key 一致。")]
        public string RangeCachePrewarmSecret { get; set; } = string.Empty;

        [DisplayName("MediaInfo JSON 存储路径模板")]
        [Description("默认保存到 Emby 的 /config/data/MediaInfoKeeper。留空时保存到媒体文件同目录。支持路径模板，详见 https://github.com/honue/MediaInfoKeeper/wiki")]
        [EditFolderPicker]
        public string MediaInfoJsonRootFolder { get; set; } = GetDefaultMediaInfoJsonRootFolder();

        [DisplayName("提取尝试次数")]
        [Description("媒体信息刷新后仍检测不到音频或视频流时的最大尝试次数，包含首次提取。")]
        [MinValue(1), MaxValue(10)]
        public int ExtractMediaInfoAttemptCount { get; set; } = 3;

        [DisplayName("提取任务并发数")]
        [Description("设置媒体信息提取的最大并发数，修改后重启生效，默认 1。")]
        [MinValue(1), MaxValue(20)]
        public int MaxConcurrentCount { get; set; } = 1;

        public void Initialize()
        {
        }

        public override IEditObjectContainer CreateEditContainer()
        {
            var container = (EditObjectContainer)base.CreateEditContainer();
            var root = container.EditorRoot;
            if (root?.EditorItems == null || root.EditorItems.Length == 0)
            {
                return container;
            }

            root.EditorItems = new EditorBase[]
            {
                new EditorGroup("媒体信息", root.EditorItems, "group1", root.Id, null)
                {
                    Description = "插件会持续监听 .strm 文件内容变更，并阻止 Emby 系统 ffprobe/ffmpeg 运行；仅在插件内部需要提取媒体信息时按需放行。"
                }
            };

            return container;
        }

        internal static string GetDefaultMediaInfoJsonRootFolder()
        {
            try
            {
                var programDataPath = Plugin.Instance?.AppHost?.Resolve<IApplicationPaths>()?.ProgramDataPath;
                if (!string.IsNullOrWhiteSpace(programDataPath))
                {
                    return Path.Combine(programDataPath, "data", Plugin.PluginName);
                }
            }
            catch
            {
            }

            return Path.Combine("/config", "data", Plugin.PluginName);
        }
    }
}
