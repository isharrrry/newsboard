# NewsBoard 开发约定（AGENTS.md）

本仓库是「安卓大字新闻看板」的实施方案落地：Blazor 类库 + MAUI(Android) 宿主 + WPF(WebView2) 宿主。

## 构建 / 测试命令

```powershell
# 共享类库（所有纯逻辑与 UI 都在这）
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo

# 单元测试
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo

# WPF 宿主（Windows 桌面，同时也是调试 Blazor 层最快的途径）
dotnet run --project src\NewsBoard.Wpf

# 解决方案整体（含 MAUI/Android；Android 部分必须先点源环境脚本，见下）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo

# 单独构建 Android 宿主（产出 APK 于 bin\Debug\net10.0-android\*-Signed.apk）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo
```

当前机器：`.NET SDK 10.0.203`、runtime `10.0.7`；`maui-android` workload **已安装**（10.0.20/10.0.100，约 2.57 GB）。
Android 工具链在 **`D:\Android`**：JDK 17 于 `D:\Android\jdk-17`、Android SDK 于 `D:\Android\Sdk`（platform-36 / build-tools 36.0.0 / platform-tools）、NDK 27.2.12479018。
**构建 Android 前必须先点源 `scripts\android-env.ps1`**（本机执行策略是 `Restricted`，故需先 `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force`）；否则报 `XA5300 找不到 Android SDK`。环境现状与占用清单见 `docs\android-env-report.md`。
无 Android 设备/模拟器（`adb devices` 为空）→ 装机运行类验证做不了。

## 目录与文件所有权

| 区域 | 文件 | 负责 |
|---|---|---|
| 冻结契约（**任何人不许改**） | `Models/NewsItem.cs`、`Services/AppConfig.cs`、`Services/Abstractions/*.cs`、`_Imports.razor`、`Components/Routes.razor` | 主 Agent |
| 采集 + AI 提炼 | `Services/RssNewsSource.cs`、`Services/WebSearchNewsSource.cs`、`Services/NewsSummarizer.cs`、`Services/NewsRepository.cs`、`Services/ImageResolver.cs`、`Services/NewsFetchCoordinator.cs` | SA1 |
| 配置 + 持久化 | `Services/ConfigStore.cs`、`Components/Pages/Settings.razor`、`wwwroot/js/measure.js` 之外的设置页辅助 | SA2 |
| 播放引擎 + 播放 UI | `Services/PlaybackEngine.cs`、`Components/Pages/Display.razor`、`Components/Shared/NewsCard.razor`、`wwwroot/css/app.css`、`wwwroot/js/measure.js` | SA3 |
| WPF 宿主 | `src/NewsBoard.Wpf/**` | SA4 |
| Android 宿主 + 环境 | `src/NewsBoard.Maui/**` | SA5 |
| 测试 | `src/NewsBoard.Tests/**`（按主题分文件，勿互相覆盖） | 各 Agent |

## 冻结契约（不得修改签名/字段名）

- `NewsBoard.Shared.Models.NewsItem` / `RawNewsItem`
- `NewsBoard.Shared.Services.AppConfig`（§4 全部配置项）与全部枚举
- `NewsBoard.Shared.Services.Abstractions`：`IScreenController`、`ISpeechService`、`ISecretStore`、`IAutoStart`、`INewsSource`、`IConfigProvider`、`INewsRepository`、`IClock`（含 `SystemClock`）
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` / `InitializeNewsBoardAsync()`：宿主统一装配入口（先注册平台实现，再调用它；启动期必须 await `InitializeNewsBoardAsync()`，否则播放列表为空）
- `_Imports.razor` 的 using 集合、`Components/Routes.razor` 作为 `BlazorWebView.RootComponent`

需要新增接口/字段时：在自己的文件里加 `partial` 或在实现内私有化，**不要**动上述文件。

## 编码约定

- 目标框架：Shared/测试 = `net10.0`；**WPF = `net10.0-windows10.0.19041.0`（必须带版本化平台号）**；MAUI = `net10.0-android`。
  WPF 用裸 `net10.0-windows` 会启动即崩：`FileNotFoundException: Microsoft.Windows.SDK.NET` —— `BlazorWebView` 内部用 `WebView2CompositionControl`（D3D 合成），需要 CsWinRT 投影程序集，只有版本化 TFM 提供（SA4 实测）。
- **不引入第三方 NuGet 依赖**（HTTP 用 `HttpClient`，JSON 用 `System.Text.Json`，RSS 用 `XDocument`，测试用 xunit）。例外只有两个：WPF 宿主的 `Microsoft.AspNetCore.Components.WebView.Wpf`（10.0.0）与 `System.Speech`（10.0.7，方案 §3.1 指定的 TTS 实现）。`System.Security.Cryptography.ProtectedData`（DPAPI）**不需要加包**，`UseWPF=true` 时由 WindowsDesktop 框架提供，加包反而报 NU1510。
- `Nullable` 与 `ImplicitUsings` 已开启；代码注释用简体中文，仅在必要处写。
- Shared 项目**零平台依赖**，禁止 `#if ANDROID` / `#if WINDOWS`；平台能力只能通过 `Abstractions` 接口注入。
- 所有面向用户的新增文本用简体中文。
- 提交前必须 `dotnet build` 通过；有逻辑的分支要配 xunit 测试。

## 运行时事实（已实测）

- DeepSeek OpenAI 兼容端点：`POST {BaseUrl}/chat/completions`，`Authorization: Bearer <key>`。
- DeepSeek Anthropic 兼容 + 联网搜索：`POST {BaseUrlSearch}/v1/messages`，头 `x-api-key` + `anthropic-version: 2023-06-01`，`tools:[{"type":"web_search_20250305","name":"web_search","max_uses":N}]`；**已实测可用**，响应含 `server_tool_use` 与 `web_search_tool_result` 块，模型回显名为 `deepseek-v4-flash`。
- **RSS 源（实测，2026-10-08）**：Bing `?format=RSS` 已失效（返回 `text/html`），百度 `newsrss` 返回安全验证页，Google News RSS 在本机超时。**没有任何可用的「关键字搜索」RSS**；可用的是频道源：`https://www.chinanews.com.cn/rss/{scroll-news,china,world}.xml`、`http://www.people.com.cn/rss/{politics,world}.xml`、`https://www.ithome.com/rss/`。
  因此 `RssTemplate` 默认改为不含 `{q}` 的共享频道源，并新增 `RssFallbackTemplates` 备用链：模板**含 `{q}`** → 按关键字各抓一次；**不含** → 视为共享源只抓一次。
- 默认 `FetchMode = Auto`（先走已验证可用的联网搜索通道，RSS 作保底）。
- Windows 中文 TTS：`System.Speech` 枚举到 `Microsoft Huihui Desktop`(zh-CN)；`SpeakCompleted` 回调可靠（实测触发，产出 290KB WAV）。
