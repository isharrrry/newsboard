# Windows 宿主

[English](windows.md) | **中文** | [Español](windows.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

本篇是 NewsBoard 的**平台篇之一**；跨平台总览见 [README](../README.zh-CN.md) 与 [架构](architecture.zh-CN.md)，Android 侧见 [Android](android.zh-CN.md)。

Windows 上有**两个**宿主，它们共用同一套 Razor 组件（`NewsBoard.Shared` 零平台依赖），所以界面与配置完全一致，差别只在平台能力的实现深度。

## 两个宿主

| 宿主 | 技术栈 / TFM | 说明 |
|---|---|---|
| `src/NewsBoard.Wpf` | WPF + WebView2，`net10.0-windows10.0.19041.0` | 桌面宿主；支持无边框全屏，灭屏护栏是「前台或全屏」 |
| `src/NewsBoard.Maui` 的 Windows 头 | .NET MAUI + `BlazorWebView`，同一 TFM | 与 Android 头共用同一份宿主代码；不做无边框全屏，所以灭屏护栏退化为「仅前台」 |

| 能力 | `NewsBoard.Wpf` | MAUI 的 Windows 头 |
|---|---|---|
| 无边框全屏 | 支持（F11 进入 / Esc 退出） | 不做（`SetFullscreenAsync` 是空实现） |
| 灭屏护栏 | 「应用窗口是前台 **或** 已无边框全屏」 | 「仅前台」（全屏条件恒为 false） |
| 屏幕常亮 | 空实现 | 空实现 |

## 前置条件

| 组件 | 要求 |
|---|---|
| .NET SDK | 10（本机 `10.0.203`、运行时 `10.0.7`） |
| WebView2 Runtime | 两个宿主都要；本机实测 `154.0.4258.62` |
| Windows App Runtime | MAUI 的 Windows 头以 `WindowsPackageType=None` **非打包**运行，需要它；本机已装 1.6 / 1.7 / 1.8 |
| workload | `maui-windows`（本机已装） |

WPF 宿主不需要 Windows App Runtime；MAUI 的 Windows 头缺了它启动会失败。

## 构建与运行

```powershell
# WPF 宿主（也是调试 Blazor 层最快的途径）
dotnet run --project src\NewsBoard.Wpf

# MAUI 的 Windows 头：先构建，再运行
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# WPF 发布（Release）
dotnet publish src\NewsBoard.Wpf -c Release
```

WPF 的 `dotnet publish -c Release` 实测产出 **45 个文件 / 28.14 MB**，从发布目录直接运行成功（窗口标题「新闻看板」，`msedgewebview2` 子进程 6 个）。该产物既不自主包含也不是单文件。

## 灭屏：真的关掉显示器

Windows 侧进入黑屏时广播 `WM_SYSCOMMAND` / `SC_MONITORPOWER`（`2` = 关、`-1` = 开）：

```
SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)
```

判定与幂等由共享的纯逻辑 `MonitorPowerPolicy` 负责：**只有「应用窗口是前台 或 已无边框全屏」时才关屏**（避免用户正在别的程序里工作时被黑屏），但**退出黑屏时的开屏不受这道护栏限制**（否则关了屏又切走，用户会醒不过来）。MAUI 的 Windows 头不做无边框全屏，所以它那一侧的全屏条件恒为 `false`，护栏退化成「仅前台」。

与 Android 不同，这里是**真的把显示器关掉**，而不是把窗口亮度置 0。

## 屏幕常亮

两个 Windows 宿主的 `IScreenController.SetKeepScreenOnAsync` 都是**空实现** —— 桌面没有这个需求。

## 全屏

WPF 宿主支持无边框全屏，实测：默认窗口 `1600x1000@(160,10)` → F11 后 `1920x1080@(0,0)` 铺满整屏 → Esc 还原，切换结果同步落盘到配置。

MAUI 的 Windows 头**不做**无边框全屏（`SetFullscreenAsync` 是空实现）。

## 语音

两个 Windows 宿主都用 `System.Speech`（`SpeechSynthesizer`）。实测：

- 枚举到中文语音 `Microsoft Huihui Desktop`（zh-CN）。
- `SpeakCompleted` 回调可靠：实测朗读 **6135 ms** 后精确触发一次，产出 290 KB WAV。
- 取消（`SpeakAsyncCancelAll`）在 **744 ms** 内返回。

语速与音调按配置映射 —— 音调走 SSML，失败时退化为纯文本。

## 开机自启

写在注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。Enable → `IsEnabled` → Disable 往返实测通过，Disable 之后没有残留值。

## 配置、缓存与 API Key

WPF 与 MAUI 的 Windows 头**完全共用**同一套文件：

| 内容 | 路径 |
|---|---|
| 配置 | `%LOCALAPPDATA%\NewsBoard\config.json` |
| 新闻缓存 | `%LOCALAPPDATA%\NewsBoard\news.json` |
| API Key | `%APPDATA%\NewsBoard\secret.dat`（DPAPI `CurrentUser` + entropy `NewsBoard.SecretStore.v1`，**不落明文**） |

两个宿主共用同一份 `secret.dat`，所以密钥互通；`config.json` 每次覆盖写之前会留一份 `config.json.bak`。环境变量 `NEWSBOARD_DATA_DIR` 可以覆盖数据目录（三个宿主通用）。

## 已知的坑

1. **WPF 必须用版本化 TFM。** 裸 `net10.0-windows` 启动即崩：`FileNotFoundException: Microsoft.Windows.SDK.NET` —— `BlazorWebView` 内部用 `WebView2CompositionControl`（D3D 合成），需要 CsWinRT 投影程序集，而只有版本化 TFM 提供它。
2. **不要全局 `Stop-Process -Name msedgewebview2`。** 这会连带杀掉新建实例的 WebView2 并破坏其用户数据目录，之后 MAUI 的 Windows 头每次启动都是黑窗且进程自退；删掉 `bin\...\NewsBoard.Maui.exe.WebView2` 即恢复。清理时要杀应用进程本身。
3. **在 WebView2 宿主里注入鼠标滚轮或按键之前，必须先在被测窗口的内容区点一下拿焦点**，否则事件不会送达 WebView2（实测：不点则滚轮完全无效，点了则 `scrollTop` 从 0 正常增长）。

Windows 侧全部实测事实与踩坑记录以 [AGENTS.md](../AGENTS.md) 为准。

相关：[参与开发](contributing.zh-CN.md) · [故障排查](troubleshooting.zh-CN.md) · [Android](android.zh-CN.md) · [文档索引](index.zh-CN.md)
