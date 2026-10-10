# Troubleshooting

**English** | [中文](troubleshooting.zh-CN.md) | [Español](troubleshooting.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

Every entry below is written as **symptom → cause → fix**, and every fix is either a command you can run or a path you can open. Everything marked *measured* was observed on this repository's machine or on the test device, not inferred.

---

## 1. Black screen with an empty `#app` (MAUI host page)

**Symptom** — On the MAUI host the process is alive and the WebView fills the screen, but nothing is rendered: `#app.innerHTML.length === 0`. `logcat` / the console shows `Uncaught (in promise) Error: Blazor has already started.`

**Cause** — The MAUI host page (`src/NewsBoard.Maui/wwwroot/index.html`) started Blazor before the framework did. MAUI's `BlazorWebView` starts Blazor itself and registers the root component declared in `MainPage.xaml` (`Selector="#app"`); a page script that calls `Blazor.start()` first starts Blazor *without* a root component, so the framework's later start is rejected and `#app` stays empty. Measured on the Android device: `pidof` has a value, `uiautomator` shows the WebView filling the screen, and the CDP check returns `#app.innerHTML.length === 0`.

**Fix** — The host page must never call `Blazor.start()` before the framework. It only observes: a timer watches for `#app` having child nodes (then removes the `#boot-hint` element and stops), and falls back to `Blazor.start()` only if `#app` is still empty after 3 seconds. The regression test is `src/NewsBoard.Tests/Sa5MauiHostPageTests.cs`, which asserts that `Blazor.start()` appears after the `setInterval` call (line comments are stripped before the assertion). The WPF host page is a different file and is not affected.

---

## 2. Stuck on the "fetching news" placeholder

**Symptom** — The board page never leaves the placeholder `正在获取新闻…`; in the worst case the process shows no network connections at all.

**Cause** — Four known causes, in the order worth checking:

1. No API Key is configured, so both fetching and summarization fail.
2. The MAUI host never called `InitializeNewsBoardAsync()`, so configuration (including the key) is not loaded and the fetch scheduler never starts.
3. Every feed in the RSS chain is unavailable. Measured on 2026-10-08: Bing `?format=RSS` returns `text/html`, Baidu `newsrss` returns a security-verification page, and Google News RSS times out on this machine.
4. All cached items were removed by the importance threshold — see entry 9.

**Fix** —

- Key: Settings page → **API / fetch** group → paste the key. On Android it is stored in `SecureStorage`.
- MAUI host: `MauiProgram.CreateMauiApp()` must call `InitializeNewsBoardAsync()`; `Display.razor` also awaits it before playback as a safety net (idempotent).
- Feeds: check `RssTemplate` / `RssFallbackTemplates` in the Settings page. Channel feeds measured working here: `https://www.chinanews.com.cn/rss/{scroll-news,china,world}.xml`, `http://www.people.com.cn/rss/{politics,world}.xml`, `https://www.ithome.com/rss/`.
- Threshold: see entry 9.

---

## 3. Android: the Debug APK crashes right after `adb install`

**Symptom** — `adb install` of the Debug `*-Signed.apk` succeeds, but the app aborts on launch:

```
Fatal signal 6 (SIGABRT)
Abort message: 'No assemblies found in /data/user/0/com.newsboard.app/files/.__override__/arm64-v8a
... Assuming this is part of Fast Deployment. Exiting...'
```

**Cause** — Debug builds use .NET Android **Fast Deployment**: the assemblies are not inside the APK, they are pushed to the device into `files/.__override__/<abi>` by the deployment target. A plain `adb install` skips that step.

**Fix** — Deploy with the MSBuild target instead:

```powershell
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

`-t:Run` also works and launches the app. For a self-contained APK you can hand out, build Release. To confirm the assemblies were pushed: `adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l` (measured: 440 files).

---

## 4. Android: auto-start on boot does nothing (MIUI)

**Symptom** — After a reboot the app is not running: `adb shell pidof com.newsboard.app` is empty, there is no resident notification, and `dumpsys activity services com.newsboard.app` shows nothing.

**Cause** — The code path is correct; MIUI blocks it. Measured on a Xiaomi `M2011K2C` (Android 13): `dumpsys activity broadcasts` shows `BOOT_COMPLETED` **is** delivered to the receiver (`Deliver 0 #154: (manifest)` → `name=crc64017365ed8da6996e.BootReceiver`), and the system log immediately records:

```
W BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast
  Intent { act=android.intent.action.BOOT_COMPLETED … }: process is not permitted to auto start
```

`cmd appops get com.newsboard.app 10021` returns `MIUIOP(10021): ignore`.

**Fix** — Allow it by hand: Settings → Apps → Manage apps → NewsBoard → **Autostart** ON; also set the battery policy to *No restrictions*. Then re-test with `adb reboot` and check `pidof com.newsboard.app` plus `dumpsys activity services com.newsboard.app` (expect `NewsBoardForegroundService isForeground=true foregroundId=1001`). Without root you cannot do it from adb: `cmd appops set com.newsboard.app 10021 allow` returns `No operations.`, and `am start -n com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` fails with `Error type 3 … does not exist`.

---

## 5. Android: the blackout phase does not switch the panel off

**Symptom** — During blackout the screen is black, but the backlight is plainly still on.

**Cause** — By design, not a defect. `AndroidScreenController.SetBlackoutAsync(true)` sets the window brightness to `0f` and paints a full-screen black overlay; the physical backlight stays on. Android has no permission-free way for an ordinary app to switch the panel off: `PowerManager.GoToSleep()` needs the signature-level `DEVICE_POWER` permission, and `DevicePolicyManager.LockNow()` needs device-admin rights and also locks the device, so waking would require unlocking. Measured: `mScreenBrightnessOverrideFromWindowManager` goes `NaN` → `0.0` and the screenshot shrinks from 300,867 bytes to 27,787 bytes.

**Fix** — Nothing to fix; treat it as a known limitation. Keep screen on is held by the window flag `KEEP_SCREEN_ON`, which the blackout phase deliberately does not drop — dropping it would let the system switch the panel off after `screen_off_timeout` (measured on this device: 600000 ms). Windows hosts do switch the monitor off, via `SC_MONITORPOWER`.

---

## 6. The same image appears on several items, or an item shows no image

**Symptom** — Measured before the fix: 9 of 9 items had an image but 3 groups shared one template image (5 / 4 / 4 items). After the fix: 5 of 9 items have an image and there are no duplicate groups.

**Cause** — Two separate problems. First, article pages from chinanews.com.cn and people.com.cn carry neither `og:image` nor `twitter:image` and their feeds carry no `enclosure` / `media:*`, so a meta-only resolver falls back to the same site template image. Second, nothing prevented one image URL from being assigned to several items.

**Fix** — `ImageResolver` tries four levels — `og:image` → `twitter:image` → `link[rel=image_src]` → the first image inside the article body (preferring the article container, after a `<p>`, skipping logos, icons, placeholders and template assets more than 60 days older than the article date) — and validates every candidate with a Range request that must really return `image/*` (the chinanews image host is hotlink-protected and otherwise returns HTML). `ImageResolver.DedupeImages` plus `ApplyImagesAsync(..., reservedImageUrls)` keep one image on exactly one item; an item that cannot get an exclusive image is treated as "the article has no image" and shows none.

---

## 7. The keyword label shows a channel name instead of your keyword

**Symptom** — The label at the top of the board page reads a channel name such as `中新网即时新闻` instead of one of your configured keywords.

**Cause** — The default `RssTemplate` is a shared channel feed (it contains no `{q}`), so a single HTTP request returns the whole channel and items arrive tagged with the channel name. `KeywordClassifier.Classify` picks the first configured keyword found in the title, then in the body, and only falls back to the channel name when nothing matches. Measured: with `Keywords=武汉/AI` and the chinanews scroll feed, all 9 items that day were National Day / Japan / A-share stories containing neither keyword, and the model returned an empty `keyword` as well. **This is "no match", not "the classification was lost".**

**Fix** — Nothing is broken. Add the words your feed actually uses, or choose a feed that matches your keywords. A "show only items that match a keyword" filter does not exist yet; a keyword is currently only a grouping label.

---

## 8. Summaries contain Traditional Chinese characters

**Symptom** — Summaries returned by the web-search channel contain Traditional Chinese characters. Measured before the fix: 6 of 9 items had 1–3 Traditional characters.

**Cause** — The prompt did not pin the output language, and that channel sometimes retrieves Hong Kong / Taiwan sources.

**Fix** — `AppConfig.SummaryLanguage` (default `SimplifiedChinese`) is written into both prompts through `SummaryLanguageText.PromptRule()`. Measured after the fix: the same keywords produced 0 Traditional characters across 9 items. Note that switching the language does **not** rewrite cached summaries — delete `news.json` or wait for the calendar-day retention to expire.

---

## 9. The playlist is empty after threshold filtering

**Symptom** — With a high `ImportanceThreshold` the board page keeps showing the fetching placeholder even though `news.json` contains items.

**Cause** — `NewsRepository.GetPlaylist` filters by importance first and orders afterwards. If nothing passes, the list is empty. That is configuration semantics, not a hang.

**Fix** — Lower `ImportanceThreshold` in the Settings page (default 0.7). The code also has a safety net: when the filtered list is empty it appends one random cached item (a copy, so the cache is not modified) and marks that summary with a trailing ` ~`. Measured with the threshold temporarily set to 0.95, the board displayed `…移送检察机关审查起诉。 ~`.

---

## 10. Time-sensitive tests fail intermittently

**Symptom** — `Sa3PlaybackEngineTests` / `PlaybackControlTests` occasionally fail on a busy machine (measured before the fix: 3 of 4 full runs were red), for example `NextAsync_SkipsToFollowingItem`.

**Cause** — Those tests drive a `ScaledClock` at 100× while `WaitUntilAsync` polls every 5 ms of real time, which is 0.5 s of engine time; a busy machine overshoots a checkpoint before the assertion reads it.

**Fix** — Already fixed: a longer first summary (about 40 characters), checkpoints moved inside the window (59 s → 55 s, 365 s → 362 s), tolerance widened from 5 s to 20 s, and a `Task.Delay(250)` before taking the baseline. Measured after the fix: 5 consecutive full runs green at 220/220 each. If you add a new timing assertion, keep the same margins.

---

## 11. Your real data directory was overwritten by a script

**Symptom** — Your keywords or other settings change after running an automated script. This really happened: 4 e2e scripts copied a test configuration over the user's real `config.json`, replacing 6 keywords with `武汉/AI`.

**Cause** — The scripts wrote to the real data directory (`%LOCALAPPDATA%\NewsBoard`), and the `config-backup.json` they left behind was copied *after* the overwrite, so it had the same MD5 as the damaged file and could never roll anything back.

**Fix** — Run any script that touches real fetching with an isolated directory:

```powershell
$env:NEWSBOARD_DATA_DIR = "D:\path\to\isolated-dir"
```

`NewsBoardDataDirectory.Resolve()` is the single resolution point shared by `ConfigStore` and `NewsRepository`, and a non-empty `NEWSBOARD_DATA_DIR` wins. `ConfigStore` additionally keeps the previous version as `config.json.bak` before every overwrite, so a bad write can be rolled back by restoring that file.

---

## 12. MAUI on Windows starts with a black window and exits

**Symptom** — After killing WebView2 processes globally, every MAUI Windows launch is a black window that exits by itself.

**Cause** — A global `Stop-Process -Name msedgewebview2` also kills the WebView2 of a newly created instance and **corrupts its user data directory**.

**Fix** — Delete the broken user data directory and start again:

```powershell
Remove-Item -Recurse -Force "src\NewsBoard.Maui\bin\...\NewsBoard.Maui.exe.WebView2"
```

When cleaning up, kill the app process itself instead of every `msedgewebview2` on the machine.

---

## 13. A `.ps1` script with Chinese text fails to parse

**Symptom** — A verification script containing Chinese characters fails to parse, with strings cut in half.

**Cause** — Windows PowerShell 5.1 reads `.ps1` files as ANSI, so a UTF-8 file without a BOM is decoded incorrectly.

**Fix** — Either keep the script pure ASCII, or write it with a BOM:

```powershell
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true)))
```

The same trap applies to source files: never round-trip a `.cs` file through `Get-Content` / `Set-Content`, because it decodes as ANSI and can leave the file as invalid UTF-8. This happened once to a `.cs` file with Chinese comments.

---

## 14. Every row on the Settings page is squeezed onto one line

**Symptom** — After a CSS change, all Settings page rows collapse into a single line.

**Cause** — The Settings page reused the class name `.card`, which in `app.css` is the board page's news card (`display:flex`).

**Fix** — The Settings page group container must be `.settings-card` and must never use `class="card"`. Regression test: `Sa2SettingsPageTests.Settings_UsesGroupedCardLayout_AndCancelIsBackLink`. This was located by dumping the real HTML through `HtmlRenderer` and reading computed styles with Edge `--headless --dump-dom`.

---

## 15. WPF host crashes on startup: `Microsoft.Windows.SDK.NET`

**Symptom** — The WPF host dies immediately with `FileNotFoundException: Microsoft.Windows.SDK.NET`.

**Cause** — The project used the bare `net10.0-windows` target framework. `BlazorWebView` internally uses `WebView2CompositionControl` (D3D composition) and needs the CsWinRT projection assemblies, which only the versioned target framework provides.

**Fix** — Use `net10.0-windows10.0.19041.0` for the WPF project.

---

Related: [Contributing](contributing.md) · [Android notes](android.md) · [Playback](playback.md) · [News pipeline](news-pipeline.md) · [Documentation index](index.md)
