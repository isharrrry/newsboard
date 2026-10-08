# NewsBoard — 大字新闻看板

把当天新闻抓回来、交给大模型筛一遍（摘要 + 重要度），在屏幕上用**大字 + 语音播报**轮播：亮 60 秒 → 全黑 300 秒 → 循环。适合挂在墙上、电视盒或常驻平板当信息屏。

一套 Razor 组件（`NewsBoard.Shared`）+ 两个宿主：

| 宿主 | 目标平台 | 用途 |
|---|---|---|
| `NewsBoard.Wpf` | Windows 桌面（WPF + WebView2） | 完整功能；也是调试 Blazor 层最快的途径 |
| `NewsBoard.Maui` | Android（`net10.0-android`） | 主目标平台，产出 APK |
| `NewsBoard.Maui` 的 Windows 头 | Windows 桌面（`net10.0-windows10.0.19041.0`） | 在 PC 上跑 Android 那套宿主代码 |

---

## 1. 快速开始

前置：**.NET SDK 10**（本机实测 10.0.203 / runtime 10.0.7）、Windows 需 **WebView2 Runtime**（本机已装 154.x）。首次使用要在设置页填一个 DeepSeek API Key，否则抓取与提炼都会失败。

```powershell
# WPF 宿主（推荐先用它看效果）
dotnet run --project src\NewsBoard.Wpf

# MAUI 的 Windows 头
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# 构建 Android APK（产出 bin\Debug\net10.0-android\*-Signed.apk）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# 全解决方案（WPF + MAUI/Android + MAUI/Windows 三头）
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo

# 单元测试
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
```

> Android 构建**必须先点源** `scripts\android-env.ps1`，否则报 `XA5300 找不到 Android SDK`（本机执行策略是 Restricted，故先放开当前进程）。

---

## 2. 界面与操作

看板页（`Display.razor`）：顶部**一条**关键字标签、时钟、大字摘要（图文模式带配图）；右下角横排 5 个图标按钮 —— **上一条 / 下一条 / 打开原文 / 打断本轮 / 设置**。

- **灭屏期间点一下屏幕即恢复显示**（`PlaybackEngine.WakeAsync()`：立刻退出黑屏、从当前条重新起算窗口）。灭屏时控制条是隐藏的，所以点击一定落在黑色遮罩上；触屏 tap 同样有效。
- **打断**：立即进入灭屏等待「黑屏时长」秒，然后从下一轮继续；即使关掉亮熄循环也会灭一次。
- 上一条 / 下一条按列表循环回绕；「打开原文」用系统浏览器打开当前新闻链接。

设置页（`Settings.razor`）按 iOS 分组列表排版，分组顺序：

| 分组 | 内容 |
|---|---|
| **显示模式** | 播放模式（纯文字 / 文字+图片）+「显示新闻图片」派生开关（同源联动） |
| **API / 采集** | 获取方式、BaseUrl / Model / 联网搜索端点与 max_uses、API Key、关键字列表、每关键字篇数、抓取间隔、摘要字数、**摘要语言**、AI 最小调用间隔、RSS 源与备用源、只用当天新闻、**重要度门槛**、**保留天数** |
| **外观** | 背景/前景/强调色、正文字号与字重、行距、内边距、关键字标签字号、对齐、显示时钟、**时钟字号 / 时钟字体**、显示关键字/标题/来源 |
| **播放 / 语音** | 语音播报开关、播报内容、语言、语速、音调、播报前静默、静音时段、TTS 不可用时策略、亮屏时长、单条停留、滚动速度、黑屏时长、亮熄循环、黑屏方式、窗口到点行为、图片位置与占屏比例、轮播顺序、跨轮续播 |
| **系统 / 宿主** | 保持屏幕常亮、开机自启、仅 WiFi 抓取、Windows 无边框全屏、Android 显示状态栏 |

底部固定三个按钮：**保存 / 取消 / 恢复默认**；「取消」是回看板的链接，点了直接返回并丢弃未保存的改动。

> 控件 id 与配置字段基本一一对应（camelCase）：例如 `playbackMode` / `showImages` / `summaryLanguage` / `summaryMaxChars` / `importanceThreshold` / `retentionDays` / `clockFontSize` / `clockFontFamily` / `speechEnabled` / `showDuration` / `blackoutDuration` / `winFullscreen`。完整清单见 `plan §4` 与 `Settings.razor`。

