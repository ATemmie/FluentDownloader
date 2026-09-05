# FluentDownloader

Fluent 风格的 Windows 多功能下载器，采用**模块化架构**，内置 WebView2 浏览器（资源嗅探 + 点选下载）。

![技术栈](https://img.shields.io/badge/.NET-8.0-blue) ![UI](https://img.shields.io/badge/WPF-WPF_UI_Fluent-purple) ![平台](https://img.shields.io/badge/平台-Windows_10/11-lightgrey)

## 功能总览

| 模块 | 功能 | 引擎 |
|---|---|---|
| **HTTP 直链** | 多连接分段下载（默认 8 连接）、断点续传、限速、重试退避、单连接降级 | 自研（HttpClient + Range） |
| **网页嗅探** | 内置浏览器、CDP 资源嗅探（视频/音频/m3u8/附件）、元素点选下载（点哪下哪）、cookies 一键同步 | WebView2 + DevTools Protocol |
| **视频 / 音乐** | YouTube / B站 / 抖音 / 快手 / 网易云 / QQ音乐(免费曲库) / SoundCloud 等上千站点解析下载，清晰度选择，音频提取 MP3，B站高清需 cookies | yt-dlp + FFmpeg |
| **BitTorrent** | 磁力链 / 种子文件、DHT、做种、限速、完成后自动停止做种 | MonoTorrent |

- **统一任务队列**：所有引擎的任务统一调度（默认同时 3 个），统一进度/速度/暂停/恢复/取消
- **明暗主题**：浅色 / 深色 / 跟随系统，Mica 云母背板，切换过渡动画
- **任务持久化**：应用重启后未完成任务自动恢复，从断点继续
- **首次运行自举**：yt-dlp / ffmpeg 缺失时自动下载到 `%LocalAppData%\FluentDownloader\bin`

## 模块化架构

```
FluentDownloader/
├── FluentDownloader.Contracts/      # 契约层（纯接口）：IModule / IDownloadEngine / DownloadTask
├── FluentDownloader.App/            # UI 壳：导航、主题、任务页、设置页（不含引擎实现）
├── FluentDownloader.Core/           # 任务管理器、路由调度、持久化、限速器、工具
└── Modules/                         # 功能模块（每个一个程序集）
    ├── FluentDownloader.Module.Http      # 自研多线程 HTTP 引擎
    ├── FluentDownloader.Module.Browser   # WebView2 浏览器 + 嗅探 + 点选
    ├── FluentDownloader.Module.Media     # yt-dlp + ffmpeg
    └── FluentDownloader.Module.Torrent   # MonoTorrent
```

**接入机制**：启动时 `ModuleLoader` 扫描输出目录中的 `FluentDownloader.Module.*.dll`（独立
`AssemblyLoadContext` 加载，共享依赖回退宿主），反射实例化所有 `IModule` 并注册到 DI。
模块声明自己的导航页面与下载引擎；`TaskManager` 按 `Priority` 把下载请求路由给第一个
`CanHandle` 的引擎。

- **删功能**：删除输出目录里对应的 `FluentDownloader.Module.*.dll` 即可，其余零改动
- **加功能**：新建类库，实现 `IModule`（+ 可选 `IDownloadEngine`），加入 `Modules/` 即可

> 📖 **如何开发新模块？** 见详细文档：[docs/模块开发指南.md](docs/模块开发指南.md)
> —— 含架构原理、三种模块形态、FTP 模块完整实战、契约参考手册、10 条真实踩坑记录与发布检查清单。

## 构建与运行

```bash
# 需要 .NET 8 SDK（Windows 10/11）
dotnet build FluentDownloader.sln -c Debug
# 运行
FluentDownloader.App/bin/Debug/net8.0-windows/FluentDownloader.exe
```

构建时 `CopyModuleOutputs` 目标会自动把各模块输出复制进主程序目录。
WebView2 运行时 Win10/11 一般自带；缺失时从 [Microsoft 官网](https://developer.microsoft.com/microsoft-edge/webview2/) 安装 Evergreen Runtime。

## 使用说明

- **直链下载**：首页粘贴 URL（自动识别类型）→ 添加下载
- **边看边下**：「网页嗅探」→ 输入网址 → 右侧面板实时列出嗅探到的资源 → 点击下载
  - **点选模式**：开启后鼠标悬停高亮页面元素，点击即抓取其资源地址（Esc 退出）
  - **Cookies 同步**：在内置浏览器登录账号（如 B 站）→ 点「同步 Cookies」→ 后续媒体/HTTP 下载自动携带登录态（解锁 1080P+ 高清）
- **视频/音乐**：「视频 / 音乐」→ 粘贴视频页链接 → 解析 → 选清晰度 → 添加下载
- **BT**：「BitTorrent」→ 粘贴磁力链或选择 .torrent 文件

## 断点续传原理（HTTP 引擎）

1. `GET + Range: bytes=0-1` 探测：是否支持 Range（206?）、总大小、ETag
2. 按连接数均分字节区间，预分配文件后各段并发下载（校验 206，收到 200 降级单流）
3. 各段进度定期写入 `<文件>.fdl.json`（含 ETag）；暂停/重启后从段内偏移续传
4. 续传请求带 `If-Range`：资源变更时自动丢弃进度重下
5. 单段失败指数退避重试（最多 5 次），限速用共享令牌桶实现

## 合规说明

- 仅支持**公开可访问**内容；VIP / 付费 / DRM 加密内容不支持（界面已明示）
- 视频解析采用 yt-dlp 通用引擎，不逆向特定平台私有 API
- 请遵守各平台服务条款，下载内容仅供个人学习使用
- 第三方组件：yt-dlp (Unlicense)、FFmpeg (LGPL/GPL)、MonoTorrent (MIT)、WPF-UI (MIT)、CommunityToolkit.Mvvm (MIT)、WebView2 (官方运行时)

## 已知限制（MVP）

- 浏览器嗅探对 MSE/blob 加密流无法直接下载（提示改用嗅探面板中的 m3u8 清单）
- 播放列表链接暂不支持（提示粘贴单视频链接）
- 点选模式对 iframe 内元素无效
- B 站 4K/HDR 需大会员 cookies；DRM 内容一律不支持
