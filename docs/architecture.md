# Architecture

**English** | [中文](architecture.zh-CN.md) | [Español](architecture.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

## One component set, three hosts

NewsBoard is a **cross-platform** large-type news board: **one set of Razor components (`NewsBoard.Shared`, zero platform dependencies) + several hosts**. **Two platforms are supported today — Android and Windows — through three runnable hosts**, and the three are peers.

| Platform | Host | Stack / TFM | Status |
|---|---|---|---|
| Android | `NewsBoard.Maui` | .NET MAUI + `BlazorWebView`, `net10.0-android` | Supported; measured by installing on a real device (Android 13 / arm64-v8a), and the project that produces the APK |
| Windows | `NewsBoard.Wpf` | WPF + WebView2, `net10.0-windows10.0.19041.0` | Supported; measured running on this machine, and `dotnet publish -c Release` produces 45 files / 28.14 MB |
| Windows | `NewsBoard.Maui` Windows head | .NET MAUI + `BlazorWebView`, same TFM | Supported; runs unpackaged with `WindowsPackageType=None`, needs the Windows App Runtime (1.6 / 1.7 / 1.8 on this machine) |

Exactly one thing sets Android apart: it is the only platform that has been verified by installing on a physical device.

## The three projects

| Project | Target framework | What it is |
|---|---|---|
| `NewsBoard.Shared` | `net10.0` | Razor class library (RCL): **all** UI components, `wwwroot` (css/js), the configuration model, fetching, summarization, the playback engine and the platform abstraction interfaces. **Zero platform dependencies.** |
| `NewsBoard.Maui` | `net10.0-android`; `net10.0-windows10.0.19041.0` | Two hosts in one project: the **Android host** (MAUI Blazor Hybrid, and the project that produces the APK) and the **MAUI Windows head**, which runs the same Blazor layer as an unpackaged desktop app. Supplies both the Android and the Windows platform implementations. |
| `NewsBoard.Wpf` | `net10.0-windows10.0.19041.0` | The **Windows desktop host**: WPF plus `Microsoft.AspNetCore.Components.WebView.Wpf` (WebView2), hosting the same Razor components. Supplies the Windows platform implementations. |

The dependency direction is single: `Maui → Shared`, `Wpf → Shared`. **Shared never depends back on a host.**

`NewsBoard.Wpf` must use the **versioned** platform target framework. A bare `net10.0-windows` starts and immediately crashes with `FileNotFoundException: Microsoft.Windows.SDK.NET`, because `BlazorWebView` internally uses `WebView2CompositionControl` (D3D composition) and needs the CsWinRT projection assemblies that only the versioned TFM provides.

Why it is split this way:

- **Platform capabilities live entirely in the host projects.** Screen control, TTS, secret storage, auto-start and link opening are injected through interfaces defined in Shared; Shared contains no `#if ANDROID` and no `#if WINDOWS`.
- **Shared holds only pure .NET logic** — fetching, summarization, dedup, caching, the playback state machine and every Razor component. One copy serves all three heads, so the same board runs on Android and on Windows, and the Blazor layer can be debugged quickly on a PC through the WPF host.
- **Blazor only renders.** Components receive a `NewsItem` plus display options and output HTML/CSS.

## Source tree

Every file below exists in the repository (build output under `bin/` and `obj/` is omitted).

```
NewsBoard/
├── NewsBoard.slnx
├── README.md / README.zh-CN.md / README.es.md
├── CHANGELOG.md
├── LICENSE                          (MIT)
├── AGENTS.md                        (conventions for AI collaborators)
├── plans/
│   └── news-board.plan.md           (implementation plan and history)
├── scripts/
│   └── android-env.ps1              (must be dot-sourced before any Android build)
├── docs/                            (this directory)
└── src/
    ├── NewsBoard.Shared/            (net10.0, Razor class library, zero platform dependencies)
    │   ├── NewsBoard.Shared.csproj
    │   ├── _Imports.razor
    │   ├── Models/
    │   │   └── NewsItem.cs                      (NewsItem / RawNewsItem)
    │   ├── Components/
    │   │   ├── Routes.razor                     (BlazorWebView root component)
    │   │   ├── Pages/Display.razor              (board page — the only persistent page)
    │   │   ├── Pages/Settings.razor             (Settings page)
    │   │   └── Shared/NewsCard.razor            (renders one item: text, or text + image)
    │   ├── Services/
    │   │   ├── AppConfig.cs                     (every configuration field + Clone/NormalizeKeywords)
    │   │   ├── ArticleContentFetcher.cs         (article body: 5 s timeout, de-noising, recommendation-block cut)
    │   │   ├── ConfigStore.cs                   (JSON persistence, .bak before overwrite, key in platform secret storage)
    │   │   ├── ImageResolver.cs                 (four-level image lookup, Range check, one image per item)
    │   │   ├── ImportanceScoring.cs             (tolerant parsing, rank balance when a whole batch degenerates)
    │   │   ├── KeywordClassifier.cs             (classify by configured keyword, fall back to the channel name)
    │   │   ├── MonitorPowerPolicy.cs            (guard rails and idempotence for the Windows real monitor-off)
    │   │   ├── NewsBoardDataDirectory.cs        (single resolution point for the data directory)
    │   │   ├── NewsFetchCoordinator.cs          (one fetch round: dedup → summarize → body → images)
    │   │   ├── NewsFetchScheduler.cs            (fetch on start, then loop by interval)
    │   │   ├── NewsRepository.cs                (cache, URL dedup, threshold filter, calendar-day pruning)
    │   │   ├── NewsSummarizer.cs                (summarization + importance + keyword, strict JSON parsing, retry)
    │   │   ├── PlaybackEngine.cs                (playback state machine, WakeAsync, first-frame gate)
    │   │   ├── RetentionWindow.cs               (calendar-day retention window, pure logic)
    │   │   ├── RssNewsSource.cs                 (RSS channel feed + fallback chain)
    │   │   ├── ServiceCollectionExtensions.cs   (AddNewsBoardCore / InitializeNewsBoardAsync)
    │   │   ├── SummaryText.cs                   (local re-validation of the summary: cut at 1.2×, at a sentence end)
    │   │   ├── WebSearchNewsSource.cs           (Anthropic-compatible endpoint + web_search tool)
    │   │   └── Abstractions/
    │   │       ├── IAutoStart.cs
    │   │       ├── IConfigProvider.cs
    │   │       ├── ILinkOpener.cs
    │   │       ├── INewsRepository.cs
    │   │       ├── INewsSource.cs               (also defines IClock and SystemClock)
    │   │       ├── IScreenController.cs
    │   │       ├── ISecretStore.cs
    │   │       └── ISpeechService.cs
    │   └── wwwroot/
    │       ├── css/app.css                      (large-type layout, scroll animation, transitions)
    │       ├── css/settings.css                 (Settings page, grouped card layout)
    │       └── js/measure.js                    (overflow measurement + scroll progress callback)
    ├── NewsBoard.Maui/              (net10.0-android; net10.0-windows10.0.19041.0)
    │   ├── NewsBoard.Maui.csproj
    │   ├── MauiProgram.cs                       (DI: platform implementations per TFM, then AddNewsBoardCore)
    │   ├── NewsBoardBoot.cs                     (loads configuration and fetching at start-up)
    │   ├── App.xaml / App.xaml.cs
    │   ├── MainPage.xaml / MainPage.xaml.cs     (BlazorWebView host)
    │   ├── wwwroot/index.html
    │   ├── Resources/
    │   │   ├── AppIcon/appicon.svg, appiconfg.svg
    │   │   └── Splash/splash.svg
    │   ├── Platforms/Android/
    │   │   ├── AndroidManifest.xml              (INTERNET, WAKE_LOCK, POST_NOTIFICATIONS, RECEIVE_BOOT_COMPLETED, FGS)
    │   │   ├── MainActivity.cs / MainApplication.cs
    │   │   ├── AndroidScreenController.cs       (keep screen on + window brightness 0 + restore)
    │   │   ├── AndroidSpeechService.cs          (native Android.Speech.Tts + UtteranceProgressListener)
    │   │   ├── MauiSecretStore.cs               (SecureStorage)
    │   │   ├── AndroidAutoStart.cs, AutoStartHelper.cs, BootReceiver.cs, NewsBoardForegroundService.cs
    │   │   └── AndroidLinkOpener.cs
    │   └── Platforms/Windows/                   (Windows head: the same Razor components on the desktop)
    │       ├── App.xaml / App.xaml.cs
    │       ├── app.manifest
    │       ├── WindowsScreenController.cs       (SC_MONITORPOWER + foreground/fullscreen guard rails)
    │       ├── WindowsSpeechService.cs
    │       ├── WindowsSecretStore.cs
    │       ├── WindowsAutoStart.cs
    │       └── WindowsLinkOpener.cs
    ├── NewsBoard.Wpf/               (net10.0-windows10.0.19041.0)
    │   ├── NewsBoard.Wpf.csproj
    │   ├── App.xaml / App.xaml.cs
    │   ├── MainWindow.xaml / MainWindow.xaml.cs (BlazorWebView hosting Routes; F11 / Esc for borderless fullscreen)
    │   ├── wwwroot/index.html                   (explicit Blazor.start() + visible boot hint)
    │   └── Services/
    │       ├── WpfScreenController.cs
    │       ├── WpfSpeechService.cs              (System.Speech.Synthesis.SpeechSynthesizer)
    │       ├── WpfSecretStore.cs                (DPAPI ProtectedData)
    │       ├── WpfAutoStart.cs                  (HKCU Run key)
    │       └── WpfLinkOpener.cs
    └── NewsBoard.Tests/             (net10.0, xunit — 24 .cs files: Sa1–Sa10 suites by area, plus focused suites and shared doubles/collection fixtures)
        └── NewsBoard.Tests.csproj
```

## One start → fetch → playback flow

1. **Host start.** Each host registers its own platform implementations: the WPF host in `App.xaml.cs`, the MAUI host in `MauiProgram.cs` (the Android or the Windows set, selected by target framework). All of them then call `AddNewsBoardCore()`. `MainWindow` (WPF) and `MainPage` (MAUI, both heads) host a `BlazorWebView` whose root component is `Components/Routes.razor`.
2. **Assembly.** `AddNewsBoardCore()` registers one `HttpClient` with `Timeout.InfiniteTimeSpan` — the three network implementations each do their own timing (15 s / 90 s / 3 s), so an `HttpClient` timeout would cut them short. It also registers `IClock → SystemClock`, `NewsRepository` (exposed as `INewsRepository`), `ConfigStore` (exposed as `IConfigProvider`), `NewsSummarizer`, `ImageResolver`, `ArticleContentFetcher`, both `INewsSource` implementations, `NewsFetchCoordinator`, `NewsFetchScheduler` and `PlaybackEngine`.
3. **Initialization.** `InitializeNewsBoardAsync()` loads `config.json` (including the API key read from `ISecretStore`), loads `news.json`, then starts `NewsFetchScheduler` — one fetch immediately, then a loop at the "fetch interval" (default 5 minutes). **If this call is not awaited, the playlist stays empty and the board shows "正在获取新闻…" forever.**
4. **Fetch.** Scheduler → `NewsFetchCoordinator` → `INewsSource` (the RSS channel feed, or the model's web-search channel; `Auto` tries web search first and falls back to RSS) → URL dedup → article-body backfill → `NewsSummarizer` (summary + importance + keyword) → `ImageResolver` (text + image mode only) → `NewsRepository` cache.
5. **Playback.** `Display.razor` awaits `InitializeNewsBoardAsync()` once more, arms the first-frame gate, then calls `PlaybackEngine.StartAsync()`, which sets keep-screen-on, applies the fullscreen preference, leaves blackout, probes the speech engine, reloads the playlist and starts the SHOW → BLACKOUT → SHOW loop.
6. **Render.** `NewsCard.razor` renders the current item; `wwwroot/js/measure.js` measures overflow and drives the scroll; `ISpeechService` reads the summary aloud and its completion callback ends that item (decision D5 — speech is the item clock). `IScreenController` owns keep-screen-on and blackout.

## How platform capabilities are injected

`NewsBoard.Shared.Services.Abstractions` defines the interfaces; each host registers its own implementations in dependency injection **before** calling `AddNewsBoardCore()`. The three hosts sit side by side below — Android first, then the two Windows heads — and the last column of the header names the folder each implementation lives in.

| Interface | Android (`NewsBoard.Maui`, `Platforms/Android/`) | Windows (`NewsBoard.Wpf`, `Services/`) | Windows (`NewsBoard.Maui`, `Platforms/Windows/`) |
|---|---|---|---|
| `IScreenController` | `AndroidScreenController` | `WpfScreenController` | `WindowsScreenController` |
| `ISpeechService` | `AndroidSpeechService` | `WpfSpeechService` | `WindowsSpeechService` |
| `ISecretStore` | `MauiSecretStore` | `WpfSecretStore` | `WindowsSecretStore` |
| `IAutoStart` | `AndroidAutoStart` | `WpfAutoStart` | `WindowsAutoStart` |
| `ILinkOpener` | `AndroidLinkOpener` | `WpfLinkOpener` | `WindowsLinkOpener` |
| `INewsSource`, `INewsRepository`, `IConfigProvider`, `IClock` | registered once by `AddNewsBoardCore()` in Shared — no platform implementation | | |

`NewsBoard.Maui` selects the Android or the Windows set by target framework inside `MauiProgram.cs`; that conditional compilation stays in the host project. Shared itself must stay free of `#if ANDROID` and `#if WINDOWS`.

## Platform differences

The table below is the whole platform difference surface; everything not listed behaves identically on all three hosts, because it lives in Shared.

| Capability | Android | Windows (`NewsBoard.Wpf`) | Windows (`NewsBoard.Maui` Windows head) |
|---|---|---|---|
| Blackout | window brightness 0 + a full-black overlay, **the physical backlight stays on** | **really turns the monitor off** (`WM_SYSCOMMAND` / `SC_MONITORPOWER`, 2 = off / -1 = on) | same as WPF; this head does no borderless fullscreen, so the guard rail degrades to "foreground only" |
| Monitor-off guard rail | not applicable | turns the monitor off only when "the app window is foreground **or** already borderless fullscreen"; **turning it back on is not gated** | same, but the fullscreen condition is always false |
| Keep screen on | carried by the `KEEP_SCREEN_ON` window flag, **not dropped during blackout** | empty implementation (no such need on the desktop) | empty implementation |
| Fullscreen | immersive: hides the system status bar (option `AndroidShowStatusBar`) | borderless fullscreen (F11 to enter / Esc to exit; measured 1600x1000@(160,10) → 1920x1080@(0,0)) | not done (`SetFullscreenAsync` is an empty implementation) |
| Speech synthesis | the device's own TTS engine (measured `com.xiaomi.mibrain.speech`) | `System.Speech` (measured: `Microsoft Huihui Desktop` zh-CN; the `SpeakCompleted` callback is reliable) | `System.Speech` (same as WPF) |
| Auto-start | `BootReceiver` + a foreground service + a resident notification; a vendor auto-start manager (MIUI, for example) may block it | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | same as WPF |
| Config / cache directory | the app's private directory (package `com.newsboard.app`) | `%LOCALAPPDATA%\NewsBoard\` (`config.json` / `news.json`) | same as WPF, **fully shared with WPF** |
| API key | MAUI `SecureStorage` (Android Keystore encryption, never a plaintext file) | `%APPDATA%\NewsBoard\secret.dat` (DPAPI CurrentUser + entropy `NewsBoard.SecretStore.v1`) | same as WPF (the two hosts share the key) |
| Opening the source article | the system browser (Intent) | the default browser | the default browser |
| Data directory override | the `NEWSBOARD_DATA_DIR` environment variable (common to all three hosts) | same | same |

## Frozen contracts

These files and signatures must not be changed (adding a `partial` member inside your own file, or privatising an implementation detail, is fine):

- `NewsBoard.Shared.Models.NewsItem` and `RawNewsItem` — including `NewsItem.Importance` (`double?`, 0–1, `null` = not scored) and `NewsItem.RawContent` (the fetched raw content), plus `RawNewsItem.Importance` and `RawNewsItem.Description`.
- `NewsBoard.Shared.Services.AppConfig` and all of its enums — including `SummaryLanguage` (`SummaryLanguageOption`: SimplifiedChinese by default / TraditionalChinese / English, with `SummaryLanguageText.PromptRule()` for the prompt constraint and `DisplayName()` for UI text), `ClockFontSizeSp` (default 32), `ClockFontFamily` (default `""`), `ImportanceThreshold` (default 0.7) and `RetentionDays` (default 3); `PlayOrder` defaults to **Random**.
- `NewsBoard.Shared.Services.Abstractions`: `IScreenController`, `ISpeechService`, `ISecretStore`, `IAutoStart`, `ILinkOpener`, `INewsSource`, `IConfigProvider`, `INewsRepository`, `IClock` (with `SystemClock`).
- The manual control API of `PlaybackEngine`: `NextAsync()` / `PreviousAsync()` / `InterruptAsync()` (interrupt this round → blackout for "blackout duration" seconds → next round; it blacks out once even when the blackout cycle is disabled) with wrap-around in both directions, and `WakeAsync()` (during blackout, one tap returns to the board and restarts the window from the current item; while showing it has no side effect — it neither changes the item nor restarts the window).
- `NewsBoard.Shared.Services.ServiceCollectionExtensions.AddNewsBoardCore()` and `InitializeNewsBoardAsync()` — the single assembly entry point for hosts.
- The `using` set of `_Imports.razor`, and `Components/Routes.razor` as the `BlazorWebView.RootComponent`.
