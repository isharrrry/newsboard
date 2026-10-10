# Contributing

**English** | [中文](contributing.zh-CN.md) | [Español](contributing.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

This page is the short version of `AGENTS.md`: what to install, what to run, what you may not change, and what is still unverified. `AGENTS.md` remains the authoritative list of measured facts and traps.

---

## 1. Environment

Measured on this machine:

| Item | Measured value |
|---|---|
| .NET SDK | 10.0.203 (runtime 10.0.7) |
| Workloads | `maui-android` 10.0.20 / 10.0.100 and `maui-windows` — both installed |
| Windows App Runtime | 1.6 / 1.7 / 1.8 (the MAUI Windows head runs unpackaged, `WindowsPackageType=None`) |
| WebView2 Runtime | 154.x (both Windows hosts) |
| JDK | 17 at `D:\Android\jdk-17` (Microsoft OpenJDK 17.0.20.1 LTS) |
| Android SDK | `D:\Android\Sdk` — platform-36 / build-tools 36.0.0 / platform-tools |
| Android NDK | 27.2.12479018 |

Windows needs the WebView2 Runtime. Android builds need the toolchain above and, in every fresh shell, the environment script.

---

## 2. Build and test commands

### Shared library and tests

```powershell
# Shared library (all pure logic and UI live here)
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo

# Unit tests
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo

# Whole solution (WPF + MAUI/Android + MAUI/Windows)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo
```

### Windows hosts

```powershell
# WPF host (Windows desktop; also the fastest way to debug the Blazor layer)
dotnet run --project src\NewsBoard.Wpf

# MAUI Windows head (unpackaged)
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-windows10.0.19041.0 -v q --nologo
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0
```

### Android host

```powershell
# Android APK (output: bin\Debug\net10.0-android\*-Signed.apk)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Android: build + install on the connected device — Debug packages can ONLY be deployed this way
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

Sourcing `scripts\android-env.ps1` is mandatory before any Android build: without it you get `XA5300 找不到 Android SDK`. The script is idempotent and only sets variables in the current process — it never touches machine-level or user-level environment variables. The local execution policy is `Restricted`, hence the `Set-ExecutionPolicy -Scope Process` line.

Per-platform prerequisites, run modes and capability differences are in [Android](android.md) and [Windows hosts](windows.md).

---

## 3. Repository layout and file ownership

| Area | Files | Owner |
|---|---|---|
| Frozen contract (**nobody may change**) | `Models/NewsItem.cs`, `Services/AppConfig.cs`, `Services/Abstractions/*.cs`, `_Imports.razor`, `Components/Routes.razor` | lead agent |
| Fetching + AI summarization | `Services/RssNewsSource.cs`, `Services/WebSearchNewsSource.cs`, `Services/NewsSummarizer.cs`, `Services/NewsRepository.cs`, `Services/ImageResolver.cs`, `Services/NewsFetchCoordinator.cs` | SA1 |
| Configuration + persistence | `Services/ConfigStore.cs`, `Components/Pages/Settings.razor`, Settings page helpers other than `wwwroot/js/measure.js` | SA2 |
| Playback engine + playback UI | `Services/PlaybackEngine.cs`, `Components/Pages/Display.razor`, `Components/Shared/NewsCard.razor`, `wwwroot/css/app.css`, `wwwroot/js/measure.js` | SA3 |
| WPF host | `src/NewsBoard.Wpf/**` | SA4 |
| Android host + environment | `src/NewsBoard.Maui/**` | SA5 |
| Tests | `src/NewsBoard.Tests/**` (one file per topic — do not overwrite each other's files) | every agent |

---

## 4. Frozen contracts

Do not change these signatures or field names:

- `NewsBoard.Shared.Models.NewsItem` / `RawNewsItem` — including `NewsItem.Importance` (`double?`, 0–1, null = not scored) and `NewsItem.RawContent` (the fetched raw content), plus `RawNewsItem.Importance` / `RawNewsItem.Description`.
- `NewsBoard.Shared.Services.AppConfig` and all of its enums — including `SummaryLanguage` (`SummaryLanguageOption`: `SimplifiedChinese` default / `TraditionalChinese` / `English`, with `SummaryLanguageText.PromptRule()` for the prompt constraint and `DisplayName()` for UI text), `ClockFontSizeSp` (default 32), `ClockFontFamily` (default `""`), `ImportanceThreshold` (default 0.7), `RetentionDays` (default 3). `PlayOrder` defaults to **Random**.
- `NewsBoard.Shared.Services.Abstractions`: `IScreenController`, `ISpeechService`, `ISecretStore`, `IAutoStart`, `ILinkOpener`, `INewsSource`, `IConfigProvider`, `INewsRepository`, `IClock` (with `SystemClock`).
- `PlaybackEngine`'s manual control API: `NextAsync()` / `PreviousAsync()` / `InterruptAsync()` (interrupt this round → blackout for "blackout duration" seconds → next round; it blacks out once even when `BlackoutCycleEnabled=false`), with direction changes wrapping around the list; `WakeAsync()` (during blackout, one tap on the screen returns to display and restarts the window from the current item; during display it has no side effect, neither advancing nor restarting the window).
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` / `InitializeNewsBoardAsync()` — the single assembly entry point for hosts (register platform implementations first, then call it; startup must await `InitializeNewsBoardAsync()` or the playlist stays empty).
- The `using` set of `_Imports.razor`, and `Components/Routes.razor` as the `BlazorWebView.RootComponent`.

To add an interface or field, use `partial` in your own file or keep it private inside the implementation — do not touch the files above.

---

## 5. Coding conventions

- Target frameworks: Shared/tests = `net10.0`; **WPF = `net10.0-windows10.0.19041.0` (the versioned platform number is mandatory)**; MAUI = `net10.0-android` + `net10.0-windows10.0.19041.0`. A bare `net10.0-windows` makes the WPF host die at startup with `FileNotFoundException: Microsoft.Windows.SDK.NET`, because `BlazorWebView` uses `WebView2CompositionControl` (D3D composition) and needs the CsWinRT projection assemblies that only the versioned TFM provides.
- **No third-party NuGet dependencies** — HTTP uses `HttpClient`, JSON uses `System.Text.Json`, RSS uses `XDocument`, tests use xunit. The only exceptions are the WPF host's `Microsoft.AspNetCore.Components.WebView.Wpf` (10.0.0), `System.Speech` (10.0.7, the TTS implementation named by the plan), and `System.Security.Cryptography.ProtectedData` (10.0.0) which the MAUI Windows head needs because MAUI does not use the WindowsDesktop framework. On the WPF side DPAPI needs **no** package — `UseWPF=true` provides it, and adding the package triggers `NU1510`.
- `Nullable` and `ImplicitUsings` are enabled; code comments are written in Simplified Chinese, and only where they are actually needed.
- The Shared project has **zero platform dependencies** — no `#if ANDROID` / `#if WINDOWS`. Platform capabilities are injected through the `Abstractions` interfaces.
- All new user-facing text is written in Simplified Chinese.
- `dotnet build` must pass before you commit, and any branch with logic needs an xunit test.

---

## 6. Pre-commit gate

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo     # must be 0 warnings, 0 errors across all three heads
dotnet test  src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
```

- The whole-solution build must be **0 warnings / 0 errors** (WPF + MAUI/Android + MAUI/Windows).
- `dotnet test` must be fully green. Measured baseline: **224/224**.
- Do not hand over a change with a known red test. If a test is genuinely flaky, fix the flakiness first — see troubleshooting entry 10 for how the time-sensitive tests were stabilised.

---

## 7. Documentation discipline

A change to behaviour, a configuration item, or a command must update the documentation **in the same commit**:

| Update | What to touch |
|---|---|
| User-visible behaviour / how to run | the three `README.*` files |
| A topic page | the three language versions of the affected `docs/` page (especially `configuration` when a config item changes) |
| Conventions or measured facts | `AGENTS.md` |
| The plan, the config list, the history | `plans/news-board.plan.md` (§4 configuration list + the matching iteration section + §10 revision log) |
| Anything a user would notice | `CHANGELOG.md` |

**No configuration item may exist in code but not in the documentation.** Likewise, numbers in the documentation must be measured values that exist in this repository — if there is no measurement behind a claim, write "not verified" or delete the claim.

---

## 8. Not yet verified

Stated as-is, not as "should work". Split by platform.

### Android

1. **Auto-start on boot has never succeeded once.** The chain is correct, but MIUI blocks it until the user grants the autostart permission manually; before that the system log ends with `process is not permitted to auto start`.
2. **Long-run stability is only measured for 20 minutes** (no crash, no ANR, memory plateauing between 295 and 302 MB), while the acceptance criterion in the plan is 2 hours.
3. **Immersive full screen (status bar hidden) has not been verified** on a device.
4. **A Release APK has never been installed on a device** — only Debug builds were deployed (Debug 14.97 MB / Release 28.77 MB).
5. **There is no emulator / AVD** on this machine.

### Windows

1. **Real monitor power-off has not been confirmed by hand.** `SC_MONITORPOWER` plus the `MonitorPowerPolicy` guard is implemented, but no automated run has observed an actual backlight change.
2. **Release trimming / AOT is not enabled**, and the WPF publish output is neither self-contained nor single-file.

### Platform-independent

1. **Switching the summary language does not rewrite cached summaries** — you must delete `news.json` or wait for the calendar-day retention to expire.
2. **The "show only items that match a keyword" filter does not exist**; a keyword is currently only a grouping label.
3. **TTS-driven scroll speed** ("scroll to the bottom exactly when the reading ends") is covered by unit tests only, with no audio-level verification.
4. **The round trip "change settings in the UI, kill the process, restart"** has not been automated.

---

Related: [Troubleshooting](troubleshooting.md) · [Architecture](architecture.md) · [Android](android.md) · [Windows hosts](windows.md) · [Documentation index](index.md)
