# NewsBoard 开发约定（AGENTS.md）

本仓库是「安卓大字新闻看板」的实施方案落地：Blazor 类库 + MAUI(Android) 宿主 + WPF(WebView2) 宿主。

## 文档导航与维护纪律

| 文档 | 写什么 |
|---|---|
| `README.md` / `README.zh-CN.md` / `README.es.md` | **使用者视角**（英文为默认版，三语并列）：怎么跑（三种运行形态 + Android 前置点源）、界面与操作（含灭屏点屏唤醒）、设置页分组、配置/缓存/Key 在哪、新闻数据流、实测结果、已知限制 |
| `docs/` | **按主题的正式文档**，每篇三语（`<name>.md` 英 / `<name>.zh-CN.md` 中 / `<name>.es.md` 西）：`index`（索引）、`architecture`、`configuration`、`news-pipeline`、`playback`、`android`、`troubleshooting`、`contributing` |
| `docs/android-env-report.md` | Android 工具链环境实测报告（单语） |
| `plans/news-board.plan.md` | **方案与历史**：决策记录 D1–D5、架构与项目结构、§4 配置项清单（与 `AppConfig` 对齐）、§5 播放引擎、§6 采集与提炼、§8 任务状态表、§10 修订记录 R1–R8、§11–§16 逐轮迭代与事故记录 |
| `CHANGELOG.md` | 版本变更记录（用户可见的 Added / Changed / Fixed / Verified） |
| `AGENTS.md`（本文件） | **开发约定**：命令、目录与文件所有权、冻结契约、编码约定、以及全部**实测事实**（含踩过的坑） |

**纪律：改了行为、配置项或命令，必须在同一次提交里同步这些地方** —— 三语 `README.*`（会改变用户操作方式的部分）、三语 `docs/`（受影响的主题篇，尤其 `configuration`）、`AGENTS.md`（约定与实测事实）、`plans/news-board.plan.md`（§4 配置清单 + 对应迭代章节 + §10 修订记录）、`CHANGELOG.md`（用户可见的变更）。**不允许**出现「代码里有、文档里没有」的配置项。

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

# 构建 + 安装到已连接的设备（**调试包只能这样部署**，见下）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo

