# Configuration and data

**English** | [中文](configuration.zh-CN.md) | [Español](configuration.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

## Where the files live

| Host | Configuration | News cache | API key |
|---|---|---|---|
| WPF | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat` (DPAPI `CurrentUser` + entropy `NewsBoard.SecretStore.v1`; the file on disk is ciphertext) |
| MAUI (Windows) | the same file as WPF — the two hosts share it completely | the same file | the same file (keys work in both hosts) |
| MAUI (Android) | app-private directory (`LocalApplicationData`, package `com.newsboard.app`) | the same directory | MAUI `SecureStorage` (Android Keystore; not a plaintext file) |

- **Change the data directory**: set the environment variable `NEWSBOARD_DATA_DIR` (any non-empty value wins). `ConfigStore` and `NewsRepository` share this single resolution point. Automated or probe scripts **must** point it at an isolated directory and never at the real one — a probe script once overwrote the user's 6 keywords with 2 (see `plans/news-board.plan.md` §13).
- **Roll back a bad write**: before every overwrite, `ConfigStore` copies the previous version to `config.json.bak`.
- **`ApiKey` never reaches the JSON**: the field carries `[JsonIgnore]` and is injected from the platform secret store under the name `ConfigStore.ApiKeySecretName` (`deepseek-api-key`).
- **Getting the key into Android**: `secret.dat` is encrypted with DPAPI for the current Windows user and is machine-bound, so copying it to a phone achieves nothing. Paste the DeepSeek API key again on the device, in Settings → API / fetching.
- **Auto-start on boot** (Windows) is written to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Settings page

`Settings.razor` is laid out as an iOS-style grouped list (`.settings-card`). Top bar and bottom action bar are `position:fixed` against the real viewport.

| Group | What is in it |
|---|---|
| **Display mode** | playback mode (text only / text + image) and the derived “show news images” toggle — the same switch seen twice |
| **API / fetching** | fetch mode, BaseUrl / Model / search endpoint and max_uses, API key, keyword list, items per keyword, fetch interval, summary length, summary language, AI minimum interval, RSS source and fallbacks, today-only, importance threshold, retention days |
| **Appearance** | background / foreground / accent colours, body font size and weight, line height, padding, keyword label size, alignment, show clock, clock size and font, show keyword / title / source |
| **Playback / speech** | speech on/off, spoken content, language, rate, pitch, lead silence, quiet hours, TTS-unavailable policy, show duration, item hold, scroll speed, blackout duration, blackout cycle, blackout mode, window-end behaviour, image position and screen share, play order, resume across rounds |
| **System / host** | keep screen on, auto-start on boot, WiFi only fetching, Windows borderless fullscreen, Android show status bar |

Bottom bar: **Save / Cancel / Restore defaults**. “Restore defaults” only resets the values being edited on this page — it does not clear a saved API key — and nothing takes effect until you press Save. The auto-start row reads the real platform state through `IAutoStart`, and the group also offers a “blackout for 2 seconds” self-test that does not touch the configuration.

Control ids match the field names in camelCase: `playbackMode`, `showImages`, `summaryLanguage`, `summaryMaxChars`, `importanceThreshold`, `retentionDays`, `clockFontSize`, `clockFontFamily`, `speechEnabled`, `showDuration`, `blackoutDuration`, `winFullscreen` and so on.

## Configuration reference

Field names are the identifiers of `src/NewsBoard.Shared/Services/AppConfig.cs`, and the defaults are that file's defaults. `plans/news-board.plan.md` §4 lists the same items in the same four groups.

### API / fetching

| Field | Default | Meaning |
|---|---|---|
| `FetchMode` | `Auto` | `RssThenSummarize` / `WebSearch` / `Auto`. `Auto` tries the web-search channel first and falls back to RSS |
| `BaseUrl` | `https://api.deepseek.com` | OpenAI-compatible endpoint used by the RSS summarization channel (`POST /chat/completions`) |
| `Model` | `deepseek-chat` | model name, shared by both channels (the backend echoes `deepseek-v4-flash`) |
| `BaseUrlSearch` | `https://api.deepseek.com/anthropic` | Anthropic-compatible endpoint used by the web-search channel (`POST /v1/messages` with the `web_search` tool) |
| `SearchMaxUses` | `3` | how many times the model may call the search tool per keyword |
| `ApiKey` | empty | lives only in the platform secret store, never in the JSON |
| `Keywords` | `武汉`, `中国`, `美国`, `台湾`, `国际`, `AI` | the keyword list; its order is the order used by `PlayOrder.ByKeywordOrder`, and saving trims blanks, duplicates and surrounding spaces |
| `ItemsPerKeyword` | `3` | how many items to take per keyword |
| `FetchIntervalMinutes` | `5` | scheduled fetch period |
| `SummaryMaxChars` | `70` | prompt soft constraint (write 55–70 characters); both channels also run the local `SummaryText.Normalize` check, which cuts at 1.2× (84 characters) back to the last full stop, because the model was measured to overshoot |
| `SummaryLanguage` | `SimplifiedChinese` | `SimplifiedChinese` / `TraditionalChinese` / `English`; written into both channels' prompts as a hard rule |
| `AiMinIntervalMinutes` | `10` | minimum interval between AI calls, so a jittery source does not burn tokens twice |
| `RssTemplate` | `https://www.chinanews.com.cn/rss/scroll-news.xml` | a template containing `{q}` is fetched once per keyword; a template without it is a shared channel feed and is fetched once in total |
| `RssFallbackTemplates` | `http://www.people.com.cn/rss/politics.xml`, `https://www.ithome.com/rss/` | tried in order when the main source fails |
| `OnlyTodayNews` | `true` | drop items published more than 24 hours ago (items without a publish time are kept) |
| `ImportanceThreshold` | `0.7` | only items the model scored at or above this value are played |
| `RetentionDays` | `3` | cache retention, counted in calendar days |

### Appearance

| Field | Default | Meaning |
|---|---|---|
| `BackgroundColor` | `#000000` | board background |
| `ForegroundColor` | `#FFFFFF` | body text |
| `AccentColor` | `#00D4AA` | keyword label and clock |
| `FontSizeSp` | `56` | body font size |
| `FontWeight` | `Bold` | `Normal` / `Bold` |
| `LineHeight` | `1.35` | line height multiple |
| `PagePaddingPx` | `48` | page padding |
| `KeywordLabelFontSizeSp` | `28` | keyword label size |
| `TextAlign` | `Center` | `Left` / `Center` / `Right` |
| `ShowClock` | `true` | show the clock |
| `ClockFontSizeSp` | `32` | clock size; no longer derived from the keyword label size |
| `ClockFontFamily` | empty | empty means follow the system default |
| `ShowKeywordLabel` | `true` | show the keyword label |
| `ShowTitle` | `false` | off by default: the title and the summary usually overlap, so showing both repeats the same words on one screen |
| `ShowSource` | `false` | show the news source |

### Playback / speech

| Field | Default | Meaning |
|---|---|---|
| `PlaybackMode` | `TextOnly` | `TextOnly` / `TextAndImage`. The Settings page surfaces it in the first group (“Display mode”) together with the derived `showImages` toggle |
| `SpeechEnabled` | `true` | with speech off the engine falls back to the fixed item hold |
| `SpeechContent` | `SummaryOnly` | `SummaryOnly` / `TitleAndSummary` |
| `SpeechRate` | `1.0` | 0.5–2.0 |
| `SpeechPitch` | `1.0` | 0.5–2.0 |
| `SpeechLanguage` | `zh-CN` | goes to the system TTS voice package |
| `SpeechLeadSeconds` | `1` | silence after switching to an item, before speech starts |
| `QuietHours` | empty | for example `23:00-07:00`; across midnight works. Inside that range the board displays but does not read aloud; empty means audible all day |
| `TtsUnavailable` | `FallbackHold` | `FallbackHold` (fixed hold) / `SkipItem` |
| `ShowDurationSeconds` | `60` | length of one display window |
| `ItemHoldSeconds` | `12` | only takes effect when speech is off |
| `ScrollSpeedPxPerSecond` | `60` | only takes effect when speech is off; with speech on the scroll speed follows the reading duration |
| `BlackoutDurationSeconds` | `300` | how long the blackout lasts |
| `BlackoutCycleEnabled` | `true` | off means the screen stays on; a manual interrupt still blacks out once |
| `BlackoutMode` | `BrightnessZeroAndBlack` | `BrightnessZeroAndBlack` / `BlackOnly` (keep the backlight) |
| `WindowEndBehavior` | `FinishCurrentItem` | `FinishCurrentItem` / `InterruptImmediately` |
| `ImagePosition` | `TopImageBottomText` | `TopImageBottomText` / `LeftImageRightText` |
| `ImageScreenPercent` | `40` | share of the screen taken by the image in text + image mode |
| `PlayOrder` | `Random` | `ByKeywordOrder` / `ByPublishTime` / `Random` |
| `ResumeAcrossRounds` | `true` | an item cut off by the window continues in the next round |

### System / host

| Field | Default | Meaning |
|---|---|---|
| `KeepScreenOn` | `true` | takes effect on Android; the desktop hosts implement it as a no-op |
| `AutoStart` | `true` | auto-start on boot; see `plans/news-board.plan.md` §3.4 |
| `WifiOnlyFetch` | `false` | off means fetching over mobile data is allowed |
| `WindowsBorderlessFullscreen` | `false` | off means an ordinary window with a title bar; it can be turned on in Settings |
| `AndroidShowStatusBar` | `true` | on means clock and battery stay visible; off means immersive fullscreen |

## Fields that are easy to get wrong

- **`ImportanceThreshold` (0.7)** — items whose `Importance` is below it are filtered out *before* ordering. Items with `Importance == null` (an older cache, or an AI call that failed) are kept, so an upgrade cannot empty the whole store. If the filter leaves the cache empty, `GetPlaylist` adds one random item back as a **copy** with ` ~` appended to its summary: that item did not pass the threshold. The random-order cache key includes the threshold, so changing it re-shuffles immediately.
- **`RetentionDays` (3)** — this is calendar-day bucketing, not a sliding window. `3` keeps today plus the previous two calendar days, and the window starts at local midnight of the earliest kept day (`RetentionWindow.EarliestKeptInstant`). The practical difference: 06:00 and 23:00 see the same bucket boundary, instead of one of them cutting the third day in half.
- **`SummaryLanguage`** — the default is Simplified Chinese, and the same value drives what is displayed and what is read aloud. Switching it does **not** rewrite summaries already in the cache: clear `news.json` or wait for the calendar day to expire. Measured before the prompt rule existed, the web-search channel picked up traditional characters in 6 of 9 articles; after it, 0 of 9.
- **`PlayOrder` (`Random`)** — the order is applied after threshold filtering. `ByKeywordOrder` follows the order of the keyword list, `ByPublishTime` is newest first, `Random` is shuffled once and reused until the cache or the threshold changes.
- **`OnlyTodayNews` vs `RetentionDays`** — the first filters at fetch time (anything older than 24 hours), the second prunes the cache by calendar day. Both are on by default and they do different jobs.
- **Writing to the configuration from a script** — always set `NEWSBOARD_DATA_DIR` to an isolated directory first, and remember that `config.json.bak` holds the previous version if a write goes wrong.
