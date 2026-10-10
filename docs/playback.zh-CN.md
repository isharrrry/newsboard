# 播放引擎

[English](playback.md) | **中文** | [Español](playback.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

`PlaybackEngine`（在 `src/NewsBoard.Shared/Services/PlaybackEngine.cs`）是一个两阶段状态机：在本轮的 SHOW 窗口里逐条播放播放列表，窗口到点进 BLACKOUT，黑屏结束开下一轮。它只依赖 `Abstractions` 里的接口，所有时间都取自注入的 `IClock` —— 这正是它能在测试里用可缩放时钟精确驱动的原因。

## 状态机

```
SHOW 窗口（ShowDurationSeconds，默认 60 秒）
  → 按本轮播放列表逐条播放
  → 窗口到点 → BLACKOUT（BlackoutDurationSeconds，默认 300 秒）
  → 回到 SHOW（下一轮）
```

| 配置项 | 对循环的影响 |
|---|---|
| `ShowDurationSeconds`（60） | 一轮展示窗口的长度 |
| `BlackoutDurationSeconds`（300） | 黑屏持续多久 |
| `BlackoutCycleEnabled`（true） | 关掉则一直亮屏；手动**打断**仍会灭一次 |
| `ResumeAcrossRounds`（true） | 被窗口打断的条目保留位置与已播时长；关掉则下一轮从列表头重来 |
| `WindowEndBehavior` | `FinishCurrentItem`（默认）让当前条念完；`InterruptImmediately` 在窗口边界切断朗读 |
| `PlayOrder` | 本轮播放列表的排序方式（默认 `Random`） |

播放列表在每轮开始时重建，所以本轮中途抓到的新闻由下一轮接上。播放列表为空时引擎不会干等满一个窗口：它等数据，一有就开播。

## 朗读就是条目的时钟

语音开启时，一条的时长就是把它念完的时间：切到该条 → 先静默 `SpeechLeadSeconds`（1 秒）→ 朗读；朗读结束 = 该条结束。`EstimateSpeechSeconds` 是共用的时长估算（中文按每字 0.22 秒、其它文字按每词 0.32 秒，都除以 `SpeechRate`），看板页用它给溢出滚动调速，让文字滚完与朗读念完大致同时发生。

语音不可用时 —— `SpeechEnabled` 关闭、宿主报告 TTS 不可用、或当前时间落在 `QuietHours` 里 —— 该条回退到固定的 `ItemHoldSeconds`（12 秒），滚动按 `ScrollSpeedPxPerSecond`（60）走。当 `TtsUnavailable = SkipItem` 时，念不了的条目不占任何时间，直接跳过。

Windows 实测：`System.Speech` 配 `Microsoft Huihui Desktop`（zh-CN）念一条摘要用 6135 ms，`SpeakCompleted` 按时触发；取消在 744 ms 内返回。

## 首帧闸门

`ArmFirstFrameGate()` 让首条在朗读前等 UI 报告首帧真的画出来了（`MarkFirstFrameRendered()`）。等待上限 8 秒，停止 / 手动切条 / 窗口到点都能打断它。

Windows 实测：加闸门之前，朗读比画面早约 1.9 秒，启动头 2.9–11.3 秒是纯黑无字；加了之后，画面比朗读早 2.3–3.1 秒。

## 控制与手势

看板页右下角横排 5 个按钮：

| 按钮 | 动作 |
|---|---|
| ⏮ 上一条 | 上一条，按列表循环回绕 |
| ⏭ 下一条 | 下一条，按列表循环回绕 |
| 🔗 打开原文 | 用系统浏览器打开当前条的 URL |
| ⏸ 打断 | 立刻结束本轮，灭屏等待 `BlackoutDurationSeconds` 秒后开下一轮 —— 即使 `BlackoutCycleEnabled` 关着也会灭一次 |
| ⚙ 设置 | 打开设置页 |

黑屏期间控制条是隐藏的（`board-controls--hidden`），黑色遮罩铺满屏幕，所以点哪儿都落在遮罩上并调用 `WakeAsync()`：灭屏立刻结束、画面回来、窗口从当前条重新起算。SHOW 期间点击没有任何作用 —— 不切条，也不缩短窗口。Windows 实测：亮度 58.4 → 打断 0 → 点一下 58.4。

## 灭屏因平台而异

| 宿主 | 灭屏做了什么 | 实测 |
|---|---|---|
| WPF | 真的关掉显示器：`SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND(0x0112), SC_MONITORPOWER(0xF170), 2 / -1, SMTO_ABORTIFHUNG, 1000)`（2 = 关，−1 = 开） | 关屏动作本身未人工确认，见「已知限制」 |
| MAUI(Windows) | 同一个 Win32 广播；因为该宿主不做无边框全屏，护栏退化为「应用窗口是前台」 | — |
| MAUI(Android) | 窗口亮度置 0 + 全黑遮罩；**背光仍然亮着** | 亮度覆盖 `NaN`（无覆盖）→ `0.0`，截图 300,867 B → 27,787 B，1.2 秒内完成；点屏幕后回到 `NaN` 与 300,867 B |

`MonitorPowerPolicy` 是共用的纯逻辑护栏：只有「应用窗口是前台窗口」**或**「窗口是无边框全屏」才真的关屏。你切到别的程序去干活时，看板只显示黑遮罩，不会把你屏幕黑掉。反过来，**开屏**故意不受护栏限制 —— 否则一个关了屏又失去焦点的看板就永远叫不醒屏幕了。

Android 上普通应用没有免权限的真熄屏手段：`PowerManager.GoToSleep()` 是签名权限，`DevicePolicyManager.LockNow()` 要设备管理员权限、还会连带锁屏，唤醒就得先解锁。Android 的常亮由窗口标志 `KEEP_SCREEN_ON` 承担，引擎在黑屏期间**故意不撤**它 —— 撤了系统就会按 `screen_off_timeout` 真的把屏幕关掉。

## 长稳实测

在 Android 真机（Xiaomi `M2011K2C`、Android 13）上跑 20 分钟，每 30 秒采一次 `dumpsys meminfo`：

| 时段 | TOTAL PSS | 说明 |
|---|---|---|
| 前 12 分钟 | 230 → 239 MB | 稳定，小幅爬升 |
| 随后约 5 分钟 | 258 → 280 MB | 一次约 +40 MB 的跃升 |
| 最后 8 分钟 | 295–302 MB 区间震荡 | 平台化，无趋势 |

- PID 全程没变：没有重启，也没有 `PID=GONE`。
- `logcat` 里没有 `com.newsboard.app` 的 `FATAL` 或 `ANR`。
- `dumpsys audio` 的 AudioTrack 时间线持续 `started → stopped`；语音开启时，单条朗读实测 7.4 / 13.1 / 10.6 / 13.7 / 14.6 秒，条间约 1 秒（= `SpeechLeadSeconds`）。
- 该机 `screen_off_timeout` 是 600000 ms（10 分钟）；20 分钟后仍是 `mWakefulness=Awake` / `mScreenOnFully=true`。
- 同一台设备的冷启动：`am start` 后 +2.8 秒还是启动提示（39 KB 截图），+5.2 秒画面已渲染完成（306 KB 截图）。

方案给这一项定的验收目标是连跑 2 小时；目前实际测到的是 20 分钟。

## 已知限制

- **Android 的灭屏不是关屏**：背光仍然亮着。各宿主到底做了什么，见上面的表。
- **Windows 的真关屏未人工确认**：实现与护栏都在，但自动化里没有观察到背光真的发生变化。
- **长稳是 20 分钟，不是方案要求的 2 小时**：内存已平台化，但小时级行为未测。
- **语音音质取决于宿主提供的引擎** —— Android 用设备自带引擎（在一台小米设备上实测为 `com.xiaomi.mibrain.speech`），Windows 用 `System.Speech` 配 `Microsoft Huihui Desktop`（zh-CN）。应用只把文本交给引擎，不评估念出来好不好听。
- **重要度门槛把缓存筛空时**，画面上那一条是兜底补的：摘要末尾带 ` ~`。