# MAUI 的 Windows 头（桌面调试；非打包运行，依赖已安装的 Windows App Runtime）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0
```

当前机器：`.NET SDK 10.0.203`、runtime `10.0.7`；`maui-android`（10.0.20/10.0.100）与 `maui-windows` workload **均已安装**。
MAUI 的 Windows 头用 `WindowsPackageType=None`（非打包）运行，需要 Windows App Runtime（本机已装 1.6 / 1.7 / 1.8）。
Android 工具链在 **`D:\Android`**：JDK 17 于 `D:\Android\jdk-17`、Android SDK 于 `D:\Android\Sdk`（platform-36 / build-tools 36.0.0 / platform-tools）、NDK 27.2.12479018。
**构建 Android 前必须先点源 `scripts\android-env.ps1`**（本机执行策略是 `Restricted`，故需先 `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force`）；否则报 `XA5300 找不到 Android SDK`。环境现状与占用清单见 `docs\android-env-report.md`。
**Android 真机（2026-10-08 末次检测）**：`adb devices` 有 **1 台**已授权设备 —— Xiaomi `M2011K2C`（Redmi K40），Android 13 / SDK 33 / arm64-v8a，序列号 `<device-serial>`，**已装机并跑通**（同日；包名 `com.newsboard.app`，versionCode 1 / minSdk 24 / targetSdk 36）：启动渲染、6 个关键字抓回 16 条、关键字标签 + 时钟 + 大字摘要 + 控制条、`config.json`/`news.json` 落 app 私有目录，均实测通过（细节见 plan §15）。**2026-10-08 已补做 Android 侧逐项走查** —— 灭屏/常亮与 TTS 通过、开机自启被 MIUI 拦、长稳见 plan §16。

**Android 平台三条实测事实（2026-10-08 真机逐项走查，细节见 plan §16）**：
- **「黑屏」≠ 关屏**：`AndroidScreenController.SetBlackoutAsync(true)` 只把窗口 `WindowManager.LayoutParams.ScreenBrightness` 置 `0f` + 画全黑遮罩，**物理背光仍然亮着**（`dumpsys power` 的 `mScreenBrightnessOverrideFromWindowManager` 由 `NaN` 变 `0.0`，同刻截图 300KB → 27KB；点屏幕唤醒后回到 `NaN`，截图恢复）。Android 上普通应用**没有免权限的真熄屏手段**（`PowerManager.GoToSleep()` 是签名权限；`DevicePolicyManager.LockNow()` 要设备管理员、还会连带锁屏、唤醒需解锁）。屏幕常亮由窗口标志承担：`dumpsys window` 里可见 `fl=… KEEP_SCREEN_ON …`、`mLastWakeLockHoldingWindow=…MainActivity` —— **黑屏期间不能撤掉它**，否则系统按 `screen_off_timeout`（本机 600000ms）真息屏。
- **Android TTS 用设备自带引擎**：本机是 `com.xiaomi.mibrain.speech`（`settings get secure tts_default_synth`，`tts_default_locale=null`），绑定成功日志为 `TextToSpeech: Sucessfully bound to …` + `Connected to TTS engine`。是否真的发声**看 `dumpsys audio` 而不是 logcat**：`player piid:N … type:android.media.AudioTrack … content=CONTENT_TYPE_SPEECH` 的 `started→stopped` 时间线就是每次朗读，本机实测单条 **7.4 / 13.1 / 10.6 / 13.7 / 14.6 秒**、条间 ≈1s（= `SpeechLeadSeconds`）。音质只能人耳评估。
- **MIUI 会拦开机自启（非代码缺陷）**：`BootReceiver` 注册与广播派发都正常（`dumpsys activity broadcasts` 里能看到 `Deliver 0 #154: (manifest)` 指向 `crc64017365ed8da6996e.BootReceiver`，`enabled=true exported=true`），但系统紧接着记录 `BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast Intent { act=android.intent.action.BOOT_COMPLETED … }: process is not permitted to auto start`，且 `cmd appops get com.newsboard.app 10021` → `MIUIOP(10021): ignore`。**需用户在 MIUI「自启动管理」里手动放行**（无 root 时 `cmd appops set … 10021 allow` 返回 `No operations.`，代不了；该机型 `com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` 也无法用 `am start` 拉起 —— `Error type 3 … does not exist`，所以 `AutoStartHelper.OpenAutoStartSettings()` 在本机会退回到应用详情页，属优雅降级不是 bug）。
**Android 部署（实测踩过，非产品缺陷）**：Debug 构建走 .NET Android 的 **Fast Deployment** —— 程序集不在 APK 里，而是放在设备上 `files/.__override__/<abi>` 目录、由部署目标单独推送。**只 `adb install` 那个 `*-Signed.apk` 会启动即崩**：`Fatal signal 6 (SIGABRT)`，`Abort message: No assemblies found in '/data/user/0/com.newsboard.app/files/.__override__/arm64-v8a' ... Assuming this is part of Fast Deployment. Exiting...`（backtrace 为 `libc.so (abort)` ← `libmonodroid.so (abort_application)`）。
部署到设备请用 **`-t:Install`**（`-t:Run` 亦可，它会顺带启动）；要一个能直接分发安装的自包含 APK，请用 Release 构建。（装机后如需确认程序集已推送：`adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l`，本次为 440 个文件。）

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
- **MAUI 宿主页（`src/NewsBoard.Maui/wwwroot/index.html`）绝不可抢在框架之前调用 `Blazor.start()`**：MAUI 的 `BlazorWebView` 会**自己**启动 Blazor 并注册 `MainPage.xaml` 里声明的根组件（`Selector="#app"`）。宿主页脚本一旦抢跑，就会用「没有根组件」的方式把 Blazor 启动起来，框架随后带根组件的启动被拒（控制台 `Uncaught (in promise) Error: Blazor has already started.`，调用方是框架注入的匿名脚本），结果是**进程活着、WebView 在、`#app` 一片空白**（Android 真机实测，2026-10-08；`pidof` 有值、`uiautomator` 里 WebView 铺满屏幕、CDP 查到 `#app.innerHTML.length === 0`）。
  正确写法：宿主页只用定时器观察 —— `#app` 有子节点就撤掉 `#boot-hint` 并结束；超过 3s 仍为空才兜底 `Blazor.start()`（兜底也安全：此时框架必然已过了页面加载，重复调用只会抛错被吞）。回归测试 `Sa5MauiHostPageTests.MAUI宿主页_不抢在框架之前启动Blazor`（断言 `Blazor.start()` 出现在 `setInterval` 之后，断言前先剥掉行注释，否则注释里的字样会误伤）。
  **勘误**：早前这里记的「MAUI 的 index.html 必须显式 `Blazor.start()`、重复调用是幂等的」是**错误结论** —— Android 上重复调用不是幂等的，且抢跑会直接导致黑屏。WPF 的 `index.html` 是另一份文件（**没有** `autostart="false"`，走默认自动启动 + 显式 start），未受影响、保持不变。
