# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## Unreleased

Documentation reorganised into the shape of a public repository: three languages (English / Chinese /
Spanish), a topic-based `docs/` set, a changelog and a licence.

### Added

- `docs/` topic set, **each document in English / Chinese / Spanish**: documentation index,
  architecture, configuration, news pipeline, playback, Android, Windows, troubleshooting,
  contributing.
- `CHANGELOG.md` and `LICENSE` (MIT).
- `plans/` — the implementation plan moved out of the repository root.
- `screenshots/` — four curated screenshots used by the README.

### Changed

- `README.md` is now the **English** edition; `README.zh-CN.md` and `README.es.md` carry the Chinese
  and Spanish editions. Every document starts with a one-line language switcher.
- Documentation rewritten around a **cross-platform** narrative: Android and Windows are presented as
  peers instead of Android being the primary target, and platform-specific capabilities (blackout,
  keep-awake, speech, autostart, config and key locations) are now compared side by side.
- New `docs/windows.*` documents the two Windows hosts (`NewsBoard.Wpf` and the Windows head of
  `NewsBoard.Maui`) and their differences.

## 1.0.0 — 2026-10-08

First working version: news comes in, a model filters and summarises it, and the board plays it as
large type with speech — on three hosts (WPF, MAUI/Windows, MAUI/Android).

### Added

| Item | What it does |
|---|---|
| `NewsBoard.Shared` | Razor component library holding **all** logic and UI; zero platform dependencies, so it is testable without a device. |
| `NewsBoard.Wpf` | Windows host (WPF + WebView2); full feature set and the fastest way to debug the Blazor layer. |
| `NewsBoard.Maui` | One project with two heads: the Android host (`net10.0-android`) and the Windows head (`net10.0-windows10.0.19041.0`). |
| News pipeline | RSS channel feeds with a fallback chain, plus a model **web-search** channel; `FetchMode.Auto` tries web search first and falls back to RSS. URL-hash dedup means the second round makes **0 AI calls**. |
| Summarisation | One batch call returns a ≤70-character summary, an importance score (0–1) and a keyword per item; a local `SummaryText.Normalize` pass enforces the length ceiling. |
| Article body fetch | `ArticleContentFetcher` fetches the original page for short descriptions (5 per round, 2 concurrent) and writes the body back, so both the summary input and the cached `RawContent` use real article text. |
| Images (text + image mode) | Four-level resolver (og:image → twitter:image → `link[rel=image_src]` → first body image), each candidate verified to really return `image/*`; **one image belongs to one item only**, and an item with no usable image is rendered as text rather than a shared template graphic. |
| Playback engine | `SHOW → BLACKOUT → SHOW` state machine driven entirely by an injected clock; **speech completion is the item clock**; a first-frame gate stops speech from starting before the board is rendered; carry-over across rounds; previous/next wrap around the list. |
| Blackout and wake | Tapping the screen during blackout wakes immediately and restarts the current item's window. Windows turns the monitor off for real (`SC_MONITORPOWER`, guarded by `MonitorPowerPolicy`); Android sets the window brightness to 0 and paints black. |
| Settings page | Five iOS-style groups (display mode, API/collection, appearance, playback/speech, system/host); the display-mode group is the first thing on screen. |
| Retention | Playlist cache pruned by **calendar day** buckets (`RetentionDays`, default 3 = today plus the two previous days), not by a sliding 72-hour window. |
| Below-threshold fallback | When the importance threshold filters the cache down to nothing, one random item is played with a ` ~` suffix instead of showing an empty board forever. |
| Data isolation | `NEWSBOARD_DATA_DIR` redirects the data directory (used by both config and cache); `ConfigStore` keeps the previous version as `config.json.bak` before every overwrite. |
| Auto-start | Windows: `HKCU\…\CurrentVersion\Run`. Android: `BootReceiver` + a foreground service with a persistent notification (never `StartActivity`, which Android 10+ blocks from the background). |

### Changed

Contract revisions made during implementation, all driven by measured evidence:

- **R1** — the default fetch mode became `Auto` and the RSS template moved to channel feeds that were
  actually reachable; a fallback chain was added.
- **R2** — the WPF target framework had to carry a versioned platform moniker
  (`net10.0-windows10.0.19041.0`); the bare `net10.0-windows` crashed on start.
- **R3 / R3b** — summary length default went `50 → 120 → 70` characters, and cards stopped showing the
  title by default (it duplicated the summary).
- **R4** — added summary language (Simplified Chinese / Traditional Chinese / English) and
  clock font size / family.
- **R5** — added `Importance`, `RawContent`, the importance threshold and the retention window;
  default play order became `Random`.
- **R6** — calendar-day retention, article body fetch, keyword classification, per-item image
  ownership, empty-playlist fallback, and the display-mode group moved to the top of Settings.

### Fixed

- **The MAUI host never initialised**: configuration (including the API key) was never loaded and the
  fetch scheduler never started, so the board sat on "fetching news…" with zero network activity.
- **Android showed a black screen**: the MAUI host page called `Blazor.start()` before the framework
  did, so the framework's start-with-root-component was rejected. The page now observes `#app` and only
  falls back after 3 s (regression test `Sa5MauiHostPageTests`).
- The settings page could not scroll and reused the playback card's `.card` class, which collapsed
  every row onto one line.
- `ImageResolver` returned the same site template graphic for several items; candidates are now
  verified individually and images are deduplicated per item.
- Four time-sensitive playback tests failed intermittently (100× scaled clock plus 5 ms polling);
  checkpoints moved inside the window, tolerances widened, and five consecutive full runs are green.

### Verified

| Item | Reading |
|---|---|
| Build | `dotnet build NewsBoard.slnx` (WPF + MAUI/Android + MAUI/Windows) ⇒ **0 warnings, 0 errors** |
| Tests | `dotnet test` ⇒ **224 / 224** |
| Android, on device | Xiaomi `M2011K2C` (Redmi K40), Android 13, arm64-v8a: installs and renders; **16 items** fetched across 6 keywords; tapping *interrupt* drives the window brightness `NaN → 0.0` within 1.2 s (screenshot 300 KB → 27 KB on a black frame); the screen stayed awake past the 10-minute system timeout; Chinese TTS produced five consecutive `CONTENT_TYPE_SPEECH` audio tracks of **7.4–14.6 s** with ~1 s gaps |
| Soak (20 min) | Same PID throughout, no FATAL/ANR, memory plateaued at 295–302 MB PSS after an initial climb; playback kept advancing |
| Models | RSS summarisation 53–66 characters, web-search summarisation 72–84 characters, importance spread across the full range after balancing |

### Known limitations at this version

Detailed, and kept current, in the [README](README.md#known-limitations):
Android blackout does not switch the backlight off; MIUI blocks auto-start until the user allows it in
"Autostart"; the web-search channel cannot return article bodies; Windows monitor-off has not been
confirmed by a human watching the backlight.
