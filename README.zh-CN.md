# NewsBoard — 大字新闻看板

[English](README.md) | **中文** | [Español](README.es.md)

> NewsBoard — 大字新闻看板：把当天新闻抓回来，交给大模型筛一遍，在屏幕上用大字和语音轮播。

## 解决什么问题

把当天新闻抓回来、交给大模型筛一遍（摘要 + 重要度），在屏幕上用**大字 + 语音播报**轮播：亮 60 秒 → 全黑 300 秒 → 循环。适合挂在墙上、电视盒或常驻平板当信息屏。

- **大字轮播** —— 每屏 1 条，超出显示区域就滚动；默认亮 60 秒、全黑 300 秒循环。
- **模型筛选** —— 每条带 70 字摘要与 0–1 的重要度，低于重要度门槛（默认 0.7）的不进播放列表。
- **语音播报** —— 系统 TTS 念摘要，念完即切下一条：朗读就是该条的时钟。

NewsBoard 是一个**跨平台**的大字新闻看板：**1 套 Razor 组件（`NewsBoard.Shared`，零平台依赖）+ 多个宿主**。**当前已支持 Android 与 Windows 两个平台**，共 3 个可运行的宿主：

| 平台 | 宿主 | 技术栈 / TFM | 状态 |
|---|---|---|---|
| Android | `NewsBoard.Maui` | .NET MAUI + `BlazorWebView`，`net10.0-android` | 已支持；真机（Android 13 / arm64-v8a）装机实测通过，产出 APK |
| Windows | `NewsBoard.Wpf` | WPF + WebView2，`net10.0-windows10.0.19041.0` | 已支持；本机实测运行，`dotnet publish -c Release` 产出 45 文件 / 28.14MB |
| Windows | `NewsBoard.Maui` 的 Windows 头 | .NET MAUI + `BlazorWebView`，同一 TFM | 已支持；以 `WindowsPackageType=None` 非打包运行，需 Windows App Runtime（本机 1.6 / 1.7 / 1.8） |

![新闻看板在 Android 手机上运行](screenshots/01-board-android.png)

*Android 手机上的看板：一条关键字标签、时钟、大字摘要，以及角落的 5 个控制按钮。*

## 快速开始

前置：**.NET SDK 10**（本机实测 10.0.203 / runtime 10.0.7）；Windows 需要 **WebView2 Runtime**（本机实测 154.0.4258.62）。首次使用要在设置页填一个 DeepSeek API Key，否则抓取与提炼都会失败。

**Windows**

```powershell
# WPF 宿主（推荐先用它看效果）
dotnet run --project src\NewsBoard.Wpf

# MAUI 的 Windows 头
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0
```

**Android**

```powershell
# 构建 Android APK（产出 bin\Debug\net10.0-android\*-Signed.apk）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# 构建 + 安装到已连接的设备（调试包必须走这个目标）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

**全平台**

```powershell
# 全解决方案（WPF + MAUI/Android + MAUI/Windows）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo

# 单元测试
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
```

> Android 构建**必须先点源** `scripts\android-env.ps1`，否则报 `XA5300 找不到 Android SDK`（本机执行策略是 Restricted，故先放开当前进程）。Android 完整前置与装机细节见 [docs/android.zh-CN.md](docs/android.zh-CN.md)。

## 平台能力对照

| 能力 | Android | Windows（`NewsBoard.Wpf`） | Windows（`NewsBoard.Maui` 的 Windows 头） |
|---|---|---|---|
| 黑屏 / 灭屏 | 窗口亮度置 0 + 全黑遮罩，**物理背光仍然亮着** | **真正关掉显示器**（`WM_SYSCOMMAND` / `SC_MONITORPOWER`，2=关 / -1=开） | 同 WPF；但本头不做无边框全屏，护栏退化为「仅前台」 |
| 关屏安全护栏 | 不适用 | 仅在「应用窗口是前台 **或** 已无边框全屏」时关屏；**开屏不受护栏限制** | 同左，但全屏条件恒为 false |
| 屏幕常亮 | `KEEP_SCREEN_ON` 窗口标志承担，**黑屏期间也不撤** | 空实现（桌面无此需求） | 空实现 |
| 全屏 | 沉浸式：隐藏系统状态栏（配置项 `AndroidShowStatusBar`） | 无边框全屏（F11 进入 / Esc 退出；实测 `1600x1000@(160,10)` → `1920x1080@(0,0)`） | 不做（`SetFullscreenAsync` 为空实现） |
| 语音合成 | 设备自带 TTS 引擎（实测 `com.xiaomi.mibrain.speech`） | `System.Speech`（实测枚举到 `Microsoft Huihui Desktop` zh-CN；`SpeakCompleted` 回调可靠） | `System.Speech`（同 WPF） |
| 开机自启 | `BootReceiver` + 前台服务 + 常驻通知；厂商自启动管理（如 MIUI）可能拦截 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | 同 WPF |
| 配置 / 缓存目录 | app 私有目录（包名 `com.newsboard.app`） | `%LOCALAPPDATA%\NewsBoard\`（`config.json` / `news.json`） | 同 WPF，**与 WPF 完全共用** |
| API Key | MAUI `SecureStorage`（Android Keystore 加密，非明文文件） | `%APPDATA%\NewsBoard\secret.dat`（DPAPI CurrentUser + entropy `NewsBoard.SecretStore.v1`） | 同 WPF（2 个宿主共用 1 个密钥） |
| 打开原文 | 系统浏览器（Intent） | 默认浏览器 | 默认浏览器 |
| 数据目录覆盖 | 环境变量 `NEWSBOARD_DATA_DIR`（3 个宿主通用） | 同左 | 同左 |

## 界面与操作

看板页（`Display.razor`）：顶部 **1 条**关键字标签、时钟、大字摘要（图文模式带配图）；右下角横排 5 个图标按钮 —— **上一条 / 下一条 / 打开原文 / 打断本轮 / 设置**。

- **灭屏期间点屏幕即恢复显示**（`PlaybackEngine.WakeAsync()`：立刻退出黑屏、从当前条重新起算窗口）。灭屏时控制条是隐藏的，所以点击一定落在黑色遮罩上；触屏 tap 同样有效。
- **打断**：立即进入灭屏等待「黑屏时长」秒，然后从下一轮继续；即使关掉亮熄循环也仍会灭屏。
- 上一条 / 下一条按列表循环回绕；「打开原文」用系统浏览器打开当前新闻链接。

| 图文模式 | 黑屏 | 设置页 |
|---|---|---|
| ![图文模式](screenshots/02-board-text-image.png) | ![黑屏](screenshots/03-blackout.png) | ![设置页](screenshots/04-settings.png) |

设置页（`Settings.razor`）按 iOS 分组列表排版，分组顺序：

| 分组 | 内容 |
|---|---|
| **显示模式** | 播放模式（纯文字 / 文字+图片）+「显示新闻图片」派生开关（同源联动） |
| **API / 采集** | 获取方式、BaseUrl / Model / 联网搜索端点与 max_uses、API Key、关键字列表、每关键字篇数、抓取间隔、摘要字数、**摘要语言**、AI 最小调用间隔、RSS 源与备用源、只用当天新闻、**重要度门槛**、**保留天数** |
| **外观** | 背景/前景/强调色、正文字号与字重、行距、内边距、关键字标签字号、对齐、显示时钟、**时钟字号 / 时钟字体**、显示关键字/标题/来源 |
| **播放 / 语音** | 语音播报开关、播报内容、语言、语速、音调、播报前静默、静音时段、TTS 不可用时策略、亮屏时长、单条停留、滚动速度、黑屏时长、亮熄循环、黑屏方式、窗口到点行为、图片位置与占屏比例、轮播顺序、跨轮续播 |
| **系统 / 宿主** | 保持屏幕常亮、开机自启、仅 WiFi 抓取、Windows 无边框全屏、Android 显示状态栏 |

底部固定 3 个按钮：**保存 / 取消 / 恢复默认**；「取消」是回看板的链接，点了直接返回并丢弃未保存的改动。

> 控件 id 与配置字段基本直接对应（camelCase）：例如 `playbackMode` / `showImages` / `summaryLanguage` / `summaryMaxChars` / `importanceThreshold` / `retentionDays` / `clockFontSize` / `clockFontFamily` / `speechEnabled` / `showDuration` / `blackoutDuration` / `winFullscreen`。完整清单见 `plans/news-board.plan.md` 与 `Settings.razor`。

## 配置、缓存与密钥在哪

| 版本 | 配置文件 | 新闻缓存 | API Key |
|---|---|---|---|
| WPF | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat`（DPAPI CurrentUser 加密，**不落明文**） |
| MAUI(Windows) | 同上（**与 WPF 完全共用**） | 同上 | 同上（2 个宿主共用 1 个密钥） |
| MAUI(Android) | app 私有目录（`LocalApplicationData`，包名 `com.newsboard.app`） | 同左 | MAUI `SecureStorage`（Android Keystore 加密，非明文文件） |