- **MAUI 宿主必须在启动期调用 `InitializeNewsBoardAsync()`**（现于 `MauiProgram.CreateMauiApp()` 末尾 fire-and-forget）；不调用则配置（含 API Key）不加载、抓取调度器不启动 —— 症状是画面永远停在「正在获取新闻…」且进程零网络连接（实测踩过）。`Display.razor` 起播前也会 `await Services.InitializeNewsBoardAsync()` 兜底（幂等）。
- 摘要默认 **70 字**、卡片默认**不显示标题**（`AppConfig.ShowTitle=false`）；两条通道（RSS 提炼 / 联网搜索）都要过 `SummaryText.Normalize` 本地二次校验（上限 1.2 倍 → 截到最后一个句号），因为模型实测会超字数。实测（2026-10-08，默认 70）：RSS 提炼 53–66 字、联网搜索 72–84 字。
- **摘要语言**：两条通道的提示词都会写入 `config.SummaryLanguage.PromptRule()`。**实测（2026-10-08）**：加这条之前，联网通道检索港台源时摘要会夹带繁体（9 篇里 6 篇命中 1–3 个繁体字）；加之后同样关键字复测 **9 篇全 0**（判定用「只含繁体专有字」的字集，简繁同形字如 路/新/果/大 必须排除，否则误报）。
- **重要度筛选（模型打分 + 本地均衡）**：两条通道的提示词都要求模型给出 `importance`（0.00–1.00，**相对最近三天这批新闻**、要拉开差距：重大 0.80–1.00 / 例行 0.20–0.50）；`ImportanceScoring.Read()` 容错解析（数字/数字字符串、越界夹取、非法→null）；`ImportanceScoring.Balance()` 在**整批退化**（极差 < 0.1 或 ≥80% 堆在 ≥0.85/≤0.15）且 ≥3 条时按秩在 [0.15, 0.95] 展开；`NewsRepository.GetPlaylist` **先按 `ImportanceThreshold` 过滤再排序**（`Importance` 为 null 的旧缓存保留，等于门槛也保留），随机顺序缓存键包含门槛（改门槛立即重排）。
  实测（2026-10-08，1 关键字 RSS + 2 关键字联网）：缓存 16 条分布 `0.2-0.4:4 / 0.4-0.6:5 / 0.6-0.8:4 / 0.8-1.0:3`，过 0.7 门槛显示 6 条 —— 既不堆高分也不堆低分。
  **注意**：门槛过滤后播放列表可能为空（「正在获取新闻…」会一直显示），这是配置语义而非卡死。
- **`RawContent`**：入库时按 `NewsSummarizer.TruncateRawContent` 截到 **2000 字**（便于回看/二次提炼）。注意这个上限作用在**入库的 `RawContent`** 上；源头的 `RawNewsItem.Description` 不截断（早前记成「RSS 把 Description 截断到 2000 字」，**有误**）。
  **勘误（2026-10-09，代码 + 实测）**：早前写的「**联网通道拿不到正文**，该字段恒为 null」**是错的**。联网通道自身确实不返回正文（`WebSearchNewsSource` 构造 `RawNewsItem` 时不设 `Description`），但正文补抓 `BackfillRawContentAsync` 在 `NewsFetchCoordinator` 里位于**通道分支之前**、对两条通道的新条目统一生效；筛选条件 `Description < 300` 字让联网条目**必然入选**，补抓成功后 `Description` 被写回、构造 `NewsItem` 时 `RawContent = TruncateRawContent(r.Description)` 即非 null。
  **实测**：本机真实缓存 `%LOCALAPPDATA%\NewsBoard\news.json` 共 313 条（全部联网来源），其中 **92 条 `RawContent` 非 null**，长度 109–2000 字符；补抓失败的条目仍为 null。另：少数条目抓到的是站点模板文案（实测出现过以「调查问题加载中，请稍候。若长时间无响应，请刷新本页面」开头的正文），`ExtractMainText` 目前不清这类占位。