---

## 3. 配置、缓存与密钥在哪

| 版本 | 配置文件 | 新闻缓存 | API Key |
|---|---|---|---|
| WPF | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat`（DPAPI CurrentUser 加密，**不落明文**） |
| MAUI(Windows) | 同上（**与 WPF 完全共用**） | 同上 | 同上（两宿主密钥互通） |
| MAUI(Android) | app 私有目录（`LocalApplicationData`，包名 `com.newsboard.app`） | 同左 | MAUI `SecureStorage`（Android Keystore 加密，非明文文件） |

- **改数据目录**：设置环境变量 `NEWSBOARD_DATA_DIR`（非空即生效，`ConfigStore` 与 `NewsRepository` 共用这个解析点）。跑自动化/探针脚本**必须**用它指向隔离目录，不要碰真实数据目录。
- **误改可回滚**：`ConfigStore` 每次覆盖写盘前会把上一版留成 `config.json.bak`。
- **开机自启**（Windows）写在 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。
- `ApiKey` **永远不会写进 JSON**（字段带 `[JsonIgnore]`，由平台安全存储注入）。

---

## 4. 新闻是怎么来的

```
定时（默认 5 分钟）/ 启动
  → 通道：RSS 频道源（保底） 或 模型联网搜索（默认「自动」= 先联网，失败回退 RSS）
  → URL 去重（已提炼过的永不再调 AI）
  → 描述过短的条目补抓原文正文（每轮最多 5 条）
  → 模型提炼：摘要（默认 70 字）+ 重要度 0–1 + 分类关键字
  → 图文模式：四级取图（og:image → twitter:image → image_src → 正文首图）+ 同一张图只归一条新闻
  → 缓存（按自然日保留 3 天，超期连同去重记录一起清理）
  → 播放列表：按重要度门槛（默认 0.7）过滤 → 按配置顺序（默认随机）播放
```

- 门槛把缓存筛空时，会**随机补一条**并在摘要末尾加 ` ~`，表示它其实没达标。
- 卡在「正在获取新闻…」通常是：还没配 API Key、源站暂时不可用、或门槛设得太高。
- 摘要语言默认简体中文；切换语言后**缓存里的旧摘要不会自动重写**（清掉 `news.json` 或等自然日过期后重抓）。

---

## 5. 开发

```powershell
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo
dotnet test  src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
dotnet build NewsBoard.slnx -v q --nologo          # 三头全量（需先点源 android-env.ps1）
```

约定、冻结契约、全部实测事实（含各种踩过的坑）见 **`AGENTS.md`**；提交前必须构建通过，逻辑分支要配 xunit 测试。

### 文档索引

| 文档 | 内容 |
|---|---|
| `README.md` | 本文件：怎么跑、界面怎么用、配置与数据在哪 |
| `news-board.plan.md` | 实施方案：决策记录、架构、配置清单、任务拆解与完成状态、逐轮迭代记录（§11–§14）、修订记录 |
| `AGENTS.md` | 开发约定：目录与文件所有权、冻结契约、编码约定、运行时实测事实 |
| `docs\android-env-report.md` | Android 工具链环境实测报告（workload / JDK / SDK / NDK 与占用） |

---

## 6. 已知限制

- **Android 未装机验证**：2026-10-08 检测到 1 台已授权真机（Xiaomi `M2011K2C` / Android 13 / arm64-v8a），但**尚未安装运行**；此前的 Android 侧结论（启动自启、沉浸式状态栏、真灭屏、TTS、长稳）都是静态核对或未验证。
- **联网搜索通道拿不到正文**：该通道由模型直接返回摘要，`RawContent` 恒为 null（RSS 通道有真正文）。
- **Windows 真关屏未人工确认**：实现走 `SC_MONITORPOWER` 且带护栏（仅「前台 或 无边框全屏」才关屏，开屏不受限），但自动化里没实机触发过背光变化。
- **摘要语言切换不重写历史缓存**（见 §4）。
- 「只显示命中关键字的新闻」过滤开关**未做**；当前关键字只用于分组标签与展示。