- **改数据目录**：设置环境变量 `NEWSBOARD_DATA_DIR`（非空即生效，`ConfigStore` 与 `NewsRepository` 共用这个解析点）。跑自动化/探针脚本**必须**用它指向隔离目录，不要碰真实数据目录。
- **误改可回滚**：`ConfigStore` 每次覆盖写盘前会把改前的配置留成 `config.json.bak`。
- **Android 的 Key 怎么进去**：Windows 的 `secret.dat` 用 DPAPI（当前用户）加密、**绑机器**，拷到手机没用。换设备时在应用内「设置 → API / 采集」里重新粘贴 DeepSeek API Key（存进 Android Keystore 加密的 `SecureStorage`，同样不落明文）。
- **开机自启**（Windows）写在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。
- `ApiKey` **永远不会写进 JSON**（字段带 `[JsonIgnore]`，由平台安全存储注入）。

## 新闻是怎么来的

```
定时（默认 5 分钟）/ 启动
  → 通道：RSS 频道源（保底） 或 模型联网搜索（默认「自动」= 先联网，失败回退 RSS）
  → URL 去重（已提炼过的永不再调 AI）
  → 描述过短的条目补抓原文正文（每轮最多 5 条）
  → 模型提炼：摘要（默认 70 字）+ 重要度 0–1 + 分类关键字
  → 图文模式：4 级取图（og:image → twitter:image → image_src → 正文首图）+ 1 张图只归 1 条新闻
  → 缓存（按自然日保留 3 天，超期条目与其去重记录同时清理）
  → 播放列表：按重要度门槛（默认 0.7）过滤 → 按配置顺序（默认随机）播放
```

- 门槛把缓存筛空时，会**随机补 1 条**并在摘要末尾加 ` ~`，表示它其实没达标。
- 卡在「正在获取新闻…」通常是：还没配 API Key、源站暂时不可用、或门槛设得太高。
- 摘要语言默认简体中文；切换语言后**缓存里的旧摘要不会自动重写**（清掉 `news.json` 或等自然日过期后重抓）。

完整规则（去重与 AI 冷却、正文抓取、摘要长度校验、配图独占、自然日分桶）见 [docs/news-pipeline.zh-CN.md](docs/news-pipeline.zh-CN.md)。

## 文档

| 文档 | 内容 |
|---|---|
| [docs/index.zh-CN.md](docs/index.zh-CN.md) | 文档索引：8 篇分册，先看哪一篇 |
| [docs/architecture.zh-CN.md](docs/architecture.zh-CN.md) | 3 个项目与依赖方向、目录树、启动到播放的数据流、冻结契约 |
| [docs/configuration.zh-CN.md](docs/configuration.zh-CN.md) | 配置文件位置、Key 存放、设置页 5 个分组、完整配置项表 |
| [docs/news-pipeline.zh-CN.md](docs/news-pipeline.zh-CN.md) | 2 条通道与回退顺序、去重与 AI 冷却、正文抓取、摘要与重要度、配图 |
| [docs/playback.zh-CN.md](docs/playback.zh-CN.md) | 播放状态机、朗读即时钟、5 个按钮与手势、灭屏的平台差异、长稳结果 |
| [docs/android.zh-CN.md](docs/android.zh-CN.md) | Android 工具链、构建与装机命令、真机验收、3 条平台事实 |
| [docs/windows.zh-CN.md](docs/windows.zh-CN.md) | Windows 宿主：WPF 宿主与 MAUI 的 Windows 头 |
| [docs/troubleshooting.zh-CN.md](docs/troubleshooting.zh-CN.md) | 常见症状 → 原因 → 处置，每条带可执行命令 |
| [docs/contributing.zh-CN.md](docs/contributing.zh-CN.md) | 环境与命令、文件所有权、编码约定、提交前门禁、文档纪律 |
| [docs/android-env-report.md](docs/android-env-report.md) | Android 工具链环境实测报告（单语） |

## 开发

```powershell
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo
dotnet test  src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
dotnet build NewsBoard.slnx -v q --nologo          # 3 头全量（需先点源 android-env.ps1）
```