- **图文模式的配图来源（实测，2026-10-08）**：中新网、人民网文章页**都没有 `og:image`/`twitter:image`**，RSS 也没有 `enclosure`/`media:*` ⇒ 只看 meta 会永远取不到图。`ImageResolver` 现为四级：og:image → twitter:image → `link[rel=image_src]` → **正文首图**（优先正文容器内、`<p>` 之后、跳过 logo/图标/占位图/早于文章日期 60 天以上的模板素材），且**逐个候选发 Range 请求校验真的返回 `image/*`**（中新网图床 `i2.chinanews.com.cn` 防盗链，直接取回的是 html）。实测 8 篇真实文章：修前 8/8 命中但 4 篇拿到同一张模板图、1 篇页脚图；修后 7/8 命中且**各不相同**的真实配图。
  图文模式下每轮还会为「缓存里已存在但没图」的旧条目补抓配图（每轮上限 8 条，`NewsFetchCoordinator.MaxImageBackfill`）。
- **灭屏期间点一下屏幕即唤醒**：`.blackout` 遮罩带 `@onclick="WakeAsync"`（`PlaybackEngine.WakeAsync()` → 立刻退出灭屏、回到显示、从当前条重新起算窗口）。灭屏期间控制条是隐藏的（`.board-controls--hidden`），所以点击一定落在遮罩上；实测亮度 58.4 → 打断 0 → 点一下 58.4。
- **Windows 上的「真灭屏」**：进黑屏时 `SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)` 关/开显示器，判定与幂等由 Shared 的纯逻辑 `MonitorPowerPolicy` 负责：**只有「本应用窗口是前台 或 已无边框全屏」才关屏**（避免用户在其他程序里被黑屏），但**退出黑屏的开屏不受护栏限制**（否则关屏后用户切走会醒不过来）。MAUI Windows 头目前不做无边框全屏，故其 fullscreen 恒 false、护栏退化为「仅前台」。
- **首帧闸门（修「已在朗读但画面还在加载」）**：`PlaybackEngine.ArmFirstFrameGate()` 后，首条会在「T_lead 之后、朗读之前」等待 `MarkFirstFrameRendered()`（上限 8s，可被停止/切条/窗口到点打断）；`Display.razor` 在首帧渲染且 `Current != null` 时只报告一次。**实测**：修复前 TTS 早于画面 1.9s、启动段 2.9–11.3s 纯黑无字；修复后画面早于朗读 2.3–3.1s，且两端 `index.html` 有可见的 `#boot-hint`（原「正在启动…」是黑字黑底看不见）。
- **自动化坑（非产品缺陷）**：**不要全局 `Stop-Process -Name msedgewebview2`** —— 会连带杀掉新建实例的 WebView2 并**破坏其用户数据目录**，之后 MAUI Windows 每次启动都是黑窗且进程自退；删掉 `bin\...\NewsBoard.Maui.exe.WebView2` 即恢复（实测踩过）。清理时要杀应用进程本身。
- **Windows PowerShell 5.1 跑 .ps1 的编码坑**：本机没有 pwsh 7，`powershell.exe` 按 ANSI 读脚本，**含中文的 UTF-8 无 BOM 脚本会解析失败**（字符串被截断）。验证脚本要么全 ASCII，要么用 `[System.IO.File]::WriteAllText($p,$text,(New-Object System.Text.UTF8Encoding($true)))` 加上 BOM 再跑。同理**绝不要**用 `Get-Content`/`Set-Content` 往返源码文件（会按 ANSI 解码把中文注释写坏，实测把一个 .cs 变成无效 UTF-8）。
- MAUI(Windows) 的窗口标题来自 `ApplicationTitle`（「新闻看板」）；播放页自身不再输出任何标题文字，关键字标签只在页面顶部显示**一条**（`ShowKeywordLabel` 控制）。
- **设置页排版（iOS 分组列表）**：`.settings-page` 自己就是滚动容器（`height:100%` + `overflow-y:auto`；app.css 把 `html/body` 设成 `overflow:hidden`，不能指望页面级滚动）；顶部栏与底部操作条用 `position:fixed` 贴真实视口（原先 `sticky` 会浮在页面中间）。
  **类名不能叫 `.card`** —— `app.css` 里的 `.card` 是播放页新闻卡片（`display:flex`），设置页复用它会让所有行挤成一行（实测踩过，排错靠 `HtmlRenderer` 导出真实 HTML + Edge `--headless --dump-dom` 读计算样式定位）。设置页分组容器固定叫 `.settings-card`。
  回归测试：`Sa2SettingsPageTests.Settings_UsesGroupedCardLayout_AndCancelIsBackLink`（断言用 `.settings-card`、不得出现 `class="card"`、取消是 `href="/"` 链接）。
