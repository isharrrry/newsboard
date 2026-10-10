# Android

**English** | [中文](android.zh-CN.md) | [Español](android.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

This page is **one of the platform pages**; for the cross-platform overview see the [README](../README.md) and [Architecture](architecture.md), and the Windows side in [Windows hosts](windows.md).

The Android host is `src/NewsBoard.Maui` (TFM `net10.0-android`), rendering the same Blazor UI as the Windows hosts, on a phone that sits on a desk and reads the news out loud. It is the only mobile platform that has been measured on real hardware so far.

## Toolchain and prerequisites

The toolchain lives in `D:\Android` on this machine, and `scripts\android-env.ps1` points a shell at it.

| Piece | Path / version |
|---|---|
| .NET SDK | `10.0.203`, runtime `10.0.7` |
| workload | `maui-android` `10.0.20/10.0.100` (workload version `10.0.204.1`) |
| JDK 17 | `D:\Android\jdk-17` (OpenJDK 17.0.20.1), 0.29 GB |
| Android SDK | `D:\Android\Sdk` — `platforms;android-36`, `build-tools;36.0.0`, `platform-tools`, `cmdline-tools;latest`, 0.40 GB |
| NDK | `D:\Android\Sdk\ndk\27.2.12479018` (r27c), 2.20 GB |

**Every new terminal must source the script first** — the execution policy on this machine is `Restricted`, so unlock the current process before dot-sourcing it:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
. .\scripts\android-env.ps1
```

The script sets `DOTNET_ROOT`, `JAVA_HOME`, `ANDROID_HOME` / `ANDROID_SDK_ROOT` / `AndroidSdkDirectory`, `JavaSdkDirectory`, `ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT` and prepends `dotnet`, `jdk-17\bin`, `platform-tools`, `cmdline-tools\latest\bin` and `emulator` to `PATH`. It is idempotent, affects only the current process, and only warns when a path is missing. It must stay **UTF-8 with BOM** (without BOM the Chinese comments are mojibake after dot-sourcing). Skipping it fails the build with `error XA5300 找不到 Android SDK 目录`.

The full environment inventory — what was installed, where, how much space, and what was never tested — is in [android-env-report.md](android-env-report.md).

## Build

```powershell
# Debug APK -> src\NewsBoard.Maui\bin\Debug\net10.0-android\*-Signed.apk
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Release, self-contained and distributable
dotnet publish src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -c Release

# the whole solution (WPF + MAUI/Android + MAUI/Windows)
dotnet build NewsBoard.slnx -v q --nologo
```

## Deploy to a device

**A Debug build cannot be installed with `adb install` alone.** Debug builds use .NET Android's **Fast Deployment**: the assemblies are not inside the APK, the deployment target pushes them separately to `files/.__override__/<abi>` on the device. Installing only the `*-Signed.apk` crashes on start:

```
F/libc: Fatal signal 6 (SIGABRT), code -1 (SI_QUEUE) in tid 12152 (m.newsboard.app)
Abort message: 'No assemblies found in '/data/user/0/com.newsboard.app/files/.__override__/arm64-v8a'
                or '<unavailable>'. Assuming this is part of Fast Deployment. Exiting...'
backtrace: #00 libc.so (abort)  ←  #01 libmonodroid.so (abort_application)
```

Use the official deployment target instead (`-t:Run` also works and launches the app afterwards):

```powershell
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

After installing, the pushed assemblies can be counted with `adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l` (440 files in this run). For a package that can be installed standalone, build Release.

## Measured acceptance on device

Device: Xiaomi `M2011K2C` (Redmi K40), Android 13 / SDK 33 / arm64-v8a, serial `<device-serial>`.

| Item | Result |
|---|---|
| Install | `com.newsboard.app` v1.0, minSdk 24 / targetSdk 36 |
| Start | process stays alive, no FATAL / abort; the first start asks for the notification permission (a MIUI dialog), and the board appears after granting it |
| Rendering | keyword label + clock + large summary text + bottom control bar (⏮ ⏭ 🔗 ⏸ ⚙) |
| Fetching | **16 items**, all six keywords hit: China 2 / Wuhan 3 / US 3 / Taiwan 3 / International 2 / AI 3; with summaries and importance (example 0.81), channel "web search" |
| Data | `files/NewsBoard/config.json` (1817 B) + `files/NewsBoard/news.json` (22464 B), both in the app-private directory |

Cold start measured with `am start`: at +2.8 s the screen still shows the boot hint (39 KB screenshot), at **+5.2 s the board is rendered** (306 KB).

## Platform fact — blackout is not screen-off

The Android implementation of `IScreenController` sets the window's `WindowManager.LayoutParams.ScreenBrightness` to `0f` and paints a full-black overlay. It does **not** turn the panel off.

| Moment | Action | `mScreenBrightnessOverrideFromWindowManager` | Screenshot bytes |
|---|---|---|---|
| Showing | — | `NaN` (no override = system brightness) | — |
| Tap "interrupt" (4th control button) | `input tap 919 2287` | **`0.0`** | **27,787** (all black) |
| Tap the screen centre to wake | `input tap 540 1200` | **`NaN`** | **300,867** (board restored) |

The transition takes effect within **1.2 s** in both directions; during the blackout `dumpsys audio` shows the speech has already `stopped`.

**Keep screen on** is carried by the window flag: `dumpsys window` shows `fl=… KEEP_SCREEN_ON …` and `mLastWakeLockHoldingWindow=…MainActivity`. The controller deliberately **keeps** that flag during the blackout, so the system does not switch the panel off after `screen_off_timeout` (600000 ms = 10 minutes on this device) — the 20-minute soak kept `mWakefulness=Awake / mScreenOnFully=true` throughout.

**Limitation**: because the backlight stays on, the blackout is not a real screen-off. Android offers nothing permission-free that switches the panel off — `PowerManager.GoToSleep()` is a `DEVICE_POWER` signature permission, and `DevicePolicyManager.LockNow()` needs device-admin rights **and locks the screen**, which conflicts with "tap the screen to wake". This is the closest an unprivileged app can get; real screen-off would need the device-admin route and a product decision. Windows is not affected (`SC_MONITORPOWER` really powers the monitor down).

## Platform fact — is speech actually playing? Look at `dumpsys audio`

Android uses the **device's own TTS engine**; on this device `settings get secure tts_default_synth` is `com.xiaomi.mibrain.speech` (`tts_default_locale=null`). A successful binding looks like:

```
I TextToSpeech: Sucessfully bound to com.xiaomi.mibrain.speech
I TextToSpeech: Connected to TTS engine
```

`logcat` does not tell you whether sound came out; the audio track does. In `dumpsys audio`, `com.xiaomi.mibrain.speech` (pid 24016) creates one `AudioTrack` per utterance (`attr: usage=USAGE_MEDIA content=CONTENT_TYPE_SPEECH`), and `started→stopped` is exactly one read-aloud. Measured, five in a row:

| # | started | stopped | Duration |
|---|---|---|---|
| 1 | 16:11:07.883 | 16:11:15.318 | 7.4 s |
| 2 | 16:11:17.260 | 16:11:30.354 | 13.1 s |
| 3 | 16:11:31.912 | 16:11:42.497 | 10.6 s |
| 4 | 16:11:43.996 | 16:11:57.674 | 13.7 s |
| 5 | 16:11:59.048 | 16:12:13.656 | 14.6 s |

The gap between items is ≈1 s, which is `SpeechLeadSeconds`. The durations match a 70-character Chinese summary at roughly 5 characters per second. That proves three things at once: speech really happens, `UtteranceProgressListener.OnDone` fires on time, and the playback engine really uses "speech finished" as the item clock.

**Speech quality cannot be evaluated objectively** — there is no audio loopback; only a human can judge it. The app's job is just to hand the text to the system engine.

## Platform fact — MIUI blocks auto-start on boot

The code side is correct. `BootReceiver` is registered with three actions (`BOOT_COMPLETED`, `MY_PACKAGE_REPLACED`, `QUICKBOOT_POWERON`), `enabled=true exported=true`, the boolean defaults to `true` when no preferences file exists, and `RECEIVE_BOOT_COMPLETED` is declared. After a reboot the broadcast is in fact delivered to it:

```
Deliver 0 #154: (manifest)
ActivityInfo:
  name=crc64017365ed8da6996e.BootReceiver
  packageName=com.newsboard.app
  enabled=true exported=true directBootAware=false
```

The system then refuses to start the process (this class is MIUI-specific, absent from AOSP):

```
W BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast
  Intent { act=android.intent.action.BOOT_COMPLETED flg=0x89000010 (has extras) }:
  process is not permitted to auto start
```

Two corroborating facts: `cmd appops get com.newsboard.app 10021` returns **`MIUIOP(10021): ignore`** (10021 is MIUI's own auto-start op), and a manually sent `QUICKBOOT_POWERON` lands in the "Historical Broadcast **background**" queue with `dispatchTime=--` / `dispatchClockTime=1970-01-01`, i.e. never dispatched, while the process never appears.

**Conclusion: this is MIUI's auto-start policy, not a code defect.** To allow it, open Settings → Apps → Manage apps → NewsBoard → **Autostart** and turn it on; setting the battery saver to "No restrictions" is recommended as well.

Releasing it from a shell is not possible: the device has no root (`su` is not found), `cmd appops set com.newsboard.app 10021 allow` answers `No operations.`, and `am start -n com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` fails with `Error type 3 … does not exist` even though the activity is listed by `dumpsys package`. Consequently `AutoStartHelper.OpenAutoStartSettings()` gracefully falls back to the app details page on this device; that is not a bug.

Re-test after granting it:

```powershell
adb reboot
adb wait-for-device
adb shell getprop sys.boot_completed          # wait for 1
adb shell pidof com.newsboard.app             # should be non-empty
adb shell dumpsys activity services com.newsboard.app | Select-String isForeground
# expect: NewsBoardForegroundService isForeground=true foregroundId=1001
adb shell cmd appops get com.newsboard.app 10021   # should read allow
```

## Stability soak

Measured over 20 minutes from a cold start, sampling `dumpsys meminfo <pid> | grep TOTAL` every 30 s (40 samples), always the same PID `10223` — no restart, no `PID=GONE`:

| Period | TOTAL PSS | Note |
|---|---|---|
| 16:18:58 → 16:24:30 (samples 1–12) | 230 → 239 MB | stable for the first 12 minutes |
| 16:25:00 → 16:29:31 (13–22) | 258 → 280 MB | one ≈+40 MB jump (WebView rendering / network buffers) |
| 16:30:02 → 16:38:34 (23–40) | **295–302 MB band** | flat for the last 8 minutes, no upward trend |

- **Crashes / ANR**: not a single `newsboard` FATAL or ANR in `logcat` (the two `SIGABRT` lines found are the system `init` process during boot).
- **Playback keeps moving**: the AudioTrack timeline in `dumpsys audio` keeps alternating `started→stopped` for the whole 20 minutes; the last one stopped at 16:38:04, one minute before sampling ended.
- **Screen stays on**: the device's `screen_off_timeout` is 600000 ms (10 minutes), and after 20 minutes it was still `mWakefulness=Awake / mScreenOnFully=true`.
- **Foreground service**: `NewsBoardForegroundService isForeground=true foregroundId=1001`, persistent notification present.

**Scope warning**: the original acceptance criterion for stability on Android was **2 hours**; this run covered 20 minutes. That is enough to show "does not crash, does not stall, does not switch the screen off, memory plateaus", but not enough to judge hour-scale stability — in particular whether the +40 MB jump is followed by a second growth phase.

## Known limitations

- Speech quality on Android depends on the phone's built-in TTS engine and has not been evaluated objectively.
- Auto-start on boot needs a manual allow in the vendor's auto-start manager (on this device `MIUIOP(10021)` currently reads `ignore`).
- The 20-minute soak does not meet the 2-hour criterion.
- Release/AOT publishing has not been run; only the NDK being in place has been verified (see [android-env-report.md](android-env-report.md)).
