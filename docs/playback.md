# Playback engine

**English** | [中文](playback.zh-CN.md) | [Español](playback.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

`PlaybackEngine` (in `src/NewsBoard.Shared/Services/PlaybackEngine.cs`) is a two-phase state machine: it plays the round's playlist inside a SHOW window, then goes to BLACKOUT, then starts the next round. It depends only on the `Abstractions` interfaces, and every timestamp comes from the injected `IClock` — which is what makes it drivable with a scaled clock in tests. All three hosts share this one engine — `NewsBoard.Maui` on Android, `NewsBoard.Wpf` on Windows, and the Windows head of `NewsBoard.Maui` — so platform differences show up only where the sections below say so.

## State machine

```
SHOW window (ShowDurationSeconds, default 60 s)
  → item by item through the round's playlist
  → window ends → BLACKOUT (BlackoutDurationSeconds, default 300 s)
  → SHOW again (next round)
```

| Setting | Effect on the loop |
|---|---|
| `ShowDurationSeconds` (60) | length of one round's display window |
| `BlackoutDurationSeconds` (300) | how long the blackout lasts |
| `BlackoutCycleEnabled` (true) | off means the screen stays on; a manual **interrupt** still blacks out once |
| `ResumeAcrossRounds` (true) | an item cut off by the window keeps its place and its already-played seconds; otherwise the next round restarts from the top of the playlist |
| `WindowEndBehavior` | `FinishCurrentItem` (default) lets the current item be read to the end; `InterruptImmediately` cuts the reading at the window edge |
| `PlayOrder` | how the round's playlist is ordered (default `Random`) |

The playlist is rebuilt at the start of every round, so news fetched mid-round is picked up by the next one. When the playlist is empty the engine does not wait out a whole window: it waits for data and starts as soon as an item arrives.

## Speech is the item clock

With speech enabled, an item lasts exactly as long as it takes to read: switch to the item → stay silent for `SpeechLeadSeconds` (1 s) → read aloud; the item ends when the reading ends. `EstimateSpeechSeconds` is the shared estimate (Chinese: 0.22 s per character, other scripts: 0.32 s per word, both divided by `SpeechRate`), and the board page uses it to pace overflow scrolling so the text finishes scrolling at roughly the same moment the reading finishes.

When speech is not usable — `SpeechEnabled` off, the host reports TTS unavailable, or the current time falls inside `QuietHours` — the item falls back to a fixed `ItemHoldSeconds` (12 s), and scrolling runs at `ScrollSpeedPxPerSecond` (60). With `TtsUnavailable = SkipItem`, an item that cannot be read takes no time at all and is skipped.

The speech engine comes from the host; the three hosts use two implementations:

| Platform | Speech engine | Measured |
|---|---|---|
| Android (`NewsBoard.Maui`) | the device's own TTS engine | measured on a Xiaomi device: `com.xiaomi.mibrain.speech` |
| Windows (`NewsBoard.Wpf`, and the Windows head of `NewsBoard.Maui`) | `System.Speech` | `Microsoft Huihui Desktop` (zh-CN) read one summary in 6135 ms and `SpeakCompleted` fired on time; cancelling returned in 744 ms |

## First-frame gate

`ArmFirstFrameGate()` makes the first item wait for the UI to report that the first frame has actually been painted (`MarkFirstFrameRendered()`). The wait is capped at 8 seconds, and stop / manual switch / window end all break it.

Measured on Windows: before the gate existed, speech started about 1.9 s before the picture and the opening 2.9–11.3 s was black with no text; after it, the picture leads the speech by 2.3–3.1 s.

## Controls and gestures

Five buttons sit in the bottom-right of the board page:

| Button | Action |
|---|---|
| ⏮ previous | previous item, wrapping around the list |
| ⏭ next | next item, wrapping around the list |
| 🔗 open source | opens the current item's URL in the system browser |
| ⏸ interrupt | ends this round immediately, blacks out for `BlackoutDurationSeconds`, then starts the next round — even when `BlackoutCycleEnabled` is off |
| ⚙ settings | opens the Settings page |

During a blackout the control bar is hidden (`board-controls--hidden`) and the black overlay covers the screen, so a tap anywhere lands on the overlay and calls `WakeAsync()`: the blackout ends at once, the display comes back, and the window is recounted from the current item. Tapping during SHOW does nothing — it neither switches items nor shortens the window. Measured on Windows: brightness 58.4 → interrupt 0 → one tap 58.4.

## Blackout and keep-screen-on: platform differences

| Capability | Android (`NewsBoard.Maui`) | Windows (`NewsBoard.Wpf`) | Windows (the Windows head of `NewsBoard.Maui`) |
|---|---|---|---|
| Blackout | window brightness 0 plus a full-black overlay; **the backlight stays on** | **really turns the monitor off**: `SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)` (2 = off, −1 = on) | the same Win32 broadcast |
| Monitor-off guard | not applicable | the monitor is cut only when the app window is the foreground window **or** the window is borderless fullscreen (`MonitorPowerPolicy`) | the same, except that this host does no borderless fullscreen, so the guard degrades to “app window is foreground” |
| Keep screen on | carried by the window flag `KEEP_SCREEN_ON`, which a blackout deliberately does not drop | empty implementation (no such need on the desktop) | empty implementation |

Measured evidence:

- Android: brightness override `NaN` (no override) → `0.0`, screenshot 300,867 B → 27,787 B within 1.2 s; tapping the screen brings it back to `NaN` and 300,867 B.
- Windows (`NewsBoard.Wpf`): the monitor-off itself has not been confirmed by hand; see Known limitations.

`MonitorPowerPolicy` is the shared pure-logic guard (the rule in the “Monitor-off guard” row above): the monitor is cut only when the app window is the foreground window **or** the window is borderless fullscreen. If you switch to another program, the board keeps showing a black overlay instead of blacking out your screen. Turning the monitor back **on** is deliberately not gated — otherwise a board that blacked out and then lost focus could never bring the screen back.

On Android there is no permission-free way to really switch the panel off: `PowerManager.GoToSleep()` requires a signature permission, and `DevicePolicyManager.LockNow()` needs device-admin rights and locks the device, so waking would require an unlock. Android keep screen on is carried by the window flag `KEEP_SCREEN_ON`, which the engine deliberately leaves set during a blackout — dropping it would let the system's `screen_off_timeout` (measured on this device: 600000 ms) switch the panel off for real.

## Measured long run

A 20-minute session on the Android device (Xiaomi `M2011K2C`, Android 13), sampling `dumpsys meminfo` every 30 seconds:

| Period | TOTAL PSS | Note |
|---|---|---|
| first 12 min | 230 → 239 MB | stable, small climb |
| next ~5 min | 258 → 280 MB | one jump of about +40 MB |
| last 8 min | 295–302 MB, oscillating | plateau, no trend |

- The PID never changed: no restart, no `PID=GONE`.
- No `FATAL` or `ANR` line for `com.newsboard.app` in `logcat`.
- The `dumpsys audio` AudioTrack timeline kept alternating `started → stopped`; with speech on, single readings measured 7.4 / 13.1 / 10.6 / 13.7 / 14.6 seconds, about 1 second apart (= `SpeechLeadSeconds`).
- `screen_off_timeout` on that device is 600000 ms (10 minutes); after 20 minutes the device was still `mWakefulness=Awake` / `mScreenOnFully=true`.
- Cold start on the same device: `am start` → at +2.8 s still the boot hint (39 KB screenshot), at +5.2 s the board had rendered (306 KB screenshot).

The plan's acceptance target for this item was a 2-hour run; 20 minutes is what has actually been measured.

## Known limitations

- **Android blackout is not a monitor-off**: the backlight stays on. The table above says what each host actually does.
- **The Windows monitor-off has not been confirmed by hand**: the implementation and its guard are in place, but no automated run has observed the backlight actually changing.
- **The long run is 20 minutes, not the 2 hours the plan asks for**: memory plateaued, but hour-scale behaviour is unmeasured.
- **Speech quality depends on the engine the host provides** — Android uses the device engine (measured on a Xiaomi device: `com.xiaomi.mibrain.speech`), Windows uses `System.Speech` with `Microsoft Huihui Desktop` for zh-CN. The app only hands text to the engine; it does not evaluate how it sounds.
- **When the importance threshold filters the cache empty**, the single item shown is a fallback: its summary ends with ` ~`.
