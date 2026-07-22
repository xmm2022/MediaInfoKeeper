
<p align="center">
  <img src="Resources/ThumbImage.png" alt="MediaInfoKeeper" width="320" />
</p>

<p align="center">
  <a href="https://github.com/honue/MediaInfoKeeper/releases">
    <img src="https://img.shields.io/github/v/release/honue/MediaInfoKeeper?label=MediaInfoKeeper" />
  </a>
  <a href="https://github.com/honue/MediaInfoKeeper/stargazers">
    <img src="https://img.shields.io/github/stars/honue/MediaInfoKeeper" />
  </a>
  <a href="https://api.github.com/repos/honue/mediainfokeeper/releases/latest">
    <img src="https://img.shields.io/github/downloads/honue/MediaInfoKeeper/latest/total?label=LatestUsers" />
  </a>
  <a href="https://github.com/honue/MediaInfoKeeper/releases">
    <img src="https://img.shields.io/github/downloads/honue/MediaInfoKeeper/total?label=Downloads" />
  </a>
  <a href="https://github.com/MediaBrowser/Emby.Releases/releases">
    <img src="https://img.shields.io/github/v/release/MediaBrowser/Emby.Releases?label=Emby" />
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/github/license/honue/MediaInfoKeeper" />
  </a>
  <br>
  <a href="https://github.com/honue/MediaInfoKeeper/wiki">
    <img src="https://img.shields.io/badge/Wiki-功能说明-blue?logo=github&amp;logoColor=white" />
  </a>
  <a href="https://t.me/EmbyPatch_Channel">
    <img src="https://img.shields.io/badge/Telegram频道-EmbyPatch-26A5E4?logo=telegram&amp;logoColor=white" />
  </a>
</p>
<p align="center">
  <img src="Resources/ScreenShot.png" alt="MediaInfoKeeper" width="640" />
</p>

⏬ 安装
--------

