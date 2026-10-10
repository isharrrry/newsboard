# 参与开发

[English](contributing.md) | **中文** | [Español](contributing.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

本页是 `AGENTS.md` 的精简版：装什么、跑什么、什么不许改、还有什么没验。`AGENTS.md` 仍是全部实测事实与踩坑记录的权威出处。

---

## 1. 环境

本机实测：

| 项 | 实测值 |
|---|---|
| .NET SDK | 10.0.203（runtime 10.0.7） |
| workload | `maui-android` 10.0.20 / 10.0.100 与 `maui-windows` —— 均已安装 |
| Windows App Runtime | 1.6 / 1.7 / 1.8（MAUI 的 Windows 头以非打包方式运行，`WindowsPackageType=None`） |
| WebView2 Runtime | 154.x（两个 Windows 宿主都要） |
| JDK | 17，位于 `D:\Android\jdk-17`（Microsoft OpenJDK 17.0.20.1 LTS） |
| Android SDK | `D:\Android\Sdk` —— platform-36 / build-tools 36.0.0 / platform-tools |
| Android NDK | 27.2.12479018 |

Windows 需要 WebView2 Runtime。Android 构建需要上面这套工具链，并且**每个新开的 shell 都要先点源环境脚本**。

---

## 2. 构建与测试命令

```powershell
# 共享类库（所有纯逻辑与 UI 都在这）
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo

# 单元测试
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo

# WPF 宿主（Windows 桌面；也是调试 Blazor 层最快的途径）
dotnet run --project src\NewsBoard.Wpf

# MAUI 的 Windows 头（非打包运行）
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# Android APK（产出 bin\Debug\net10.0-android\*-Signed.apk）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Android：构建 + 安装到已连接的设备 —— 调试包只能这样部署
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo

# 全解决方案（WPF + MAUI/Android + MAUI/Windows）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo
```

任何 Android 构建之前都必须点源 `scripts\android-env.ps1`：不点源会报 `XA5300 找不到 Android SDK`。该脚本幂等，且只设置当前进程的环境变量 —— 绝不改动机器级 / 用户级环境变量。本机执行策略是 `Restricted`，所以要先放开当前进程。

---

## 3. 目录与文件所有权

| 区域 | 文件 | 负责 |
|---|---|---|
| 冻结契约（**任何人不许改**） | `Models/NewsItem.cs`、`Services/AppConfig.cs`、`Services/Abstractions/*.cs`、`_Imports.razor`、`Components/Routes.razor` | lead agent |
| 采集 + AI 提炼 | `Services/RssNewsSource.cs`、`Services/WebSearchNewsSource.cs`、`Services/NewsSummarizer.cs`、`Services/NewsRepository.cs`、`Services/ImageResolver.cs`、`Services/NewsFetchCoordinator.cs` | SA1 |
| 配置 + 持久化 | `Services/ConfigStore.cs`、`Components/Pages/Settings.razor`、`wwwroot/js/measure.js` 之外的设置页辅助 | SA2 |
| 播放引擎 + 播放 UI | `Services/PlaybackEngine.cs`、`Components/Pages/Display.razor`、`Components/Shared/NewsCard.razor`、`wwwroot/css/app.css`、`wwwroot/js/measure.js` | SA3 |
| WPF 宿主 | `src/NewsBoard.Wpf/**` | SA4 |
| Android 宿主 + 环境 | `src/NewsBoard.Maui/**` | SA5 |
| 测试 | `src/NewsBoard.Tests/**`（按主题分文件，勿互相覆盖） | 各 Agent |

---

## 4. 冻结契约

以下签名与字段名不许改：

- `NewsBoard.Shared.Models.NewsItem` / `RawNewsItem` —— 含 `NewsItem.Importance`（`double?`，0–1，null = 未评分）与 `NewsItem.RawContent`（抓到的原始内容），以及 `RawNewsItem.Importance` / `RawNewsItem.Description`。
- `NewsBoard.Shared.Services.AppConfig` 与它的全部枚举 —— 含 `SummaryLanguage`（`SummaryLanguageOption`：`SimplifiedChinese` 默认 / `TraditionalChinese` / `English`，配 `SummaryLanguageText.PromptRule()` 给提示词硬约束、`DisplayName()` 给界面文案）、`ClockFontSizeSp`（默认 32）、`ClockFontFamily`（默认 `""`）、`ImportanceThreshold`（默认 0.7）、`RetentionDays`（默认 3）。`PlayOrder` 默认 **Random**。
- `NewsBoard.Shared.Services.Abstractions`：`IScreenController`、`ISpeechService`、`ISecretStore`、`IAutoStart`、`ILinkOpener`、`INewsSource`、`IConfigProvider`、`INewsRepository`、`IClock`（含 `SystemClock`）。
- `PlaybackEngine` 的手动控制 API：`NextAsync()` / `PreviousAsync()` / `InterruptAsync()`（打断本轮 → 黑屏等待「黑屏时长」秒 → 下一轮；即使 `BlackoutCycleEnabled=false` 也会灭一次），方向切换按列表循环回绕；`WakeAsync()`（黑屏期间点一下屏幕 → 回到显示并从当前条重新起算窗口；显示期间调用无副作用，既不切条也不重算窗口）。
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` / `InitializeNewsBoardAsync()` —— 宿主统一装配入口（先注册平台实现，再调用它；启动期必须 await `InitializeNewsBoardAsync()`，否则播放列表为空）。
- `_Imports.razor` 的 using 集合，以及 `Components/Routes.razor` 作为 `BlazorWebView.RootComponent`。

需要新增接口或字段时：在自己的文件里加 `partial`，或在实现内私有化 —— **不要**动上面这些文件。

---

## 5. 编码约定

- 目标框架：Shared/测试 = `net10.0`；**WPF = `net10.0-windows10.0.19041.0`（必须带版本化平台号）**；MAUI = `net10.0-android` + `net10.0-windows10.0.19041.0`。WPF 用裸 `net10.0-windows` 会启动即崩：`FileNotFoundException: Microsoft.Windows.SDK.NET` —— `BlazorWebView` 内部用 `WebView2CompositionControl`（D3D 合成），需要只有版本化 TFM 才提供的 CsWinRT 投影程序集。
- **不引入第三方 NuGet 依赖** —— HTTP 用 `HttpClient`，JSON 用 `System.Text.Json`，RSS 用 `XDocument`，测试用 xunit。例外只有：WPF 宿主的 `Microsoft.AspNetCore.Components.WebView.Wpf`（10.0.0）、`System.Speech`（10.0.7，方案指定的 TTS 实现）、以及 MAUI 的 Windows 头必需的 `System.Security.Cryptography.ProtectedData`（10.0.0，因为 MAUI 不走 WindowsDesktop 框架）。WPF 侧 DPAPI **不需要**加包 —— `UseWPF=true` 时由框架提供，加包反而报 `NU1510`。
- `Nullable` 与 `ImplicitUsings` 已开启；代码注释用简体中文，且只在必要处写。
- Shared 项目**零平台依赖** —— 禁止 `#if ANDROID` / `#if WINDOWS`；平台能力只能通过 `Abstractions` 接口注入。
- 所有面向用户的新增文本用简体中文。
- 提交前必须 `dotnet build` 通过；有逻辑的分支要配 xunit 测试。

---

## 6. 提交前门禁

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo     # 三头都必须是 0 警告 0 错误
dotnet test  src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
```

- 全解决方案构建必须是 **0 警告 / 0 错误**（WPF + MAUI/Android + MAUI/Windows）。
- `dotnet test` 必须全绿。实测基线：**224/224**。
- 不要交付带有已知红测试的改动。如果测试是真的偶发红，先把偶发性修掉 —— 时间敏感测试当初是怎么修稳的，见故障排查第 10 条。

---

## 7. 文档纪律

改了行为、配置项或命令，必须在**同一次提交**里更新文档：

| 改了什么 | 要动哪里 |
|---|---|
| 用户可见行为 / 怎么跑 | 三份 `README.*` |
| 某一主题篇 | 受影响的 `docs/` 页面的三个语言版本（配置项变更尤其要动 `configuration`） |
| 约定与实测事实 | `AGENTS.md` |
| 方案、配置清单、历史 | `plans/news-board.plan.md`（§4 配置清单 + 对应迭代章节 + §10 修订记录） |
| 任何用户会注意到的东西 | `CHANGELOG.md` |

**不允许出现「代码里有、文档里没有」的配置项。** 同样，文档里的数字必须是本仓已有的实测值 —— 一个说法背后没有实测，就写「未验证」或者删掉。

---

## 8. 尚未验证清单

照实写，不写成「应该可以」：

1. **Windows 真关屏没有人工确认过。** `SC_MONITORPOWER` 与 `MonitorPowerPolicy` 护栏都已实现，但没有任何一次自动化运行观察到真实的背光变化。
2. **Android 开机自启一次都没成功过。** 链路正确，但 MIUI 会拦，需要用户手动授予自启动权限；在那之前系统日志以 `process is not permitted to auto start` 结束。
3. **Android 长稳只测了 20 分钟**（无崩溃、无 ANR、内存在 295–302 MB 区间平台化），而方案里的验收口径是 2 小时。
4. **Android 沉浸式全屏（状态栏隐藏）没有在真机上验证过。**
5. **Release APK 从未装到设备上** —— 实际部署的都是 Debug 包（Debug 14.97 MB / Release 28.77 MB）。
6. **Release trimming / AOT 未启用**，WPF 的发布产物也不是自包含、不是单文件。
7. **切换摘要语言不会重写缓存里的旧摘要** —— 必须删掉 `news.json`，或等自然日保留期过期。
8. **「只显示命中关键字的新闻」过滤开关不存在**；关键字目前只用作分组标签。
9. **TTS 驱动的滚动调速**（念完那一刻正好滚到底）只有单测覆盖，没有做音频级验证。
10. **「在设置页改完 → 杀进程 → 重启」这条往返**没有做自动化验证。
11. **本机没有模拟器 / AVD。**

---

相关：[故障排查](troubleshooting.zh-CN.md) · [架构](architecture.zh-CN.md) · [Android 笔记](android.zh-CN.md) · [文档索引](index.zh-CN.md)
