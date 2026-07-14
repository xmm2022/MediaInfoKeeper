using System;
using System.Collections.Generic;
using System.ComponentModel;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Editors;
using MediaBrowser.Model.Attributes;
using MediaBrowser.Model.GenericEdit;

namespace MediaInfoKeeper.Options
{
    public class EnhanceOptions : EditableOptionsBase
    {
        public override string EditorTitle => "增强功能";

        public override string EditorDescription => string.Empty;
        
        [DisplayName("重建数据库索引")]
        public ButtonItem OptimizeDatabaseButton { get; set; } = new ButtonItem("重建数据库索引")
        {
            Caption = "重建数据库索引",
            CommandId = "enhance.optimizeDatabase",
            Icon = IconNames.settings_backup_restore,
            ConfirmationPrompt = "将会重建分词索引、排序名，并清理裂图记录"
        };
        
        [DisplayName("启用增强搜索")]
        [Description("支持中文模糊搜索与拼音搜索，默认关闭。\n\n修改后请先保存配置，再重启 Emby 使设置生效。\n\n卸载插件前，请先关闭本功能并保存配置重启Emby，再移除插件，避免出现 no such tokenizer: simple。")]
        public bool EnhanceChineseSearch { get; set; } = false;

        [VisibleCondition(nameof(ShowChineseSearchTokenizerStatus), SimpleCondition.IsTrue)]
        public StatusItem ChineseSearchTokenizerStatus { get; set; } = new StatusItem();
        
        [Browsable(false)]
        public bool EnhanceChineseSearchRestore { get; set; } = false;

        [Browsable(false)]
        public bool ShowChineseSearchTokenizerStatus { get; set; } = true;

        [DisplayName("排除原始标题")]
        [Description("从搜索中排除 OriginalTitle 字段")]
        public bool ExcludeOriginalTitleFromSearch { get; set; } = false;
        
        public enum SearchItemType
        {
            Movie,
            Collection,
            Series,
            Season,
            Episode,
            Person,
            LiveTv,
            Playlist,
            MusicAlbum,
            MusicTrack,
            MusicArtist,
            MusicGenre
        }

        [Browsable(false)]
        public List<EditorSelectOption> SearchItemTypeList { get; set; } = new List<EditorSelectOption>();

