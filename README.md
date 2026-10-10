# NewsBoard — large-type news board

**English** | [中文](README.zh-CN.md) | [Español](README.es.md)

> NewsBoard — a large-type news board: fetch the day's news, have a model screen it, then rotate it on screen in large type with speech.

## What problem it solves

Fetch the day's news, have a model screen it (summary + importance), then rotate it on screen in **large type with speech**: 60 seconds visible → 300 seconds blackout → repeat. Meant to hang on a wall, a TV box, or an always-on tablet as an information board.

- **Large-type rotation** — 1 item per screen; anything taller than the display area scrolls. 60 seconds visible and 300 seconds blackout by default.
- **Model screening** — every item carries a 70-character summary and an importance of 0–1; anything below the importance threshold (0.7 by default) never enters the playlist.
- **Speech** — the system TTS reads the summary aloud, and the item ends when the reading ends: the reading *is* that item's clock.

1 set of Razor components (`NewsBoard.Shared`) plus 2 hosts:

| Host | Target platform | Purpose |
|---|---|---|
| `NewsBoard.Wpf` | Windows desktop (WPF + WebView2) | Full feature set; also the fastest way to debug the Blazor layer |
| `NewsBoard.Maui` | Android (`net10.0-android`) | Primary target platform, produces the APK |
| Windows head of `NewsBoard.Maui` | Windows desktop (`net10.0-windows10.0.19041.0`) | Runs the Android host code on a PC |

![NewsBoard running on an Android phone](screenshots/01-board-android.png)

*The board on an Android phone: one keyword label, the clock, the summary in large type, and the 5-button control bar in the corner.*

## Quick start

Requirements: **.NET SDK 10** (measured here: 10.0.203 / runtime 10.0.7); on Windows you also need the **WebView2 Runtime** (measured here: 154.0.4258.62). On first use, paste a DeepSeek API Key on the Settings page — without it both fetching and summarization fail.

```powershell
# WPF host (the quickest way to see it working)
dotnet run --project src\NewsBoard.Wpf

# Windows head of MAUI
dotnet run --project src\NewsBoard.Maui -f net10.0-windows10.0.19041.0

# Build the Android APK (output: bin\Debug\net10.0-android\*-Signed.apk)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Build and install to a connected device (debug builds must go through this target)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo

# Whole solution (WPF + MAUI/Android + MAUI/Windows)
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force; . .\scripts\android-env.ps1
dotnet build NewsBoard.slnx -v q --nologo

# Unit tests
dotnet test src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
```

