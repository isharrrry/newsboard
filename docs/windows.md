# Windows hosts

**English** | [中文](windows.zh-CN.md) | [Español](windows.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

This page is **one of the platform pages**; for the cross-platform overview see the [README](../README.md) and [Architecture](architecture.md), and the Android side in [Android](android.md).

Windows has **two** hosts. They share the same Razor components (`NewsBoard.Shared`, zero platform dependencies), so the UI and the configuration are identical — only the depth of the platform capabilities differs.

## The two hosts

| Host | Stack / TFM | Notes |
|---|---|---|
| `src/NewsBoard.Wpf` | WPF + WebView2, `net10.0-windows10.0.19041.0` | Desktop host; supports borderless full screen, its blackout guard is "foreground or full screen" |
| the Windows head of `src/NewsBoard.Maui` | .NET MAUI + `BlazorWebView`, same TFM | Shares its host code with the Android head; it does not do borderless full screen, so its guard degrades to "foreground only" |

| Capability | `NewsBoard.Wpf` | MAUI Windows head |
|---|---|---|
| Borderless full screen | supported (F11 enters, Esc exits) | not implemented (`SetFullscreenAsync` is a no-op) |
| Blackout guard | "the app window is foreground **or** borderless full screen" | "foreground only" (the full-screen condition is always false) |
| Keep screen on | no-op | no-op |

## Prerequisites

| Piece | Requirement |
|---|---|
| .NET SDK | 10 (this machine: `10.0.203`, runtime `10.0.7`) |
| WebView2 Runtime | needed by both hosts; measured `154.0.4258.62` here |
| Windows App Runtime | the MAUI Windows head runs **unpackaged** (`WindowsPackageType=None`) and needs it; 1.6 / 1.7 / 1.8 installed here |
| workload | `maui-windows` (installed here) |

The WPF host does not need the Windows App Runtime; the MAUI Windows head fails to start without it.

## Build and run

```powershell
# WPF host (also the fastest way to debug the Blazor layer)
dotnet run --project src\NewsBoard.Wpf

# MAUI Windows head: build first, then run
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# WPF publish (Release)
dotnet publish src\NewsBoard.Wpf -c Release
```

`dotnet publish -c Release` for WPF measured **45 files / 28.14 MB**, and it ran straight from the publish directory (window title "新闻看板", 6 `msedgewebview2` child processes). That output is neither self-contained nor single-file.

## Blackout: the monitor really goes off

When the Windows side enters a blackout it broadcasts `WM_SYSCOMMAND` / `SC_MONITORPOWER` (`2` = off, `-1` = on):

```
SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)
```

The decision and the idempotency live in the shared pure logic `MonitorPowerPolicy`: the monitor is switched off **only when "the app window is foreground or borderless full screen"** (so a user working in another program is not blacked out), but **switching it back on is not subject to that guard** (otherwise, after the screen went off and the user switched away, they could never wake it). The MAUI Windows head does not do borderless full screen, so on that side the full-screen condition is always `false` and the guard degrades to "foreground only".

Unlike Android, this **really powers the monitor down** rather than setting the window brightness to 0.

## Keep screen on

Both Windows hosts implement `IScreenController.SetKeepScreenOnAsync` as a **no-op** — a desktop has no need for it.

## Full screen

The WPF host supports borderless full screen; measured: the default window `1600x1000@(160,10)` → after F11 `1920x1080@(0,0)` filling the screen → Esc restores it, and the result is written back to the configuration.

The MAUI Windows head does **not** do borderless full screen (`SetFullscreenAsync` is a no-op).

## Speech

Both Windows hosts use `System.Speech` (`SpeechSynthesizer`). Measured:

- `Microsoft Huihui Desktop` (zh-CN) is enumerated among the installed voices.
- The `SpeakCompleted` callback is reliable: it fired exactly once after a **6135 ms** utterance, producing a 290 KB WAV.
- Cancelling (`SpeakAsyncCancelAll`) returned within **744 ms**.

Rate and pitch are mapped from the configuration — pitch goes through SSML and falls back to plain text if that fails.

## Auto-start on boot

Written to the registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. The Enable → `IsEnabled` → Disable round trip passed, and after Disable no value is left behind.

## Configuration, cache and API key

The WPF host and the MAUI Windows head **share exactly the same files**:

| Content | Path |
|---|---|
| Configuration | `%LOCALAPPDATA%\NewsBoard\config.json` |
| News cache | `%LOCALAPPDATA%\NewsBoard\news.json` |
| API key | `%APPDATA%\NewsBoard\secret.dat` (DPAPI `CurrentUser` + entropy `NewsBoard.SecretStore.v1`, **never plaintext**) |

Both hosts share one `secret.dat`, so the key works in either; every overwrite of `config.json` leaves a `config.json.bak` behind. The environment variable `NEWSBOARD_DATA_DIR` can override the data directory (common to all three hosts).

## Known traps

1. **WPF must use the versioned TFM.** A bare `net10.0-windows` dies at startup with `FileNotFoundException: Microsoft.Windows.SDK.NET` — `BlazorWebView` uses `WebView2CompositionControl` (D3D composition) internally and needs the CsWinRT projection assemblies, which only the versioned TFM provides.
2. **Never `Stop-Process -Name msedgewebview2` globally.** It also kills the WebView2 of newly created instances and corrupts their user-data directory, after which the MAUI Windows head starts into a black window and exits by itself every time; deleting `bin\...\NewsBoard.Maui.exe.WebView2` restores it. Kill the application process itself instead.
3. **Before injecting mouse-wheel or key events into a WebView2 host, click the content area of the target window first to give it focus**, otherwise the events never reach WebView2 (measured: without the click the wheel does nothing at all, with it `scrollTop` grows from 0 as expected).

`AGENTS.md` remains the authoritative source for every measured Windows fact and trap.

Related: [Contributing](contributing.md) · [Troubleshooting](troubleshooting.md) · [Android](android.md) · [Documentation index](index.md)
