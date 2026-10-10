# Android

[English](android.md) | **中文** | [Español](android.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

本篇是 NewsBoard 的**平台篇之一**；跨平台总览见 [README](../README.zh-CN.md) 与 [架构](architecture.zh-CN.md)，Windows 侧见 [Windows 宿主](windows.zh-CN.md)。

Android 宿主是 `src/NewsBoard.Maui`（目标框架 `net10.0-android`），渲染与 Windows 宿主相同的 Blazor 界面，跑在一台搁在桌上、会念新闻的手机上。它是当前唯一做过真机装机实测的移动平台。

## 工具链前置

本机工具链装在 `D:\Android`，由 `scripts\android-env.ps1` 把当前终端指过去。

| 组件 | 路径 / 版本 |
|---|---|
| .NET SDK | `10.0.203`，运行时 `10.0.7` |
| workload | `maui-android` `10.0.20/10.0.100`（工作负载版本 `10.0.204.1`） |
| JDK 17 | `D:\Android\jdk-17`（OpenJDK 17.0.20.1），0.29 GB |
| Android SDK | `D:\Android\Sdk` —— `platforms;android-36`、`build-tools;36.0.0`、`platform-tools`、`cmdline-tools;latest`，0.40 GB |
| NDK | `D:\Android\Sdk\ndk\27.2.12479018`（r27c），2.20 GB |

**每个新终端都要先点源脚本** —— 本机执行策略是 `Restricted`，所以先放开当前进程再点源：

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
. .\scripts\android-env.ps1
```

脚本会设置 `DOTNET_ROOT`、`JAVA_HOME`、`ANDROID_HOME` / `ANDROID_SDK_ROOT` / `AndroidSdkDirectory`、`JavaSdkDirectory`、`ANDROID_NDK_HOME` / `ANDROID_NDK_ROOT`，并把 `dotnet`、`jdk-17\bin`、`platform-tools`、`cmdline-tools\latest\bin`、`emulator` 前置到 `PATH`。脚本幂等、只影响当前进程、路径不存在时只告警不中断；文件必须保持 **UTF-8 with BOM**（没有 BOM 时点源后中文注释是乱码）。不点源会构建失败：`error XA5300 找不到 Android SDK 目录`。

完整的环境清单（装了什么、装在哪、占多少、哪些从未验证）见 [android-env-report.md](android-env-report.md)。

## 构建

```powershell
# Debug APK -> src\NewsBoard.Maui\bin\Debug\net10.0-android\*-Signed.apk
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -v q --nologo

# Release：自包含、可直接分发
dotnet publish src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -c Release

# 整个解决方案（WPF + MAUI/Android + MAUI/Windows）
dotnet build NewsBoard.slnx -v q --nologo
```

## 部署到设备

**Debug 包不能只用 `adb install` 安装。** Debug 构建走 .NET Android 的 **Fast Deployment**：程序集不在 APK 里，部署目标会把它们另外推送到设备上的 `files/.__override__/<abi>`。只装那个 `*-Signed.apk` 会启动即崩：

```
F/libc: Fatal signal 6 (SIGABRT), code -1 (SI_QUEUE) in tid 12152 (m.newsboard.app)
Abort message: 'No assemblies found in '/data/user/0/com.newsboard.app/files/.__override__/arm64-v8a'
                or '<unavailable>'. Assuming this is part of Fast Deployment. Exiting...'
backtrace: #00 libc.so (abort)  ←  #01 libmonodroid.so (abort_application)
```

改用官方部署目标（`-t:Run` 亦可，它装完还会顺手启动）：

```powershell
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

装机后可以用 `adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l` 数一下推送过去的程序集（本次为 440 个文件）。要一个能独立安装的包请构建 Release。

## 真机验收结果

设备：Xiaomi `M2011K2C`（Redmi K40），Android 13 / SDK 33 / arm64-v8a，序列号 `<device-serial>`。

| 项 | 结果 |
|---|---|
| 装机 | `com.newsboard.app` v1.0，minSdk 24 / targetSdk 36 |
| 启动 | 进程稳定存活、无 FATAL / abort；首次启动会弹通知权限（MIUI 系统弹窗），授权后进入看板 |
| 渲染 | 关键字标签 + 时钟 + 大字摘要 + 底部控制条（⏮ ⏭ 🔗 ⏸ ⚙） |
| 抓取 | **16 条**，6 个关键字全部命中：中国 2 / 武汉 3 / 美国 3 / 台湾 3 / 国际 2 / AI 3；含摘要与重要度（示例 0.81），通道为「联网搜索」 |
| 数据 | `files/NewsBoard/config.json`(1817 B) + `files/NewsBoard/news.json`(22464 B)，均在 app 私有目录 |

冷启动用 `am start` 量的时间线：+2.8 秒时画面还是启动提示（39 KB 截图），**+5.2 秒画面已渲染完成**（306 KB）。

## 平台事实一：黑屏不是关屏

Android 侧的 `IScreenController` 实现把窗口的 `WindowManager.LayoutParams.ScreenBrightness` 置为 `0f` 并铺一层全黑遮罩，**不会关掉屏幕背光**。

| 时刻 | 操作 | `mScreenBrightnessOverrideFromWindowManager` | 截图字节 |
|---|---|---|---|
| 显示中 | — | `NaN`（无覆盖 = 用系统亮度） | — |
| 点「打断」（第 4 个控制按钮） | `input tap 919 2287` | **`0.0`** | **27,787**（全黑） |
| 点屏幕中央唤醒 | `input tap 540 1200` | **`NaN`** | **300,867**（画面恢复） |

两个方向都在 **1.2 秒内**生效；黑屏期间 `dumpsys audio` 显示朗读已经 `stopped`。

**屏幕常亮**由窗口标志承担：`dumpsys window` 里能看到 `fl=… KEEP_SCREEN_ON …` 与 `mLastWakeLockHoldingWindow=…MainActivity`。控制器**在黑屏期间有意保留**这个标志，因此系统不会在 `screen_off_timeout`（本机 600000 ms = 10 分钟）之后熄屏 —— 20 分钟长稳全程保持 `mWakefulness=Awake / mScreenOnFully=true`。

**边界**：因为背光还亮着，这个黑屏不是真正的关屏。Android 上免权限做不到真熄屏 —— `PowerManager.GoToSleep()` 是 `DEVICE_POWER` 签名权限，`DevicePolicyManager.LockNow()` 需要设备管理员权限**而且会真的锁屏**，与「点屏幕即唤醒」冲突。这是非特权应用能做到的最接近效果，真关屏只能走设备管理员方案并且是个产品决策。Windows 侧不受此限（`SC_MONITORPOWER` 真的关显示器）。

## 平台事实二：有没有真的念出来，看 `dumpsys audio`

Android 用**设备自带语音引擎**；本机 `settings get secure tts_default_synth` 是 `com.xiaomi.mibrain.speech`（`tts_default_locale=null`）。绑定成功长这样：

```
I TextToSpeech: Sucessfully bound to com.xiaomi.mibrain.speech
I TextToSpeech: Connected to TTS engine
```

`logcat` 说不清有没有出声，音频轨能。`dumpsys audio` 里 `com.xiaomi.mibrain.speech`（pid 24016）为每次朗读创建一条 `AudioTrack`（`attr: usage=USAGE_MEDIA content=CONTENT_TYPE_SPEECH`），`started→stopped` 就是一次朗读。实测连续 5 次：

| # | started | stopped | 时长 |
|---|---|---|---|
| 1 | 16:11:07.883 | 16:11:15.318 | 7.4 秒 |
| 2 | 16:11:17.260 | 16:11:30.354 | 13.1 秒 |
| 3 | 16:11:31.912 | 16:11:42.497 | 10.6 秒 |
| 4 | 16:11:43.996 | 16:11:57.674 | 13.7 秒 |
| 5 | 16:11:59.048 | 16:12:13.656 | 14.6 秒 |

条间间隔约 1 秒，正是 `SpeechLeadSeconds`；时长与 70 字中文摘要吻合（约 5 字/秒）。这一次性证明了三件事：朗读真实发生、`UtteranceProgressListener.OnDone` 按时回调、播放引擎确实拿「朗读结束」当条目时钟。

**音质无法客观评估** —— 没有录音回环，只能人耳判断。应用只负责把文本交给系统引擎。

## 平台事实三：MIUI 拦开机自启

代码侧是对的。`BootReceiver` 注册了三个 action（`BOOT_COMPLETED`、`MY_PACKAGE_REPLACED`、`QUICKBOOT_POWERON`），`enabled=true exported=true`；没有 preferences 文件时布尔开关默认 `true`；`RECEIVE_BOOT_COMPLETED` 权限已声明。重启后广播**确实派发到了它**：

```
Deliver 0 #154: (manifest)
ActivityInfo:
  name=crc64017365ed8da6996e.BootReceiver
  packageName=com.newsboard.app
  enabled=true exported=true directBootAware=false
```

紧接着系统拒绝拉起进程（这个类是 MIUI 私有的，AOSP 里没有）：

```
W BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast
  Intent { act=android.intent.action.BOOT_COMPLETED flg=0x89000010 (has extras) }:
  process is not permitted to auto start
```

两个佐证：`cmd appops get com.newsboard.app 10021` 返回 **`MIUIOP(10021): ignore`**（10021 是 MIUI 自己的自启动 op）；手动补发的 `QUICKBOOT_POWERON` 落进「Historical Broadcast **background**」队列，`dispatchTime=--` / `dispatchClockTime=1970-01-01`，也就是从不派发，同时进程从未出现。

**结论：这是 MIUI 的自启动策略，不是代码缺陷。** 放行路径：设置 → 应用设置 → 应用管理 → 新闻看板 → **自启动**（打开）；建议同时把省电策略设为「无限制」。

从 shell 代放行做不到：设备没有 root（`su` not found）、`cmd appops set com.newsboard.app 10021 allow` 返回 `No operations.`、`am start -n com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` 报 `Error type 3 … does not exist`（尽管 `dumpsys package` 里列着这个 activity）。因此 `AutoStartHelper.OpenAutoStartSettings()` 在本机会优雅降级到应用详情页，这不是 bug。

放行后这样复测：

```powershell
adb reboot
adb wait-for-device
adb shell getprop sys.boot_completed          # 等到 1
adb shell pidof com.newsboard.app             # 应当非空
adb shell dumpsys activity services com.newsboard.app | Select-String isForeground
# 期望：NewsBoardForegroundService isForeground=true foregroundId=1001
adb shell cmd appops get com.newsboard.app 10021   # 应变成 allow
```

## 长稳测试

冷启动后连续 20 分钟，每 30 秒采一次 `dumpsys meminfo <pid> | grep TOTAL`，共 40 个采样点，全程同一个 PID `10223` —— 没有重启，没有 `PID=GONE`：

| 时段 | TOTAL PSS | 说明 |
|---|---|---|
| 16:18:58 → 16:24:30（第 1–12 点） | 230 → 239 MB | 前 12 分钟稳定 |
| 16:25:00 → 16:29:31（第 13–22 点） | 258 → 280 MB | 一次约 +40 MB 的跃升（WebView 渲染 / 网络缓冲） |
| 16:30:02 → 16:38:34（第 23–40 点） | **295–302 MB 区间** | 最后 8 分钟平台化，无上升趋势 |

- **崩溃 / ANR**：`logcat` 里没有一条 `newsboard` 的 FATAL 或 ANR（扫到的两条 `SIGABRT` 是开机时系统 `init` 进程的）。
- **播放持续推进**：`dumpsys audio` 的 AudioTrack 时间线在整个 20 分钟里持续 `started→stopped`；最后一条停在 16:38:04，比采样结束早一分钟。
- **屏幕保持常亮**：本机 `screen_off_timeout` 是 600000 ms（10 分钟），20 分钟后仍是 `mWakefulness=Awake / mScreenOnFully=true`。
- **前台服务**：`NewsBoardForegroundService isForeground=true foregroundId=1001`，常驻通知在位。

**口径提醒**：Android 侧长稳原本的验收标准是 **2 小时**，本次只跑了 20 分钟。20 分钟足以说明「不崩、不卡、不自动熄屏、内存平台化」，但不足以判定小时级稳定性 —— 特别是那次 +40 MB 跃升之后是否还有二级增长。

## 已知限制

- Android 上的朗读音质取决于手机自带语音引擎，未做客观评估。
- 开机自启需要在厂商自启动管理里手动放行（本机 `MIUIOP(10021)` 当前是 `ignore`）。
- 20 分钟长稳未达 2 小时口径。
- 未跑过 Release/AOT 发布，只验证到 NDK 就位（见 [android-env-report.md](android-env-report.md)）。