1. 下载 dll 文件：[Releases](https://github.com/honue/MediaInfoKeeper/releases) 不带后缀的是通用版本，[最新通用版本](https://github.com/honue/MediaInfoKeeper/releases/latest/download/MediaInfoKeeper.dll)。
2. 放入 Emby 配置目录中的 `plugins` 目录。
3. 务必重命名为 `MediaInfoKeeper.dll`，否则后续自动更新会有两个 dll 存在。
4. 重启 Emby，在插件页面完成配置。

🧩 兼容性
-----------


- 版本说明：本仓库最新代码始终以支持 Emby 最新 release 为目标，开发过程中可能出现阶段性兼容问题。

- 当前插件 `latest` 版本适配Emby `4.9.5.0`，全平台支持。

- 更新限制：插件自动更新任务已按 [版本区间](Version.json) 限制，不会更新到当前 Emby 不支持的插件版本。

- 不支持：`4.8` 系列


🧊 Range Cache 联动（可选）
--------------------------

MediaInfoKeeper 可以与独立项目 [Emby Range Cache Proxy](https://github.com/xmm2022/emby-range-cache-proxy) 配合使用。两个项目职责分开，既可以联合部署，也可以各自独立运行：

- MediaInfoKeeper 运行在 Emby 进程内，负责识别入库、手动提取、计划任务和播放下一集等事件，并按插件开关提交预热意图。
- Emby Range Cache Proxy 是独立的 Go 服务，负责请求资格校验、源站访问、head/tail 缓存、可选 middle cache，以及 `normal`、`read_only`、`bypass` 运行模式。
- Caddy 或其他可信反向代理负责验证播放签名，并只对合格的播放请求设置缓存资格；普通 STRM、ffprobe、截图和探测请求不应获得缓存资格。
- 两者不要求在同一台服务器。跨主机部署时，建议让插件只访问 Emby 主机上的 loopback 控制桥，再由控制桥通过 HTTPS、来源 IP 白名单和独立控制密钥访问远端 Go 服务。

典型调用关系：

```text
Emby + MediaInfoKeeper
  ├─ 提交 itemId/mediaSourceId ──> /internal/prewarm
  └─ 保存 Range Cache 总开关 ──> 本机控制桥 ──> /internal/cache-mode

客户端播放 ──> 可信反向代理/签名校验 ──> Emby Range Cache Proxy ──> 源站
```

“媒体信息”页中的开关具有以下边界：

| 开关 | 作用 |
| --- | --- |
| `启用 Range Cache` | 总开关。关闭后远端进入 `bypass`，停止缓存读取、新建缓存和预热，但不删除已有缓存。 |
| `提取成功后触发 Range Cache 预热` | 只在入库、快捷菜单或计划任务提取/恢复成功后预热 head/tail。 |
| `播放时预热下一集 Range Cache` | 播放剧集约 5 秒后独立预热下一集，不依赖下一集是否需要提取 MediaInfo。 |

总开关优先级最高。“MediaInfo 预加载”和“浏览剧集提取媒体信息”只负责媒体信息，不会隐式预热 Range Cache；关闭“提取成功后触发预热”也不会关闭正常播放缓存或独立的下一集预热。

预热端点默认使用 `http://127.0.0.1:18180/internal/prewarm`，控制端点默认使用 `http://127.0.0.1:18180/internal/cache-mode`。密钥不要写入 README、日志或公开配置；远端控制密钥应与预热密钥分开。完整的缓存服务配置、反向代理边界和运行模式说明见 [Emby Range Cache Proxy README](https://github.com/xmm2022/emby-range-cache-proxy#readme)。


🔐 STRM 原生 op 签名
--------------------

插件可以在视频或音频 STRM 直链返回 `Location` 前，直接把受控的内部 `src` 地址转换为短期 `op` 签名地址。STRM 文件仍保存长期稳定的源地址，不会写入短期签名；不匹配的地址和签名失败会保留原 URL，方便继续由现有反向代理或签名桥处理。

当前实现使用 HMAC-SHA256 路径签名，并有以下边界：

- 默认关闭，只处理精确匹配的 `http://legacy-host:port`。
- 内部 URL 必须包含 32 位十六进制前缀，资源根仅允许 `/google/` 或 `/openlist/`。
- 不处理本地/PT、115、已有 `op`、查询串、点段、编码路径分隔符或反斜杠路径。
- TTL 必须为 `1`–`21600` 秒；每次签名使用独立随机 nonce。
- key 必须是正好 32 字节的原始文件；配置中只保存容器内文件路径，日志不会记录完整签名 URL。

Docker Compose 只读挂载示例：

```yaml
services:
  emby:
    volumes:
      - /host/path/op-signing-key:/run/secrets/emby-op-signing-key:ro
```

在“增强功能 → Emby 直连”中配置：

- `原生生成 op 签名地址`
- `op Public Base`，例如 `https://op.example.com`
- `legacy src Host`，例如 `src.example.com:18080`
- `op 签名 TTL（秒）`
- `op 签名 Key 文件`，默认 `/run/secrets/emby-op-signing-key`

建议先保留原有签名桥作为回滚路径，并分别验证 Google/OpenList Range 206、篡改拒绝、过期拒绝、本地媒体和其他独立链路。


⚖️ Lisence
-----------

本项目基于 [GNU General Public License v3.0](LICENSE) 开源。

- 二次开发、改名发布或重新打包 DLL 时，必须在用户可见位置标注原作者与原项目来源。
- 发布派生版本或二进制文件时，必须遵守 GPLv3，保留许可证，并公开对应版本源码或提供有效源码获取方式。

⭐ Stars
------------

<p align="center">
  <img src="https://api.star-history.com/svg?repos=honue/MediaInfoKeeper&type=Date" width="600"/>
</p>

🙏 致谢
----

本项目在部分功能设计、补丁思路与资源集成上参考或使用了以下开源项目，感谢各位对 Emby 社区的长期贡献：

- [StrmAssistant](https://github.com/sjtuross/StrmAssistant)：部分功能 Patch 思路以及 `simple` 分词器文件来源。

- [dd-danmaku](https://github.com/l429609201/dd-danmaku)：弹幕资源 `ede.js` 来源。
