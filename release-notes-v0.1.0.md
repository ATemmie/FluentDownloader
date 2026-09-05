# FluentDownloader v0.1.0

Fluent 风格的 Windows 多功能下载器 —— 首个公开版本。

## ✨ 功能

- **HTTP 多线程下载**：自研引擎，大文件自动 8 连接分段、断点续传、限速、失败重试、不支持 Range 时自动降级单流
- **内置浏览器 · 边看边下**：WebView2 内核，实时嗅探页面中的视频 / 音频 / m3u8 / 大文件，点击即下载；**点选模式**鼠标点哪下哪；**cookies 一键同步**（内置浏览器登录后解锁 B 站 1080P+ 等高清下载）
- **视频 / 音乐解析**：基于 yt-dlp，支持 YouTube、B站、抖音、快手、网易云音乐、QQ音乐(免费曲库)、SoundCloud 等上千站点，可选清晰度，音频提取 MP3
- **BitTorrent**：磁力链 / 种子、DHT、上传限速、完成后自动停止做种
- **统一任务队列**：所有任务统一调度、暂停/恢复/取消，重启后自动恢复未完成任务
- **精美界面**：WPF + Fluent Design + Mica 背板，浅色 / 深色 / 跟随系统主题，流畅动画

## 📦 下载

| 文件 | 适用场景 |
|---|---|
| `FluentDownloader-v0.1.0-win-x64-self-contained.zip` (**推荐**) | **无需安装 .NET**，解压即用（约 65MB） |
| `FluentDownloader-v0.1.0-win-x64.zip` | 体积小（4MB），需已安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

> 解压后运行 `FluentDownloader.exe`。需要 WebView2 运行时（Windows 10/11 一般自带）。
> 首次使用视频/音乐模块时会自动下载 yt-dlp 与 ffmpeg（约 1 分钟）。

## 🧩 模块化架构

功能 = 模块（`Modules/` 下的独立 dll）：删除某个模块 dll 即可移除对应功能，新增模块参见开发指南（仓库 `docs/` 目录）。

## ⚖️ 合规说明

仅支持公开可访问内容；VIP / DRM 加密内容不支持。请遵守各平台服务条款，内容仅供个人学习使用。

## 🙏 致谢

[yt-dlp](https://github.com/yt-dlp/yt-dlp) · [FFmpeg](https://ffmpeg.org) · [MonoTorrent](https://github.com/alanmcgovern/MonoTorrent) · [WPF UI](https://github.com/lepoco/wpfui) · [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) · [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)