> Android builds **must dot-source** `scripts\android-env.ps1` first, otherwise you get `XA5300 Android SDK not found` (this machine's execution policy is Restricted, hence the per-process bypass). Full Android prerequisites and install details are in [docs/android.md](docs/android.md).

## How it looks

Board page (`Display.razor`): **1** keyword label at the top, a clock, and the summary in large type (with an image in text + image mode); bottom right has 5 icon buttons in a row — **previous / next / open original / interrupt this round / settings**.

- **Tap the screen during a blackout to resume** (`PlaybackEngine.WakeAsync()`: leaves the blackout immediately and restarts the window from the current item). The control bar is hidden during a blackout, so a tap always lands on the black overlay; a touch tap works the same way.
- **Interrupt**: go into the blackout immediately and wait "blackout duration" seconds, then continue with the next round; the blackout still happens even when the cycle is switched off.
- Previous / next wrap around the list; "open original" opens the current article link in the system browser.

| Text + image mode | Blackout | Settings page |
|---|---|---|
| ![text + image mode](screenshots/02-board-text-image.png) | ![blackout](screenshots/03-blackout.png) | ![settings page](screenshots/04-settings.png) |

Settings page (`Settings.razor`) uses an iOS-style grouped list, in this order:

| Group | Contents |
|---|---|
| **Display mode** | Playback mode (text only / text + image) plus the derived "show news images" switch (same source, linked) |
| **API / fetching** | Fetch mode, BaseUrl / Model / web-search endpoint and max_uses, API Key, keyword list, items per keyword, fetch interval, summary length, **summary language**, minimum interval between AI calls, RSS feed and fallbacks, today's news only, **importance threshold**, **retention days** |
| **Appearance** | Background / foreground / accent colors, body font size and weight, line height, page padding, keyword label font size, alignment, show clock, **clock font size / clock font family**, show keyword / title / source |
| **Playback / speech** | Speech switch, what to read, language, rate, pitch, lead-in silence, quiet hours, policy when TTS is unavailable, visible duration, per-item hold, scroll speed, blackout duration, blackout cycle, blackout mode, behavior at window end, image position and screen share, play order, resume across rounds |
| **System / host** | Keep screen on, auto-start on boot, Wi-Fi only fetching, Windows borderless fullscreen, Android status bar |

3 buttons are pinned at the bottom: **save / cancel / restore defaults**; "cancel" is a link back to the board that discards unsaved changes.

> Control ids map almost directly to config fields (camelCase): for example `playbackMode` / `showImages` / `summaryLanguage` / `summaryMaxChars` / `importanceThreshold` / `retentionDays` / `clockFontSize` / `clockFontFamily` / `speechEnabled` / `showDuration` / `blackoutDuration` / `winFullscreen`. The full list is in `plans/news-board.plan.md` and `Settings.razor`.

## Where your data lives

| Version | Config file | News cache | API Key |
|---|---|---|---|
| WPF | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat` (DPAPI CurrentUser encrypted, **never plaintext**) |
| MAUI (Windows) | same as above (**shared with WPF**) | same as above | same as above (the 2 hosts share 1 key) |
| MAUI (Android) | app-private directory (`LocalApplicationData`, package `com.newsboard.app`) | same as left | MAUI `SecureStorage` (Android Keystore encrypted, not a plaintext file) |

- **Moving the data directory**: set the `NEWSBOARD_DATA_DIR` environment variable (any non-empty value wins; `ConfigStore` and `NewsRepository` share this one resolution point). Automation and probe scripts **must** point it at an isolated directory instead of the real one.
- **Rolling back an accidental write**: before every overwrite, `ConfigStore` keeps the previous version as `config.json.bak`.
- **Getting the Key onto Android**: the Windows `secret.dat` is DPAPI-encrypted (current user) and **tied to that machine**, so copying it to the phone is useless. On a new device, paste the DeepSeek API Key again inside the app under Settings → API / fetching (it goes into the Android Keystore-encrypted `SecureStorage`, also never plaintext).
- **Auto-start on boot** (Windows) is written to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- `ApiKey` is **never written into the JSON** (the field carries `[JsonIgnore]` and is injected from the platform secret store).

## How the news gets in

```
Timer (5 minutes by default) / startup
  → channel: RSS channel feed (fallback) or model web search ("auto" by default = web search first, fall back to RSS)
  → URL de-duplication (anything already summarized never calls the AI again)
  → items with a short description get their article body fetched (at most 5 per round)
  → model summarization: summary (70 characters by default) + importance 0–1 + classification keyword
  → text + image mode: 4-level image lookup (og:image → twitter:image → image_src → first image in the article body) + 1 image belongs to exactly 1 item
  → cache (kept for 3 calendar days; expired entries are pruned together with their de-duplication records)
  → playlist: filter by importance threshold (0.7 by default) → play in the configured order (random by default)
```

- When the threshold filters the cache down to nothing, the board **picks 1 item at random** and appends ` ~` to its summary, meaning it did not actually qualify.
- Being stuck on "fetching news…" usually means: no API Key yet, the source site is temporarily unavailable, or the threshold is set too high.
- The summary language defaults to Simplified Chinese; after switching it, **existing cached summaries are not rewritten** (delete `news.json` or wait for the calendar-day expiry to re-fetch).

Full rules (de-duplication and AI cooldown, article body fetching, summary length validation, image exclusivity, calendar-day bucketing) are in [docs/news-pipeline.md](docs/news-pipeline.md).

## Documentation

| Document | What it covers |
|---|---|
| [docs/index.md](docs/index.md) | Documentation index: 8 topic docs, and where to start |
| [docs/architecture.md](docs/architecture.md) | The 3 projects and dependency direction, directory tree, startup-to-playback data flow, frozen contract |
| [docs/configuration.md](docs/configuration.md) | Config file locations, where the Key lives, the 5 Settings groups, the full config field table |
| [docs/news-pipeline.md](docs/news-pipeline.md) | The 2 channels and fallback order, de-duplication and AI cooldown, article body fetching, summaries and importance, images |
| [docs/playback.md](docs/playback.md) | Playback state machine, reading as the item clock, the 5 buttons and gestures, platform differences in blackout, soak results |
| [docs/android.md](docs/android.md) | Android toolchain, build and install commands, on-device acceptance, 3 platform facts |
| [docs/troubleshooting.md](docs/troubleshooting.md) | Common symptom → cause → fix, each with a runnable command |
| [docs/contributing.md](docs/contributing.md) | Environment and commands, file ownership, coding conventions, pre-commit gate, documentation discipline |
| [docs/android-env-report.md](docs/android-env-report.md) | Measured Android toolchain environment report (single language) |

## Development

```powershell
dotnet build src\NewsBoard.Shared\NewsBoard.Shared.csproj -v q --nologo
dotnet test  src\NewsBoard.Tests\NewsBoard.Tests.csproj --nologo
dotnet build NewsBoard.slnx -v q --nologo          # all 3 heads (dot-source android-env.ps1 first)
```

Conventions, the frozen contract and every measured fact (including the traps we hit) are in `AGENTS.md`; [docs/contributing.md](docs/contributing.md) covers the collaboration workflow. Builds must pass before committing, and logic branches need xunit tests.

## Measured verification

| Item | Result |
|---|---|
| Unit tests | `dotnet test` **224/224** passing (13 seconds) |
| Android on device | Xiaomi `M2011K2C` (Redmi K40) / Android 13 / arm64-v8a: 6 keywords fetched **16 items**; tapping "interrupt" drove the window brightness from `NaN` to `0.0` within 1.2 seconds with the screenshot going 300 KB → 27 KB; TTS read **7.4–14.6 seconds** per item with about 1 second between items; a 20-minute soak showed no FATAL/ANR, with memory rising 230 MB → 295–302 MB and then plateauing; cold start rendered the board in +5.2 seconds |
| 2 Windows hosts | WPF: default `1600x1000@(160,10)` with a title bar → F11 `1920x1080@(0,0)` edge to edge → Esc restores; MAUI Windows head: process alive, 12 `msedgewebview2` child processes, real news rendered; text + image mode with `ImageScreenPercent=40` verified as image on top, text below |
| RSS feed availability | chinanews `scroll-news` / `china` / `world`, people.com.cn `politics` / `world` and ithome work; Bing `?format=RSS` returns HTML, Baidu `newsrss` returns a security-check page and Google News RSS times out (measured 2026-10-08) |

## Known limitations

- **Android is installed and individually walked through** (2026-10-08, Xiaomi `M2011K2C` / Redmi K40 / Android 13 / arm64-v8a): startup rendering, 6 keywords fetching 16 items, keyword label + clock + large-type summary + control bar, config and cache landing in the app-private directory; tapping "interrupt" drives the window brightness to 0 and blacks the screen within 1.2 seconds, and tapping the screen restores it; `KEEP_SCREEN_ON` keeps the screen on; Chinese TTS really reads item by item (5 consecutive `CONTENT_TYPE_SPEECH` AudioTracks in `dumpsys audio`, 7.4–14.6 seconds each, about 1 second between items).
- **The Android "blackout" is not a physical screen-off**: it only sets the **window brightness to 0 and paints the screen black** — the backlight stays on. A normal Android app has no permission-free way to switch the panel off (`GoToSleep()` is a signature permission; `LockNow()` needs device-admin rights, locks the screen, and then requires unlocking to wake). Only the Windows build really turns the display off (`SC_MONITORPOWER`).
- **Auto-start on boot on Android needs vendor approval**: the code path is correct (`BootReceiver` really does receive `BOOT_COMPLETED`) but MIUI's autostart manager blocks it on this device (system log `process is not permitted to auto start`, `MIUIOP(10021): ignore`). **To allow it manually**: Settings → Apps → Manage apps → NewsBoard → **Autostart** (on); also set the battery saver policy to **no restrictions**. After that, a reboot brings up the persistent notification (it does not use `StartActivity`; tap the notification to open the board).
- **TTS quality on Android depends on the phone's built-in speech engine** (here `com.xiaomi.mibrain.speech`); timbre and naturalness are yours to judge — the app only hands the summary to it.
- **The web-search channel itself returns no article body, but the body still gets fetched**: that channel takes the summary straight from the model, yet article-body fetching applies to the 2 channels alike (it triggers when `Description` is under 300 characters), so `RawContent` on web-search items can be non-null. Measured on this machine's cache: **313 items** (all from web search), **92** of them with a non-null `RawContent`, 109–2000 characters long; items whose fetch failed stay null.
- **Real screen-off on Windows is not human-confirmed**: the implementation uses `SC_MONITORPOWER` with a guard (it only switches the display off when this app is foreground or borderless-fullscreen, while switching it back on is unrestricted), but automation never triggered an actual backlight change.
- **Switching the summary language does not rewrite cached summaries** (see "How the news gets in" above).
- A filter switch for **"only show news that match a keyword"** is **not implemented**; keywords are currently used only as grouping labels and for display.

## License

[MIT](LICENSE)
