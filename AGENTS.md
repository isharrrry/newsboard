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

# MAUI 的 Windows 头（桌面调试；非打包运行，依赖已安装的 Windows App Runtime）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0
```

当前机器：`.NET SDK 10.0.203`、runtime `10.0.7`；`maui-android`（10.0.20/10.0.100）与 `maui-windows` workload **均已安装**。
MAUI 的 Windows 头用 `WindowsPackageType=None`（非打包）运行，需要 Windows App Runtime（本机已装 1.6 / 1.7 / 1.8）。
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

- `NewsBoard.Shared.Models.NewsItem` / `RawNewsItem`（另有 `NewsItem.Importance`：`double?`，0–1，null = 未评分；`NewsItem.RawContent`：抓到的原始内容；`RawNewsItem.Importance` / `Description`）
- `NewsBoard.Shared.Services.AppConfig`（§4 全部配置项）与全部枚举，另含：`SummaryLanguage`（`SummaryLanguageOption`：SimplifiedChinese 默认 / TraditionalChinese / English；扩展 `SummaryLanguageText.PromptRule()` 给提示词硬约束、`DisplayName()` 给界面文案）、`ClockFontSizeSp`（默认 32）、`ClockFontFamily`（默认 ""）、`ImportanceThreshold`（默认 0.7）、`RetentionDays`（默认 3）；`PlayOrder` 默认 **Random**
- `NewsBoard.Shared.Services.Abstractions`：`IScreenController`、`ISpeechService`、`ISecretStore`、`IAutoStart`、`ILinkOpener`（用系统浏览器打开原文）、`INewsSource`、`IConfigProvider`、`INewsRepository`、`IClock`（含 `SystemClock`）
- `PlaybackEngine` 的手动控制 API：`NextAsync()` / `PreviousAsync()` / `InterruptAsync()`（打断本轮 → 立即灭屏等待「黑屏时长」秒 → 下一轮；即使 `BlackoutCycleEnabled=false` 也会灭一次），方向切换按列表循环回绕；`WakeAsync()`（灭屏期间点一下屏幕 → 立刻回到显示并从当前条重新起算窗口；显示期间调用无副作用，不切条也不重算窗口）
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` / `InitializeNewsBoardAsync()`：宿主统一装配入口（先注册平台实现，再调用它；启动期必须 await `InitializeNewsBoardAsync()`，否则播放列表为空）
- `_Imports.razor` 的 using 集合、`Components/Routes.razor` 作为 `BlazorWebView.RootComponent`

需要新增接口/字段时：在自己的文件里加 `partial` 或在实现内私有化，**不要**动上述文件。

## 编码约定

- 目标框架：Shared/测试 = `net10.0`；**WPF = `net10.0-windows10.0.19041.0`（必须带版本化平台号）**；MAUI = `net10.0-android` + `net10.0-windows10.0.19041.0`。
  WPF 用裸 `net10.0-windows` 会启动即崩：`FileNotFoundException: Microsoft.Windows.SDK.NET` —— `BlazorWebView` 内部用 `WebView2CompositionControl`（D3D 合成），需要 CsWinRT 投影程序集，只有版本化 TFM 提供（SA4 实测）。