- 在 WebView2 宿主里做界面验证时：截图用 `PrintWindow` 可用；但**注入鼠标滚轮/按键前必须先在被测窗口内容区点一下拿焦点**，否则事件不会送达 WebView2（实测：不点则滚轮完全无效，点了则 `scrollTop` 从 0 正常增长）。
- **保留窗口按自然日分桶**（`RetentionWindow.EarliestKeptInstant/RetentionSpan`，纯逻辑）：`RetentionDays=3` ＝ 保留「今天 + 前两天」共 3 个自然日，起点是前天的本地零点（`TimeZoneInfo.Local`）。抓取侧传 `RetentionWindow.RetentionSpan(now, config.RetentionDays)` 给 `PruneExpired`，**签名未变**。与滑动窗口（now − N×24h）的差别：早上 6 点与晚上 23 点看到的是同一批自然日。
- **分类关键字（画面上那条标签）**：共享频道源（`RssTemplate` 不含 `{q}`）抓回整频道，条目原本只带频道名。现在两道处理：
  ① `KeywordClassifier.Classify(标题, 描述/正文, 配置关键字, 频道名)` —— 按配置顺序找第一个命中项（标题优先、内容兜底），都不命中才回落到频道名；
  ② `NewsSummarizer` 提示词第 9 条要求模型为每条输出 `keyword`（从配置关键字里挑最贴合的、原样照抄、都不贴合留空），解析时用 `KeywordClassifier.Match` 做**白名单校验**（防止冒出配置外的自造分类），模型给的优先于字符串匹配。
  **实测（2026-10-08）**：`Keywords=武汉/AI` + 中新网滚动新闻频道 → 当天 9 条全是国庆/日本/A股类新闻，标题与正文都不含武汉/AI，模型也留空 ⇒ 标签回落成频道名「中新网即时新闻」。**这是「没有命中」而不是「分类丢失」**；要强制只看命中关键字的新闻，需要另加过滤开关（尚未做）。
- **正文抓取**：`ArticleContentFetcher`（新文件，`FetchAsync` / 静态 `ExtractMainText`；无第三方包、5s 超时、异常一律吞成 null）。`NewsFetchCoordinator.BackfillRawContentAsync` 对本轮新条目中 `Description` < 300 字的、每轮最多 5 条、并发 2 抓原文正文；抓到就**写回 `RawNewsItem.Description`**（⇒ 提炼输入与 `RawContent` 都用正文，摘要质量因此提升），并用正文重新归一次类。
  **实测**：重建后的 MAUI Windows 跑一轮，9 条里 4 条 `RawContent` > 300 字（302/620/676/868）。子代理单独对真实站点跑 **9/9 成功**（中新网 137–720 字、IT之家 332/944 字），无乱码、无超时；`ExtractMainText` 会砍掉 `(完)` 之后的推荐位。
  注意：正文 < 60 字一律返回 null ⇒ 极短快讯退回 RSS 描述；本机 .NET 10 **没有** CodePagesEncodingProvider，GBK 站点按 UTF-8 兜底。