约定、冻结契约、全部实测事实（含各种踩过的坑）见 `AGENTS.md`；[docs/contributing.zh-CN.md](docs/contributing.zh-CN.md) 写的是协作流程。提交前必须构建通过，逻辑分支要配 xunit 测试。

## 实测

| 项 | 结果 |
|---|---|
| 单元测试 | `dotnet test` **224/224** 通过（13 秒） |
| Android 真机 | Xiaomi `M2011K2C`（Redmi K40）/ Android 13 / arm64-v8a：6 个关键字抓回 **16 条**；点「打断」后窗口亮度由 `NaN` 变 `0.0`（1.2 秒内）、截图 300 KB → 27 KB；TTS 单条 **7.4–14.6 秒**、条间约 1 秒；20 分钟长稳无 FATAL/ANR，内存 230 MB → 295–302 MB 后平台化；冷启动 +5.2 秒画面渲染完成 |
| 2 个 Windows 宿主 | WPF：默认 `1600x1000@(160,10)` 带标题栏 → F11 `1920x1080@(0,0)` 铺满 → Esc 还原；MAUI Windows 头：进程存活、12 个 `msedgewebview2` 子进程、真实新闻渲染；图文模式 `ImageScreenPercent=40` 实测上图下文 |
| RSS 源可用性 | 中新网 `scroll-news` / `china` / `world`、人民网 `politics` / `world`、IT之家可用；Bing `?format=RSS` 返回 HTML、百度 `newsrss` 返回安全验证页、Google News RSS 超时（2026-10-08 实测） |

## 已知限制

**Android**

- **Android 已装机跑通并完成逐项走查**（2026-10-08，Xiaomi `M2011K2C` / Redmi K40 / Android 13 / arm64-v8a）：启动渲染、6 个关键字抓回 16 条、关键字标签 + 时钟 + 大字摘要 + 控制条、配置与缓存落在 app 私有目录；点「打断」后窗口亮度归 0（1.2 秒内生效）且画面全黑、点屏幕唤醒即恢复；屏幕由 `KEEP_SCREEN_ON` 保持常亮；中文 TTS 逐条真实朗读（`dumpsys audio` 里连续 5 次 `CONTENT_TYPE_SPEECH` 的 AudioTrack，单条 7.4–14.6 秒，条间约 1 秒）。
- **Android 的「灭屏」不是物理关屏**：只把**窗口亮度置 0 + 画面全黑**，屏幕背光仍然亮着。Android 上普通应用没有免权限的真熄屏手段（`GoToSleep()` 是签名权限；`LockNow()` 要设备管理员、会连带锁屏且唤醒需解锁）。Windows 版才是**真关屏**（`SC_MONITORPOWER`）。
- **Android 开机自启需要厂商放行**：代码链路正确（`BootReceiver` 确实收到了 `BOOT_COMPLETED`），但在本机被 MIUI 的「自启动管理」拦下（系统日志 `process is not permitted to auto start`，`MIUIOP(10021): ignore`）。**手动放行**：设置 → 应用设置 → 应用管理 → 新闻看板 → **自启动**（打开）；建议同时把「省电策略」设为**无限制**。放行后重启即会拉起常驻通知（不走 `StartActivity`，点通知进看板）。
- **Android 的 TTS 音质取决于手机自带语音引擎**（本机是 `com.xiaomi.mibrain.speech`），音色与自然度要自己听；应用只负责把摘要交给它念。

**Windows**

- **Windows 真关屏未人工确认**：实现走 `SC_MONITORPOWER` 且带护栏（仅「前台 或 无边框全屏」才关屏，开屏不受限），但自动化里没实机触发过背光变化。

**与平台无关**

- **联网搜索通道自身不返回正文，但正文照样会被补抓**：该通道由模型直接给出摘要，不过正文补抓对 2 条通道统一生效（条件是 `Description` 不足 300 字），所以联网条目的 `RawContent` 可以非 null。实测本机缓存 **313 条**（全部来自联网搜索），其中 **92 条** `RawContent` 非 null、长度 109–2000 字符；补抓失败的仍为 null。
- **摘要语言切换不重写历史缓存**（见上文「新闻是怎么来的」）。
- 「只显示命中关键字的新闻」过滤开关**未做**；当前关键字只用于分组标签与展示。

## 许可

[MIT](LICENSE)
