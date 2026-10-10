# 架构

[English](architecture.md) | **中文** | [Español](architecture.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

## 三个项目

| 项目 | 目标框架 | 是什么 |
|---|---|---|
| `NewsBoard.Shared` | `net10.0` | Razor 类库（RCL）：**全部** UI 组件、`wwwroot`（css/js）、配置模型、抓取、提炼、播放引擎与平台抽象接口。**零平台依赖。** |
| `NewsBoard.Maui` | `net10.0-android`；`net10.0-windows10.0.19041.0` | Android 宿主（MAUI Blazor Hybrid），也是产出 APK 的项目。保留 Windows 目标框架是为了能在 PC 上调试同一套 Blazor 层。提供 Android 与 Windows 两套平台实现。 |
| `NewsBoard.Wpf` | `net10.0-windows10.0.19041.0` | Windows 桌面宿主：WPF + `Microsoft.AspNetCore.Components.WebView.Wpf`（基于 WebView2），承载同一套 Razor 组件。提供 Windows 平台实现。 |

依赖方向单一：`Maui → Shared`、`Wpf → Shared`。**Shared 永远不反向依赖宿主。**

`NewsBoard.Wpf` 必须用**带版本号**的平台目标框架。裸的 `net10.0-windows` 会启动即崩：`FileNotFoundException: Microsoft.Windows.SDK.NET` —— `BlazorWebView` 内部用 `WebView2CompositionControl`（D3D 合成），需要 CsWinRT 投影程序集，而只有版本化 TFM 才提供。

为什么这样分：

- **平台能力全在宿主项目里。** 屏幕控制、TTS、密钥存储、开机自启、打开链接都通过 Shared 定义的接口注入；Shared 里既没有 `#if ANDROID` 也没有 `#if WINDOWS`。
- **Shared 只有纯 .NET 逻辑** —— 抓取、提炼、去重、缓存、播放状态机，以及全部 Razor 组件。一份代码供三个头共用，而且能在 PC 上用 WPF 宿主快速调试 Blazor 层。
- **Blazor 只管渲染。** 组件接收 `NewsItem` 加显示选项，输出 HTML/CSS。

## 目录树

下面每个文件都在仓库里真实存在（`bin/`、`obj/` 下的构建产物已省略）。

```
NewsBoard/
├── NewsBoard.slnx
├── README.md / README.zh-CN.md / README.es.md
├── CHANGELOG.md
├── LICENSE                          （MIT）
├── AGENTS.md                        （给 AI 协作者的约定）
├── plans/
│   └── news-board.plan.md           （实施方案与历史记录）
├── scripts/
│   └── android-env.ps1              （任何 Android 构建前必须先点源它）
├── docs/                            （本目录）
└── src/
    ├── NewsBoard.Shared/            （net10.0，Razor 类库，零平台依赖）
    │   ├── NewsBoard.Shared.csproj
    │   ├── _Imports.razor
    │   ├── Models/
    │   │   └── NewsItem.cs                      （NewsItem / RawNewsItem）
    │   ├── Components/
    │   │   ├── Routes.razor                     （BlazorWebView 的根组件）
    │   │   ├── Pages/Display.razor              （看板页 —— 唯一常驻页面）
    │   │   ├── Pages/Settings.razor             （设置页）
    │   │   └── Shared/NewsCard.razor            （单条渲染：纯文字，或文字 + 图片）
    │   ├── Services/
    │   │   ├── AppConfig.cs                     （全部配置字段 + Clone/NormalizeKeywords）
    │   │   ├── ArticleContentFetcher.cs         （正文：5 秒超时、去噪、截掉推荐位）
    │   │   ├── ConfigStore.cs                   （JSON 持久化、覆盖前留 .bak、Key 存平台安全存储）
    │   │   ├── ImageResolver.cs                 （四级取图、Range 校验、一张图只归一条）
    │   │   ├── ImportanceScoring.cs             （容错解析、整批退化时按秩均衡）
    │   │   ├── KeywordClassifier.cs             （按配置关键字归类，兜底频道名）
    │   │   ├── MonitorPowerPolicy.cs            （Windows 真关屏的护栏判定与幂等）
    │   │   ├── NewsBoardDataDirectory.cs        （数据目录的唯一解析点）
    │   │   ├── NewsFetchCoordinator.cs          （一轮抓取编排：去重 → 提炼 → 正文 → 配图）
    │   │   ├── NewsFetchScheduler.cs            （启动即抓，之后按间隔循环）
    │   │   ├── NewsRepository.cs                （缓存、URL 去重、门槛过滤、自然日剪枝）
    │   │   ├── NewsSummarizer.cs                （提炼 + 重要度 + 关键字、严格 JSON 解析、重试）
    │   │   ├── PlaybackEngine.cs                （播放状态机、WakeAsync、首帧闸门）
    │   │   ├── RetentionWindow.cs               （按自然日分桶的保留窗口，纯逻辑）
    │   │   ├── RssNewsSource.cs                 （RSS 频道源 + 备用链）
    │   │   ├── ServiceCollectionExtensions.cs   （AddNewsBoardCore / InitializeNewsBoardAsync）
    │   │   ├── SummaryText.cs                   （摘要本地二次校验：上限 1.2 倍，截到句号）
    │   │   ├── WebSearchNewsSource.cs           （Anthropic 兼容端点 + web_search 工具）
    │   │   └── Abstractions/
    │   │       ├── IAutoStart.cs
    │   │       ├── IConfigProvider.cs
    │   │       ├── ILinkOpener.cs
    │   │       ├── INewsRepository.cs
    │   │       ├── INewsSource.cs               （`IClock` 与 `SystemClock` 也定义在这里）
    │   │       ├── IScreenController.cs
    │   │       ├── ISecretStore.cs
    │   │       └── ISpeechService.cs
    │   └── wwwroot/
    │       ├── css/app.css                      （大字排版、滚动动画、过渡）
    │       ├── css/settings.css                 （设置页，分组卡片排版）
    │       └── js/measure.js                    （溢出测量 + 滚动进度回调）
    ├── NewsBoard.Maui/              （net10.0-android；net10.0-windows10.0.19041.0）
    │   ├── NewsBoard.Maui.csproj
    │   ├── MauiProgram.cs                       （DI：按目标框架注册平台实现，再调 AddNewsBoardCore）
    │   ├── NewsBoardBoot.cs                     （启动期加载配置与抓取）
    │   ├── App.xaml / App.xaml.cs
    │   ├── MainPage.xaml / MainPage.xaml.cs     （BlazorWebView 宿主）
    │   ├── wwwroot/index.html
    │   ├── Resources/
    │   │   ├── AppIcon/appicon.svg, appiconfg.svg
    │   │   └── Splash/splash.svg
    │   ├── Platforms/Android/
    │   │   ├── AndroidManifest.xml              （INTERNET、WAKE_LOCK、POST_NOTIFICATIONS、RECEIVE_BOOT_COMPLETED、前台服务）
    │   │   ├── MainActivity.cs / MainApplication.cs
    │   │   ├── AndroidScreenController.cs       （常亮 + 窗口亮度 0 + 恢复）
    │   │   ├── AndroidSpeechService.cs          （原生 Android.Speech.Tts + UtteranceProgressListener）
    │   │   ├── MauiSecretStore.cs               （SecureStorage）
    │   │   ├── AndroidAutoStart.cs、AutoStartHelper.cs、BootReceiver.cs、NewsBoardForegroundService.cs
    │   │   └── AndroidLinkOpener.cs
    │   └── Platforms/Windows/                   （Windows 头：同一套 Razor 组件跑桌面）
    │       ├── App.xaml / App.xaml.cs
    │       ├── app.manifest
    │       ├── WindowsScreenController.cs       （SC_MONITORPOWER + 前台/全屏护栏）
    │       ├── WindowsSpeechService.cs
    │       ├── WindowsSecretStore.cs
    │       ├── WindowsAutoStart.cs
    │       └── WindowsLinkOpener.cs
    ├── NewsBoard.Wpf/               （net10.0-windows10.0.19041.0）
    │   ├── NewsBoard.Wpf.csproj
    │   ├── App.xaml / App.xaml.cs
    │   ├── MainWindow.xaml / MainWindow.xaml.cs （BlazorWebView 承载 Routes；F11 / Esc 切换无边框全屏）
    │   ├── wwwroot/index.html                   （显式 Blazor.start() + 可见的启动提示）
    │   └── Services/
    │       ├── WpfScreenController.cs
    │       ├── WpfSpeechService.cs              （System.Speech.Synthesis.SpeechSynthesizer）
    │       ├── WpfSecretStore.cs                （DPAPI ProtectedData）
    │       ├── WpfAutoStart.cs                  （HKCU Run 键）
    │       └── WpfLinkOpener.cs
    └── NewsBoard.Tests/             （net10.0，xunit —— 24 个 .cs 文件：按区域划分的 Sa1–Sa10 套件，加上专题测试与共用的 doubles/collection 夹具）
        └── NewsBoard.Tests.csproj
```

## 一次「启动 → 抓取 → 播放」的数据流

1. **宿主启动。** WPF 宿主在 `App.xaml.cs` 里注册自己的平台实现；MAUI 宿主在 `MauiProgram.cs` 里按目标框架注册 Android 或 Windows 那一套。两者随后都调用 `AddNewsBoardCore()`。`MainWindow`（WPF）与 `MainPage`（MAUI）承载 `BlazorWebView`，其根组件是 `Components/Routes.razor`。
2. **装配。** `AddNewsBoardCore()` 注册一个 `Timeout.InfiniteTimeSpan` 的 `HttpClient` —— 三个联网实现各自计时（15 秒 / 90 秒 / 3 秒），`HttpClient` 的超时会把它们提前切断。同时注册 `IClock → SystemClock`、`NewsRepository`（暴露为 `INewsRepository`）、`ConfigStore`（暴露为 `IConfigProvider`）、`NewsSummarizer`、`ImageResolver`、`ArticleContentFetcher`、两个 `INewsSource` 实现、`NewsFetchCoordinator`、`NewsFetchScheduler` 与 `PlaybackEngine`。
3. **初始化。** `InitializeNewsBoardAsync()` 加载 `config.json`（其中的 API Key 从 `ISecretStore` 取回）、加载 `news.json`，然后启动 `NewsFetchScheduler` —— 立刻抓一次，之后按「抓取间隔」（默认 5 分钟）循环。**如果这次调用没有被 await，播放列表会一直为空，画面永远停在「正在获取新闻…」。**
4. **抓取。** 调度器 → `NewsFetchCoordinator` → `INewsSource`（RSS 频道源，或模型联网搜索通道；「自动」先走联网搜索、失败回退 RSS）→ URL 去重 → 正文补抓 → `NewsSummarizer`（摘要 + 重要度 + 关键字）→ `ImageResolver`（仅图文模式）→ `NewsRepository` 缓存。
5. **播放。** `Display.razor` 再次 await `InitializeNewsBoardAsync()`，开启首帧闸门，然后调用 `PlaybackEngine.StartAsync()`：设置常亮、应用全屏偏好、退出黑屏、探测语音引擎、重载播放列表，并进入 SHOW → BLACKOUT → SHOW 循环。
6. **渲染。** `NewsCard.razor` 渲染当前条目；`wwwroot/js/measure.js` 测量溢出并驱动滚动；`ISpeechService` 朗读摘要，其完成回调结束该条（决策 D5 —— 朗读即条目时钟）。`IScreenController` 负责常亮与黑屏。

## 平台能力如何注入

`NewsBoard.Shared.Services.Abstractions` 定义接口；每个宿主在调用 `AddNewsBoardCore()` **之前**把自己的实现注册进依赖注入。

| 接口 | WPF | MAUI（Android） | MAUI（Windows） |
|---|---|---|---|
| `IScreenController` | `WpfScreenController` | `AndroidScreenController` | `WindowsScreenController` |
| `ISpeechService` | `WpfSpeechService` | `AndroidSpeechService` | `WindowsSpeechService` |
| `ISecretStore` | `WpfSecretStore` | `MauiSecretStore` | `WindowsSecretStore` |
| `IAutoStart` | `WpfAutoStart` | `AndroidAutoStart` | `WindowsAutoStart` |
| `ILinkOpener` | `WpfLinkOpener` | `AndroidLinkOpener` | `WindowsLinkOpener` |
| `INewsSource`、`INewsRepository`、`IConfigProvider`、`IClock` | 由 Shared 的 `AddNewsBoardCore()` 统一注册 —— 没有平台实现 | | |

`NewsBoard.Maui` 在 `MauiProgram.cs` 里按目标框架选择 Android 或 Windows 那一套；这类条件编译只留在宿主项目里。Shared 自身必须保持不含 `#if ANDROID` 与 `#if WINDOWS`。

## 冻结契约

下面这些文件与签名不许改（在自己的文件里加 `partial` 成员，或把实现细节私有化，是可以的）：

- `NewsBoard.Shared.Models.NewsItem` 与 `RawNewsItem` —— 含 `NewsItem.Importance`（`double?`，0–1，`null` = 未评分）、`NewsItem.RawContent`（抓到的原始内容），以及 `RawNewsItem.Importance`、`RawNewsItem.Description`。
- `NewsBoard.Shared.Services.AppConfig` 及其全部枚举 —— 含 `SummaryLanguage`（`SummaryLanguageOption`：默认 SimplifiedChinese / TraditionalChinese / English，配 `SummaryLanguageText.PromptRule()` 给提示词硬约束、`DisplayName()` 给界面文案）、`ClockFontSizeSp`（默认 32）、`ClockFontFamily`（默认 `""`）、`ImportanceThreshold`（默认 0.7）、`RetentionDays`（默认 3）；`PlayOrder` 默认是 **Random**。
- `NewsBoard.Shared.Services.Abstractions`：`IScreenController`、`ISpeechService`、`ISecretStore`、`IAutoStart`、`ILinkOpener`、`INewsSource`、`IConfigProvider`、`INewsRepository`、`IClock`（含 `SystemClock`）。
- `PlaybackEngine` 的手动控制 API：`NextAsync()` / `PreviousAsync()` / `InterruptAsync()`（打断本轮 → 立即灭屏等待「黑屏时长」秒 → 下一轮；即使关掉亮熄循环也会灭一次），两个方向的切换都按列表循环回绕；以及 `WakeAsync()`（灭屏期间点一下屏幕 → 立刻回到显示并从当前条重新起算窗口；显示期间调用无副作用，既不切条也不重算窗口）。
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` 与 `InitializeNewsBoardAsync()` —— 宿主统一的装配入口。
- `_Imports.razor` 的 using 集合，以及 `Components/Routes.razor` 作为 `BlazorWebView.RootComponent`。