        [DisplayName("搜索范围")]
        [Description("选择要参与搜索的类型，留空表示全部。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(SearchItemTypeList))]
        public string SearchScope { get; set; } =
            string.Join(",", new[]
            {
                SearchItemType.Movie,
                SearchItemType.Collection,
                SearchItemType.Series,
                SearchItemType.Person,
                SearchItemType.MusicAlbum,
                SearchItemType.MusicTrack
            });
        
        [DisplayName("视频 Strm 直连")]
        [Description("对无需转码且可直放的远端 http .strm 视频生效，让客户端直接拉取直链而不是经 Emby 中转。")]
        public bool EnableStrmVideoDirectRedirect { get; set; } = false;

        [DisplayName("视频跟踪 302 跳转")]
        [Description("开启后视频直连会跟踪到 302 最终地址；关闭后直接返回 .strm 中的原始 URL。")]
        [VisibleCondition(nameof(EnableStrmVideoDirectRedirect), SimpleCondition.IsTrue)]
        public bool StrmVideoDirectRedirectFollow302 { get; set; } = true;

        [DisplayName("直连 URL 白名单")]
        [Description("视频和音乐直连共用，按 .strm 原始 URL 前缀匹配，留空表示不限制。支持逗号、分号或换行分隔。")]
        public string StrmDirectRedirectUrlAllowlist { get; set; } = string.Empty;

        [DisplayName("直连 URL 黑名单")]
        [Description("视频和音乐直连共用，按 .strm 原始 URL 前缀匹配，命中后不启用 .strm 302 直连。黑名单优先于白名单。支持逗号、分号或换行分隔。")]
        public string StrmDirectRedirectUrlBlocklist { get; set; } = string.Empty;

        [DisplayName("视频直连客户端黑名单")]
        [Description("按客户端名称关键字匹配，命中的客户端不启用视频 .strm 302 直连。支持逗号、分号或换行分隔。")]
        [VisibleCondition(nameof(EnableStrmVideoDirectRedirect), SimpleCondition.IsTrue)]
        public string StrmVideoDirectRedirectClientBlacklist { get; set; } = string.Empty;

        [DisplayName("音乐 Strm 直连")]
        [Description("对无需转码且可直放的远端 http .strm 音乐生效，让客户端直接拉取直链而不是经 Emby 中转。")]
        public bool EnableStrmAudioDirectRedirect { get; set; } = false;

        [DisplayName("音乐跟踪 302 跳转")]
        [Description("开启后音乐直连会跟踪到 302 最终地址；关闭后直接返回 .strm 中的原始 URL。")]
        [VisibleCondition(nameof(EnableStrmAudioDirectRedirect), SimpleCondition.IsTrue)]
        public bool StrmAudioDirectRedirectFollow302 { get; set; } = true;

        [DisplayName("音乐直连客户端黑名单")]
        [Description("按客户端名称关键字匹配，命中的客户端不启用音乐 .strm 302 直连。支持逗号、分号或换行分隔。")]
        [VisibleCondition(nameof(EnableStrmAudioDirectRedirect), SimpleCondition.IsTrue)]
        public string StrmAudioDirectRedirectClientBlacklist { get; set; } = "Emby Web";

        [DisplayName("原生生成 op 签名地址")]
        [Description("仅对精确匹配 legacy src 的 Google/OpenList STRM 直链生成短期 op 地址。默认关闭；失败时保留原 URL，交给现有 Caddy 签名桥回退。")]
        public bool EnableStrmDirectRedirectOpSigning { get; set; } = false;

        [DisplayName("op Public Base")]
        [Description("必须为无路径和查询串的 HTTPS origin。")]
        [VisibleCondition(nameof(EnableStrmDirectRedirectOpSigning), SimpleCondition.IsTrue)]
        public string StrmDirectRedirectOpPublicBase { get; set; } = "https://op.inemby.us.ci";

        [DisplayName("legacy src Host")]
        [Description("仅匹配该 host:port 的 HTTP STRM URL；tes、已有 op 和本地媒体不会签名。")]
        [VisibleCondition(nameof(EnableStrmDirectRedirectOpSigning), SimpleCondition.IsTrue)]
        public string StrmDirectRedirectOpLegacyHost { get; set; } = "src.inemby.us.ci:18080";

        [DisplayName("op 签名 TTL（秒）")]
        [Description("必须在 1 到 21600 秒之间；生产验证器当前上限为 6 小时。")]
        [VisibleCondition(nameof(EnableStrmDirectRedirectOpSigning), SimpleCondition.IsTrue)]
        public int StrmDirectRedirectOpTtlSeconds { get; set; } = 21600;

        [DisplayName("op 签名 Key 文件")]
        [Description("容器内只读 key 文件路径。不要把 key 内容写入插件配置。")]
        [VisibleCondition(nameof(EnableStrmDirectRedirectOpSigning), SimpleCondition.IsTrue)]
        public string StrmDirectRedirectOpKeyFile { get; set; } = "/run/secrets/emby-op-signing-key";

        [DisplayName("ESA PlaybackInfo 直链（实验）")]
        [Description("仅对带受保护 ESA 入口标记、命中客户端白名单且可直放的远端 STRM 生效。把 PlaybackInfo 的 DirectStreamUrl 直接改为 ESA 签名地址，避免播放器每段 Range 再经过 Emby 302。默认关闭。")]
        public bool EnableEsaPlaybackDirectUrl { get; set; } = false;

        [DisplayName("ESA Stream Base")]
        [Description("独立 canary ESA 入口的 HTTPS /stream 地址，例如 https://esa-canary.example.com/stream。")]
        [VisibleCondition(nameof(EnableEsaPlaybackDirectUrl), SimpleCondition.IsTrue)]
        public string EsaPlaybackDirectUrlBase { get; set; } = string.Empty;

        [DisplayName("ESA 直链客户端白名单")]
        [Description("按 Emby 客户端名称精确匹配；支持逗号、分号或换行分隔。实验阶段建议仅填写 Hills。")]
        [VisibleCondition(nameof(EnableEsaPlaybackDirectUrl), SimpleCondition.IsTrue)]
        public string EsaPlaybackDirectUrlClientAllowlist { get; set; } = "Hills";

        [DisplayName("OP PlaybackInfo 直链（实验）")]
        [Description("仅对带受保护 OP Direct 入口标记、命中客户端白名单且可直放的远端 STRM 生效。直接返回原生签名 OP 地址，不经过 ESA 媒体代理或 Emby 302。默认关闭。")]
        public bool EnableOpPlaybackDirectUrl { get; set; } = false;

        [DisplayName("OP 直链客户端白名单")]
        [Description("按 Emby 客户端名称精确匹配；支持逗号、分号或换行分隔。实验阶段建议仅填写 Hills。")]
        [VisibleCondition(nameof(EnableOpPlaybackDirectUrl), SimpleCondition.IsTrue)]
        public string OpPlaybackDirectUrlClientAllowlist { get; set; } = "Hills";

        [DisplayName("启用深度删除")]
        [Description("删除媒体时，尝试级联删除 STRM 或软链接目标文件及相关文件和空目录。")]
        public bool EnableDeepDelete { get; set; } = false;
        
        [DisplayName("通知系统增强")]
        [Description("提供媒体深度删除通知，喜爱更新通知和片头片尾打标更新通知。")]
        public bool EnableNotificationEnhance { get; set; } = true;
        
        [DisplayName("接管系统新入库通知")]
        [Description("关闭时不接管 Emby 原生新入库通知，收藏剧集更新使用插件的 favorites.update 通知；开启时屏蔽 Emby 原生 library.new 通知，并让收藏剧集更新改用 library.new 通知。")]
        public bool TakeOverSystemLibraryNew { get; set; } = false;

        [DisplayName("接管刷新队列")]
        [Description("接管 Emby 原生元数据刷新队列入口，按刷新意图分流到插件的元数据/媒体信息 runner。")]
        public bool TakeOverRefreshQueue { get; set; } = true;
        
        [DisplayName("优化封面显示")]
        [Description("优化显示集封面 16:9 铺满，不会有上下黑边。")]
        public bool EnableEpisodeImageAspectRatioOptimize { get; set; } = true;
        
        [DisplayName("缺失封面使用背景图")]
        [Description("当 episode 没有自己的封面图时，优先提供 series 的背景图。")]
        public bool EnableEpisodeBackdropFallback { get; set; } = true;

        [DisplayName("音乐封面回退增强")]
        [Description("当歌曲或音乐专辑缺少直接 Primary 封面时，优先使用专辑/子歌曲可用封面。")]
        public bool EnableAudioAlbumPrimaryFallback { get; set; } = true;
        
        [DisplayName("启用 NFO 增强")]
        [Description("增强 NFO 人物节点解析，导入使用 actor/director 等人物中的 thumb 图片地址。")]
        public bool EnableNfoMetadataEnhance { get; set; } = true;

        [DisplayName("按偏好隐藏演职人员")]
        [Description("按偏好隐藏电影剧集页面的演职人员，非删除，仍可搜索。")]
        public bool HidePersonNoImage { get; set; } = false;
        
        [DisplayName("拼音首字母排序")]
        [Description("中文标题按拼音首字母排序。已有条目可通过“重建数据库索引”更新。")]
        public bool EnablePinyinSortName { get; set; } = false;

        public enum HidePersonOption
        {
            NoImage,
            ActorOnly
        }

        [Browsable(false)]
        public List<EditorSelectOption> HidePersonOptionList { get; set; } = new List<EditorSelectOption>();

        [DisplayName("隐藏偏好")]
        [Description("可组合选择：无图、仅演员。勾选“仅演员”后，只保留演员和客串演员。")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(HidePersonOptionList))]
        [VisibleCondition(nameof(HidePersonNoImage), SimpleCondition.IsTrue)]
        public string HidePersonPreference { get; set; } = string.Empty;
        
        [DisplayName("电视剧显示总集数")]
        [Description("将电视剧和季度原本返回的未观看集数改为返回总集数，剧集、季的已播放显示状态会受影响。默认关闭。")]
        public bool EnableSeriesTotalEpisodeCount { get; set; } = false;

        [DisplayName("播放源名称美化")]
        [Description("在播放信息接口中把多版本媒体源名称显示为分辨率、杜比视界 Profile 和总码率，例如 4K DV P7 - 82 Mbps。")]
        public bool EnablePlaybackMediaSourceName { get; set; } = false;
        
        [DisplayName("禁止自动合集")]
        [Description("阻止 Emby 自动创建 BoxSets 合集库，并在用户视图中过滤该入口。")]
        public bool NoBoxsetsAutoCreation { get; set; } = false;
        
        [DisplayName("统一媒体库顺序")]
        [Description("让所有用户的媒体库顺序跟随首个管理员的 OrderedViews 配置。")]
        public bool EnforceLibraryOrder { get; set; } = false;

        [DisplayName("优化新建媒体库默认设置")]
        [Description("调整新建媒体库默认设置：TMDB 优先且独占启用，图片保存到媒体文件夹，字幕下载器默认关闭，章节自动生成默认关闭，语言地区默认中国中文。")]
        public bool EnableLibrayProviderSettings { get; set; } = true;

        [DisplayName("自动合并多版本")]
        [Description("开启后自动合并相同电影/电视剧的多个版本，支持跨库操作。\n\n保存后会自动为所有电视剧库开启 Emby 的自动剧集分组。")]
        public bool MergeMultiVersion { get; set; } = false;

        public enum MergeMoviesScopeOption
        {
            [Description("同文件夹")]
            FolderScope,
            [Description("同媒体库内")]
            LibraryScope,
            [Description("所有电影库")]
            GlobalScope
        }

        [DisplayName("电影合并范围")]
        [Description("同文件夹：仅合并同文件夹下的多版本；同媒体库内：合并当前库内相同电影；所有电影库：在所有电影/混合库中查找并合并。")]
        [VisibleCondition(nameof(MergeMultiVersion), SimpleCondition.IsTrue)]
        public MergeMoviesScopeOption MergeMoviesPreference { get; set; } = MergeMoviesScopeOption.FolderScope;

        public enum MergeSeriesScopeOption
        {
            [Description("同媒体库内")]
            LibraryScope,
            [Description("所有电视剧库")]
            GlobalScope
        }

        [DisplayName("电视剧合并范围")]
        [Description("同媒体库内：仅合并当前库内的相同剧集；所有电视剧库：在所有电视剧/混合库中查找并合并。\n\n选择所有电视剧库时，保存后插件会自动开启所有电视剧/混合库的 Emby 自动剧集分组选项。")]
        [VisibleCondition(nameof(MergeMultiVersion), SimpleCondition.IsTrue)]
        public MergeSeriesScopeOption MergeSeriesPreference { get; set; } = MergeSeriesScopeOption.LibraryScope;

        [DisplayName("日志来源黑名单")]
        [Description("按 logger.Name 匹配需要屏蔽的系统日志来源，支持逗号、分号或换行分隔。支持精确匹配；对于带动态后缀的来源可填写前缀，如 SessionsService-。")]
        public string SystemLogNameBlacklist { get; set; } = "HttpClient;TheMovieDb;SessionsService-;PlaystateService-;MediaInfoService-";

        [DisplayName("日志显示详细网络请求")]
        [Description("控制是否输出详细网络请求日志，例如 HTTP 方法和最终请求地址。默认开启。")]
        public bool EnableDetailedNetworkRequestLogging { get; set; } = true;

        [DisplayName("系统日志倒序显示")]
        [Description("将 /System/Logs 下日志接口的返回内容改为最新日志在前，不影响磁盘上的原始日志文件。")]
        public bool EnableSystemLogReverse { get; set; } = false;

        public void Initialize()
        {
            SearchItemTypeList.Clear();
            foreach (SearchItemType item in Enum.GetValues(typeof(SearchItemType)))
            {
                SearchItemTypeList.Add(new EditorSelectOption
                {
                    Value = item.ToString(),
                    Name = GetSearchItemTypeDisplayName(item),
                    IsEnabled = true
                });
            }

            HidePersonOptionList.Clear();
            foreach (HidePersonOption item in Enum.GetValues(typeof(HidePersonOption)))
            {
                HidePersonOptionList.Add(new EditorSelectOption
                {
                    Value = item.ToString(),
                    Name = GetHidePersonOptionDisplayName(item),
                    IsEnabled = true
                });
            }

        }

        public override IEditObjectContainer CreateEditContainer()
        {
            var container = (EditObjectContainer)base.CreateEditContainer();
            var root = container.EditorRoot;
            if (root?.EditorItems == null || root.EditorItems.Length == 0)
            {
                return container;
            }

            var itemLookup = new Dictionary<string, EditorBase>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in root.EditorItems)
            {
                var key = item.Name ?? item.Id;

                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (!itemLookup.ContainsKey(key))
                {
                    itemLookup.Add(key, item);
                }
            }

            var groupedItems = new List<EditorBase>();
            var groupIndex = 0;

            void AddGroup(string title, string description, params string[] propertyNames)
            {
                var items = new List<EditorBase>();
                foreach (var propertyName in propertyNames)
                {
                    if (itemLookup.TryGetValue(propertyName, out var item))
                    {
                        items.Add(item);
                        itemLookup.Remove(propertyName);
                    }
                }

                if (items.Count == 0)
                {
                    return;
                }

                groupIndex++;
                var group = new EditorGroup(title, items.ToArray(), $"group{groupIndex}", root.Id, null)
                {
                    Description = description
                };
                groupedItems.Add(group);
            }

            AddGroup("增强搜索", "", 
                nameof(OptimizeDatabaseButton),
                nameof(EnhanceChineseSearch),
                nameof(ChineseSearchTokenizerStatus),
                nameof(ExcludeOriginalTitleFromSearch),
                nameof(SearchScope));

            AddGroup("Emby 直连", "",
                nameof(EnableStrmVideoDirectRedirect),
                nameof(StrmVideoDirectRedirectFollow302),
                nameof(StrmDirectRedirectUrlAllowlist),
                nameof(StrmDirectRedirectUrlBlocklist),
                nameof(StrmVideoDirectRedirectClientBlacklist),
                nameof(EnableStrmAudioDirectRedirect),
                nameof(StrmAudioDirectRedirectFollow302),
                nameof(StrmAudioDirectRedirectClientBlacklist),
                nameof(EnableStrmDirectRedirectOpSigning),
                nameof(StrmDirectRedirectOpPublicBase),
                nameof(StrmDirectRedirectOpLegacyHost),
                nameof(StrmDirectRedirectOpTtlSeconds),
                nameof(StrmDirectRedirectOpKeyFile));

            AddGroup("深度删除", "",
                nameof(EnableDeepDelete));
            
            AddGroup("通知", "", 
                nameof(EnableNotificationEnhance),
                nameof(TakeOverSystemLibraryNew));

            AddGroup("刷新调度", "",
                nameof(TakeOverRefreshQueue));
            
            AddGroup("多版本合并", "",
                nameof(MergeMultiVersion),
                nameof(MergeMoviesPreference),
                nameof(MergeSeriesPreference));
            
            AddGroup("UI功能", "",
                nameof(EnableEpisodeImageAspectRatioOptimize),
                nameof(EnableEpisodeBackdropFallback),
                nameof(EnableAudioAlbumPrimaryFallback),
                nameof(HidePersonNoImage),
                nameof(HidePersonPreference),
                nameof(EnablePinyinSortName),
                nameof(EnableNfoMetadataEnhance),
                nameof(EnableSeriesTotalEpisodeCount),
                nameof(EnablePlaybackMediaSourceName),
                nameof(NoBoxsetsAutoCreation),
                nameof(EnforceLibraryOrder),
                nameof(EnableLibrayProviderSettings));

            AddGroup("日志", "",
                nameof(EnableDetailedNetworkRequestLogging),
                nameof(EnableSystemLogReverse),
                nameof(SystemLogNameBlacklist));
            
            var remaining = new List<EditorBase>();
            foreach (var item in root.EditorItems)
            {
                var key = item.Name ?? item.Id;
                if (!string.IsNullOrEmpty(key) && itemLookup.ContainsKey(key))
                {
                    remaining.Add(item);
                    itemLookup.Remove(key);
                }
            }

            if (remaining.Count > 0)
            {
                groupIndex++;
                groupedItems.Add(new EditorGroup("未分组", remaining.ToArray(), $"group{groupIndex}", root.Id, null));
            }

            if (groupedItems.Count > 0)
            {
                root.EditorItems = groupedItems.ToArray();
            }

            return container;
        }

        private static string GetSearchItemTypeDisplayName(SearchItemType item)
        {
            switch (item)
            {
                case SearchItemType.Movie:
                    return "电影";
                case SearchItemType.Collection:
                    return "合集";
                case SearchItemType.Series:
                    return "剧集";
                case SearchItemType.Season:
                    return "剧集-季";
                case SearchItemType.Episode:
                    return "剧集-集";
                case SearchItemType.Person:
                    return "人物";
                case SearchItemType.LiveTv:
                    return "直播电视";
                case SearchItemType.Playlist:
                    return "播放列表";
                case SearchItemType.MusicAlbum:
                    return "音乐-专辑";
                case SearchItemType.MusicTrack:
                    return "音乐-单曲";
                case SearchItemType.MusicArtist:
                    return "音乐-艺人";
                case SearchItemType.MusicGenre:
                    return "音乐-流派";
                default:
                    return item.ToString();
            }
        }

        private static string GetHidePersonOptionDisplayName(HidePersonOption item)
        {
            switch (item)
            {
                case HidePersonOption.NoImage:
                    return "隐藏无图演职人员";
                case HidePersonOption.ActorOnly:
                    return "隐藏导演编剧，仅显示演员";
                default:
                    return item.ToString();
            }
        }
    }
}
