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

# 解决方案整体（不含 MAUI，MAUI 需 Android workload）
dotnet build NewsBoard.slnx -v q --nologo
```

当前机器：`.NET SDK 10.0.203`、runtime `10.0.7`、**未安装任何 workload、无 JDK/Android SDK**。

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
- `NewsBoard.Shared.Services.Abstractions`：`IScreenController`、`ISpeechService`、`ISecretStore`、`INewsSource`、`IClock`（含 `SystemClock`）
- `_Imports.razor` 的 using 集合、`Components/Routes.razor` 作为 `BlazorWebView.RootComponent`

需要新增接口/字段时：在自己的文件里加 `partial` 或在实现内私有化，**不要**动上述文件。

## 编码约定

- 目标框架：Shared/测试 = `net10.0`；WPF = `net10.0-windows`；MAUI = `net10.0-android`。
- **不引入第三方 NuGet 依赖**（HTTP 用 `HttpClient`，JSON 用 `System.Text.Json`，RSS 用 `XDocument`，测试用 xunit）。唯一例外：WPF 宿主的 `Microsoft.AspNetCore.Components.WebView.Wpf`。
- `Nullable` 与 `ImplicitUsings` 已开启；代码注释用简体中文，仅在必要处写。
- Shared 项目**零平台依赖**，禁止 `#if ANDROID` / `#if WINDOWS`；平台能力只能通过 `Abstractions` 接口注入。
- 所有面向用户的新增文本用简体中文。
- 提交前必须 `dotnet build` 通过；有逻辑的分支要配 xunit 测试。

## 运行时事实（已实测）

- DeepSeek OpenAI 兼容端点：`POST {BaseUrl}/chat/completions`，`Authorization: Bearer <key>`。
- DeepSeek Anthropic 兼容 + 联网搜索：`POST {BaseUrlSearch}/v1/messages`，头 `x-api-key` + `anthropic-version: 2023-06-01`，`tools:[{"type":"web_search_20250305","name":"web_search","max_uses":N}]`；**已实测可用**，响应含 `server_tool_use` 与 `web_search_tool_result` 块，模型回显名为 `deepseek-v4-flash`。
- Bing News RSS：`https://www.bing.com/news/search?q={q}&format=RSS`。