- **不引入第三方 NuGet 依赖**（HTTP 用 `HttpClient`，JSON 用 `System.Text.Json`，RSS 用 `XDocument`，测试用 xunit）。例外只有这些：WPF 宿主的 `Microsoft.AspNetCore.Components.WebView.Wpf`（10.0.0）、`System.Speech`（10.0.7，方案 §3.1 指定的 TTS 实现）、以及 MAUI 的 Windows 头必需的 `System.Security.Cryptography.ProtectedData`（10.0.0，MAUI 不走 WindowsDesktop 框架，DPAPI 得自带）。WPF 侧 DPAPI **不需要加包**，`UseWPF=true` 时由 WindowsDesktop 框架提供，加包反而报 NU1510。
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
- **MAUI 的 `wwwroot/index.html` 必须显式 `Blazor.start()`**：`blazor.webview.js` 带 `autostart="false"` 时，Windows 头不会替我们启动，页面会一直停在「正在启动…」（实测；`Blazor.start()` 重复调用是幂等的）。
- **MAUI 宿主必须在启动期调用 `InitializeNewsBoardAsync()`**（现于 `MauiProgram.CreateMauiApp()` 末尾 fire-and-forget）；不调用则配置（含 API Key）不加载、抓取调度器不启动 —— 症状是画面永远停在「正在获取新闻…」且进程零网络连接（实测踩过）。`Display.razor` 起播前也会 `await Services.InitializeNewsBoardAsync()` 兜底（幂等）。
- 摘要默认 **70 字**、卡片默认**不显示标题**（`AppConfig.ShowTitle=false`）；两条通道（RSS 提炼 / 联网搜索）都要过 `SummaryText.Normalize` 本地二次校验（上限 1.2 倍 → 截到最后一个句号），因为模型实测会超字数。实测（2026-10-08，默认 70）：RSS 提炼 53–66 字、联网搜索 72–84 字。
- **摘要语言**：两条通道的提示词都会写入 `config.SummaryLanguage.PromptRule()`。**实测（2026-10-08）**：加这条之前，联网通道检索港台源时摘要会夹带繁体（9 篇里 6 篇命中 1–3 个繁体字）；加之后同样关键字复测 **9 篇全 0**（判定用「只含繁体专有字」的字集，简繁同形字如 路/新/果/大 必须排除，否则误报）。
- **重要度筛选（模型打分 + 本地均衡）**：两条通道的提示词都要求模型给出 `importance`（0.00–1.00，**相对最近三天这批新闻**、要拉开差距：重大 0.80–1.00 / 例行 0.20–0.50）；`ImportanceScoring.Read()` 容错解析（数字/数字字符串、越界夹取、非法→null）；`ImportanceScoring.Balance()` 在**整批退化**（极差 < 0.1 或 ≥80% 堆在 ≥0.85/≤0.15）且 ≥3 条时按秩在 [0.15, 0.95] 展开；`NewsRepository.GetPlaylist` **先按 `ImportanceThreshold` 过滤再排序**（`Importance` 为 null 的旧缓存保留，等于门槛也保留），随机顺序缓存键包含门槛（改门槛立即重排）。
  实测（2026-10-08，1 关键字 RSS + 2 关键字联网）：缓存 16 条分布 `0.2-0.4:4 / 0.4-0.6:5 / 0.6-0.8:4 / 0.8-1.0:3`，过 0.7 门槛显示 6 条 —— 既不堆高分也不堆低分。
  **注意**：门槛过滤后播放列表可能为空（「正在获取新闻…」会一直显示），这是配置语义而非卡死。
- **`RawContent`**：RSS 通道会把 `RawNewsItem.Description` 截断到 2000 字存进缓存（便于回看/二次提炼）；**联网通道拿不到正文**，该字段恒为 null。
- **图文模式的配图来源（实测，2026-10-08）**：中新网、人民网文章页**都没有 `og:image`/`twitter:image`**，RSS 也没有 `enclosure`/`media:*` ⇒ 只看 meta 会永远取不到图。`ImageResolver` 现为四级：og:image → twitter:image → `link[rel=image_src]` → **正文首图**（优先正文容器内、`<p>` 之后、跳过 logo/图标/占位图/早于文章日期 60 天以上的模板素材），且**逐个候选发 Range 请求校验真的返回 `image/*`**（中新网图床 `i2.chinanews.com.cn` 防盗链，直接取回的是 html）。实测 8 篇真实文章：修前 8/8 命中但 4 篇拿到同一张模板图、1 篇页脚图；修后 7/8 命中且**各不相同**的真实配图。
  图文模式下每轮还会为「缓存里已存在但没图」的旧条目补抓配图（每轮上限 8 条，`NewsFetchCoordinator.MaxImageBackfill`）。
- **灭屏期间点一下屏幕即唤醒**：`.blackout` 遮罩带 `@onclick="WakeAsync"`（`PlaybackEngine.WakeAsync()` → 立刻退出灭屏、回到显示、从当前条重新起算窗口）。灭屏期间控制条是隐藏的（`.board-controls--hidden`），所以点击一定落在遮罩上；实测亮度 58.4 → 打断 0 → 点一下 58.4。
- MAUI(Windows) 的窗口标题来自 `ApplicationTitle`（「新闻看板」）；播放页自身不再输出任何标题文字，关键字标签只在页面顶部显示**一条**（`ShowKeywordLabel` 控制）。
- **设置页排版（iOS 分组列表）**：`.settings-page` 自己就是滚动容器（`height:100%` + `overflow-y:auto`；app.css 把 `html/body` 设成 `overflow:hidden`，不能指望页面级滚动）；顶部栏与底部操作条用 `position:fixed` 贴真实视口（原先 `sticky` 会浮在页面中间）。
  **类名不能叫 `.card`** —— `app.css` 里的 `.card` 是播放页新闻卡片（`display:flex`），设置页复用它会让所有行挤成一行（实测踩过，排错靠 `HtmlRenderer` 导出真实 HTML + Edge `--headless --dump-dom` 读计算样式定位）。设置页分组容器固定叫 `.settings-card`。
  回归测试：`Sa2SettingsPageTests.Settings_UsesGroupedCardLayout_AndCancelIsBackLink`（断言用 `.settings-card`、不得出现 `class="card"`、取消是 `href="/"` 链接）。
- 在 WebView2 宿主里做界面验证时：截图用 `PrintWindow` 可用；但**注入鼠标滚轮/按键前必须先在被测窗口内容区点一下拿焦点**，否则事件不会送达 WebView2（实测：不点则滚轮完全无效，点了则 `scrollTop` 从 0 正常增长）。