- **同一张配图只能属于一条新闻**：`ImageResolver.DedupeImages(items)`（按发布时间从新到旧保留第一条，其余清空）+ `ApplyImagesAsync(..., reservedImageUrls)`（新抓的图不得复用在挂的图；拿不到独占图就按「正文没图」处理，不显示图）。`NewsFetchCoordinator.ApplyImagesAsync` 每轮先用 `NewsRepository.Snapshot()` 全量去重（`Snapshot` 返回同批引用，去重直接改到缓存对象上）。
  **实测**：修复前 9/9 有图但 3 组重复（5 条/4 条/4 条各共用一张模板图）；修复后 **5/9 有图、重复组 0**。
- **门槛过滤后播放列表为空**：`NewsRepository.GetPlaylist` 会随机补一条缓存里的条目（**副本**，不污染缓存），摘要末尾加 `NewsRepository.BelowThresholdSuffix`（`" ~"`）表示「不达标条目」。**实机验证**：把门槛临时调到 0.95 后画面显示「…移送检察机关审查起诉。 ~」。
- **设置页第一屏就是「显示模式」组**（`id="playbackMode"` 下拉 + `id="showImages"` 图文开关，同源联动）；原来散在「外观 / 播放 / 语音」两处的重复控件已移除（id 未变，`Sa7SettingsOptionsTests` 仍绿）。「图片模式找不到」的问题由此解决。
- **各版本配置文件与 API Key 位置**（实测代码路径）：
  | 版本 | 配置 | 新闻缓存 | API Key |
  |---|---|---|---|
  | WPF | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat`（DPAPI CurrentUser + entropy `NewsBoard.SecretStore.v1`） |
  | MAUI(Windows) | 同上（**与 WPF 完全共用**） | 同上 | 同上（同一文件，两宿主密钥互通） |
  | MAUI(Android) | app 私有目录（`Environment.SpecialFolder.LocalApplicationData`，包名 `com.newsboard.app`） | 同左 | MAUI `SecureStorage`（Android Keystore 加密，非明文文件） |
  Windows 侧自启写在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。
- **数据目录可隔离 + 覆盖前自动留备份**：`NewsBoardDataDirectory.Resolve()`（`ConfigStore` 与 `NewsRepository` 共用）在环境变量 **`NEWSBOARD_DATA_DIR`** 非空时用它当数据目录，否则 `%LOCALAPPDATA%\NewsBoard`；`ConfigStore` 每次覆盖写入前把上一版留成 `config.json.bak`。
  **血的教训（2026-10-08）**：4 个 e2e 脚本（`.tmp\e2e.ps1` / `e2e-final.ps1` / `image.ps1` / `scroll.ps1`）直接把测试配置 `Copy-Item` 成用户真实的 `config.json`，把用户 6 个关键字覆盖成「武汉/AI」，而且那份 `config-backup.json` 是在覆盖**之后**复制的（MD5 与被覆盖文件相同）⇒ 等于没备份、从未回滚。
  **纪律：任何自动化/探针脚本要跑真实抓取，必须 `$env:NEWSBOARD_DATA_DIR=<.tmp 下隔离目录>`，绝不写真实数据目录。** 现场证据留在 `.tmp\polluted-config-2026-10-08.json`。
- **时间敏感测试的偶发红（已修稳）**：`Sa3PlaybackEngineTests` / `PlaybackControlTests` 用 `ScaledClock`（**100 倍**）跑，`WaitUntilAsync` 轮询 5ms 真实时间 ＝ 0.5s 引擎时间，机器一忙就会「错过窗口」：`NextAsync_SkipsToFollowingItem` 断言 `CurrentIndex==0` 时首条已播完、`Show60s_ThenBlackout300s...` 贴 59s 检查时实际已过 60s、`SpeechCompletion_IsTheItemClock` 的 5s 容差被调度抖动吃掉、`显示期间点一下屏幕…` 在 `SelectIndex`→`PlayItemAsync` 之间取到前一个时间戳（差 <1ms）。
  修法：首条摘要写长（拉到 ~40 字）、检查点往窗口里侧挪（59s→55s、365s→362s）、容差 5s→20s、取基线前 `Task.Delay(250)`。**修后连续 5 次全量 220/220 全绿**（修前 4 次里 3 次红）。
