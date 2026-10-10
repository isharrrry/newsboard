# 故障排查

[English](troubleshooting.md) | **中文** | [Español](troubleshooting.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

下面每一条都按 **症状 → 原因 → 处置** 写，每条处置都是可以直接跑的命令或可以直接打开的路径。凡标「实测」的，都是在本仓这台机器或测试真机上观察到的结果，不是推断。

---

## 1. 画面全黑、`#app` 空白（MAUI 宿主页抢跑）

**症状** —— MAUI 宿主上进程活着、WebView 铺满屏幕，但什么都不渲染：`#app.innerHTML.length === 0`；`logcat` / 控制台里能看到 `Uncaught (in promise) Error: Blazor has already started.`

**原因** —— MAUI 宿主页（`src/NewsBoard.Maui/wwwroot/index.html`）抢在框架之前启动了 Blazor。MAUI 的 `BlazorWebView` 会**自己**启动 Blazor 并注册 `MainPage.xaml` 里声明的根组件（`Selector="#app"`）；宿主页脚本先调 `Blazor.start()`，就会以「没有根组件」的方式把 Blazor 启动起来，框架随后带根组件的启动被拒，`#app` 永远空白。Android 真机实测：`pidof` 有值、`uiautomator` 里 WebView 铺满屏幕、CDP 查到 `#app.innerHTML.length === 0`。

**处置** —— 宿主页**绝不可**抢在框架之前调用 `Blazor.start()`。正确写法只是观察：定时器看 `#app` 有没有子节点（有就撤掉 `#boot-hint` 元素并结束），超过 3 秒仍为空才兜底调一次 `Blazor.start()`。回归测试 `src/NewsBoard.Tests/Sa5MauiHostPageTests.cs` 断言 `Blazor.start()` 出现在 `setInterval` 之后（断言前会先剥掉行注释）。WPF 的宿主页是另一份文件，不受影响。

---

## 2. 一直停在「正在获取新闻…」

**症状** —— 看板页永远停在占位文案 `正在获取新闻…`；最坏的情况下进程零网络连接。

**原因** —— 已知四种，按值得先查的顺序排列：

1. 没有配置 API Key，抓取与提炼都会失败。
2. MAUI 宿主启动期没有调用 `InitializeNewsBoardAsync()`，配置（含 Key）不加载、抓取调度器不启动。
3. RSS 备用链里的源全都不可用。2026-10-08 实测：Bing `?format=RSS` 返回 `text/html`、百度 `newsrss` 返回安全验证页、Google News RSS 在本机超时。
4. 缓存里的条目被重要度门槛全部滤掉 —— 见第 9 条。

**处置** ——

- Key：设置页 → **API / 采集** 分组 → 粘贴 Key。Android 上它存进 `SecureStorage`。
- MAUI 宿主：`MauiProgram.CreateMauiApp()` 必须调用 `InitializeNewsBoardAsync()`；`Display.razor` 起播前也会再 await 一次兜底（幂等）。
- 源：在设置页检查 `RssTemplate` / `RssFallbackTemplates`。本机实测可用的频道源：`https://www.chinanews.com.cn/rss/{scroll-news,china,world}.xml`、`http://www.people.com.cn/rss/{politics,world}.xml`、`https://www.ithome.com/rss/`。
- 门槛：见第 9 条。

---

## 3. Android：只 `adb install` 调试包，启动即崩

**症状** —— Debug 的 `*-Signed.apk` 用 `adb install` 装得上，但启动即崩：

```
Fatal signal 6 (SIGABRT)
Abort message: 'No assemblies found in /data/user/0/com.newsboard.app/files/.__override__/arm64-v8a
... Assuming this is part of Fast Deployment. Exiting...'
```

**原因** —— Debug 构建走 .NET Android 的 **Fast Deployment**：程序集不在 APK 里，而是由部署目标推送到设备的 `files/.__override__/<abi>` 目录。只 `adb install` 会跳过这一步。

**处置** —— 用 MSBuild 目标部署：

```powershell
dotnet build src\NewsBoard.Maui\NewsBoard.Maui.csproj -f net10.0-android -t:Install -v q --nologo
```

`-t:Run` 亦可，它会顺带启动。要一个能直接分发安装的自包含 APK，用 Release 构建。确认程序集已推送：`adb shell run-as com.newsboard.app ls files/.__override__/arm64-v8a | wc -l`（本次实测 440 个文件）。

---

## 4. Android：开机自启不生效（MIUI）

**症状** —— 重启后应用没起来：`adb shell pidof com.newsboard.app` 为空、通知栏没有常驻通知、`dumpsys activity services com.newsboard.app` 什么都没有。

**原因** —— 代码链路正确，是 MIUI 拦的。Xiaomi `M2011K2C`（Android 13）实测：`dumpsys activity broadcasts` 显示 `BOOT_COMPLETED` **确实派发给了**接收者（`Deliver 0 #154: (manifest)` → `name=crc64017365ed8da6996e.BootReceiver`），紧接着系统日志给出：

```
W BroadcastQueueInjector: Unable to launch app com.newsboard.app/10244 for broadcast
  Intent { act=android.intent.action.BOOT_COMPLETED … }: process is not permitted to auto start
```

且 `cmd appops get com.newsboard.app 10021` 返回 `MIUIOP(10021): ignore`。

**处置** —— 手动放行：设置 → 应用设置 → 应用管理 → 新闻看板 → **自启动** 打开；同时把省电策略设为**无限制**。然后 `adb reboot` 复测，检查 `pidof com.newsboard.app` 与 `dumpsys activity services com.newsboard.app`（应见 `NewsBoardForegroundService isForeground=true foregroundId=1001`）。无 root 时没法从 adb 代做：`cmd appops set com.newsboard.app 10021 allow` 返回 `No operations.`，`am start -n com.miui.securitycenter/com.miui.permcenter.autostart.AutoStartManagementActivity` 报 `Error type 3 … does not exist`。

---

## 5. Android：黑屏阶段没有熄灭屏幕背光

**症状** —— 黑屏期间画面全黑，但背光明显还亮着。

**原因** —— 这是设计取舍，不是缺陷。`AndroidScreenController.SetBlackoutAsync(true)` 只把窗口亮度置 `0f` 并铺一层全黑遮罩，物理背光仍然亮着。Android 上普通应用没有免权限的真熄屏手段：`PowerManager.GoToSleep()` 需要签名级的 `DEVICE_POWER` 权限；`DevicePolicyManager.LockNow()` 需要设备管理员授权，而且会真的锁屏，唤醒就得先解锁。实测：`mScreenBrightnessOverrideFromWindowManager` 由 `NaN` 变 `0.0`，同刻截图由 300,867 字节降到 27,787 字节。

**处置** —— 无需修复，按已知限制对待。常亮由窗口标志 `KEEP_SCREEN_ON` 承担，黑屏阶段**故意不撤**它 —— 撤掉的话系统会按 `screen_off_timeout`（本机实测 600000ms）真的息屏。Windows 宿主才是真关屏，走 `SC_MONITORPOWER`。

---

## 6. 同一张配图出现在多条新闻上，或某条没有配图

**症状** —— 修复前实测：9 条里 9 条有图，但 3 组共用同一张模板图（5 条 / 4 条 / 4 条）。修复后：9 条里 5 条有图，没有重复组。

**原因** —— 两个独立问题。第一，中新网、人民网的文章页既没有 `og:image` 也没有 `twitter:image`，它们的 RSS 也没有 `enclosure` / `media:*`，所以只看 meta 的取图逻辑只能退回到同一张站点模板图。第二，没有任何机制阻止一个图片 URL 被分给多条新闻。

**处置** —— `ImageResolver` 现在走四级 —— `og:image` → `twitter:image` → `link[rel=image_src]` → 正文首图（优先正文容器内、`<p>` 之后，跳过 logo、图标、占位图，以及比文章日期早 60 天以上的模板素材）—— 并且对每个候选发 Range 请求校验它**真的**返回 `image/*`（中新网图床有防盗链，否则取回的是 HTML）。`ImageResolver.DedupeImages` 加 `ApplyImagesAsync(..., reservedImageUrls)` 保证一张图只属于一条新闻；拿不到独占图的条目按「正文没图」处理，不出图。

---

## 7. 关键字标签显示成频道名

**症状** —— 看板页顶部那条标签显示的是频道名（例如 `中新网即时新闻`），而不是你配置的任何一个关键字。

**原因** —— 默认 `RssTemplate` 是共享频道源（不含 `{q}`），一次 HTTP 请求拿回整个频道，条目只带频道名。`KeywordClassifier.Classify` 先按配置顺序在标题里找命中项，再找正文，都不命中才回落到频道名。实测：`Keywords=武汉/AI` + 中新网滚动新闻频道，当天 9 条全是国庆 / 日本 / A 股类新闻，标题与正文都不含这两个关键字，模型也返回了空 `keyword`。**这是「没有命中」，不是「分类丢失」。**

**处置** —— 没有坏。把你源站真正会用的词加进关键字列表，或者换一个与你关键字匹配的源。「只显示命中关键字的新闻」的过滤开关目前**未做**；关键字现在只用作分组标签。

---

## 8. 摘要里夹带繁体字

**症状** —— 联网搜索通道返回的摘要里出现繁体字。修复前实测：9 篇里 6 篇命中 1–3 个繁体字。

**原因** —— 提示词没有钉住输出语言，而该通道有时会检索到港台源。

**处置** —— `AppConfig.SummaryLanguage`（默认 `SimplifiedChinese`）会通过 `SummaryLanguageText.PromptRule()` 写进两条通道的提示词。修复后实测：同样关键字复测 9 篇全 0。注意切换语言**不会**重写缓存里已有的摘要 —— 删掉 `news.json`，或者等自然日保留期过期后重抓。

---

## 9. 门槛过滤后播放列表为空

**症状** —— `ImportanceThreshold` 调高后，明明 `news.json` 里有条目，看板页却一直停在获取占位文案。

**原因** —— `NewsRepository.GetPlaylist` 先按重要度过滤、再排序。一条都没过门槛时列表就是空的。这是配置语义，不是卡死。

**处置** —— 在设置页调低 `ImportanceThreshold`（默认 0.7）。代码里还有一层兜底：过滤后为空时会随机补一条缓存条目（**副本**，不污染缓存），并在摘要末尾加 ` ~` 标记。实测把门槛临时调到 0.95，画面显示 `…移送检察机关审查起诉。 ~`。

---

## 10. 时间敏感测试偶发红

**症状** —— `Sa3PlaybackEngineTests` / `PlaybackControlTests` 在机器繁忙时偶发失败（修复前实测：4 次全量跑里 3 次红），例如 `NextAsync_SkipsToFollowingItem`。

**原因** —— 这些测试用 `ScaledClock`（**100 倍**）驱动，而 `WaitUntilAsync` 每 5ms 真实时间轮询一次 ＝ 0.5s 引擎时间；机器一忙就会在断言读取前冲过检查点。

**处置** —— 已修稳：首条摘要写长（约 40 字）、检查点往窗口里侧挪（59s → 55s、365s → 362s）、容差由 5s 放宽到 20s、取基线前先 `Task.Delay(250)`。修复后实测连续 5 次全量绿，每次 220/220。如果你新增了时间断言，请沿用同样的余量。

---

## 11. 真实数据目录被脚本覆盖

**症状** —— 跑完某个自动化脚本后，关键字或其它设置变了。这真的发生过：4 个 e2e 脚本把测试配置覆盖到用户真实的 `config.json` 上，把 6 个关键字换成了 `武汉/AI`。

**原因** —— 那些脚本写的是真实数据目录（`%LOCALAPPDATA%\NewsBoard`），而且它们留下的 `config-backup.json` 是在覆盖**之后**复制的，MD5 与被破坏的文件相同，等于从未具备回滚能力。

**处置** —— 任何要跑真实抓取的脚本，都必须用隔离目录：

```powershell
$env:NEWSBOARD_DATA_DIR = "D:\path\to\isolated-dir"
```

`NewsBoardDataDirectory.Resolve()` 是 `ConfigStore` 与 `NewsRepository` 共用的唯一解析点，`NEWSBOARD_DATA_DIR` 非空即优先。`ConfigStore` 每次覆盖写盘前还会把上一版留成 `config.json.bak`，所以一次误写可以用那个文件还原。

---

## 12. MAUI(Windows) 启动即黑窗并自行退出

**症状** —— 全局杀过一次 WebView2 进程之后，MAUI 的 Windows 头每次启动都是黑窗，然后进程自己退出。

**原因** —— 全局 `Stop-Process -Name msedgewebview2` 会连带杀掉新建实例的 WebView2，并**破坏它的用户数据目录**。

**处置** —— 删掉坏掉的数据目录再启动：

```powershell
Remove-Item -Recurse -Force "src\NewsBoard.Maui\bin\...\NewsBoard.Maui.exe.WebView2"
```

清理时要杀应用进程本身，而不是机器上所有的 `msedgewebview2`。

---

## 13. 含中文的 `.ps1` 脚本解析失败

**症状** —— 含中文的验证脚本解析失败，字符串被从中间截断。

**原因** —— Windows PowerShell 5.1 按 ANSI 读 `.ps1` 文件，UTF-8 无 BOM 的文件会被错误解码。

**处置** —— 要么让脚本全 ASCII，要么写文件时带上 BOM：

```powershell
[System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true)))
```

同一个坑对源码文件也成立：**绝不要**用 `Get-Content` / `Set-Content` 往返 `.cs` 文件 —— 它会按 ANSI 解码，能把文件写成无效 UTF-8。本项目真的把一个带中文注释的 `.cs` 写坏过一次。

---

## 14. 设置页所有行挤成一行

**症状** —— 某次 CSS 改动之后，设置页所有行塌成一行。

**原因** —— 设置页复用了 `.card` 这个类名，而 `app.css` 里的 `.card` 是看板页的新闻卡片（`display:flex`）。

**处置** —— 设置页的分组容器必须是 `.settings-card`，且绝不能出现 `class="card"`。回归测试：`Sa2SettingsPageTests.Settings_UsesGroupedCardLayout_AndCancelIsBackLink`。当初是用 `HtmlRenderer` 导出真实 HTML，再用 Edge `--headless --dump-dom` 读计算样式定位到的。

---

## 15. WPF 宿主启动即崩：`Microsoft.Windows.SDK.NET`

**症状** —— WPF 宿主立刻退出，报 `FileNotFoundException: Microsoft.Windows.SDK.NET`。

**原因** —— 项目用了裸 `net10.0-windows` 目标框架。`BlazorWebView` 内部用 `WebView2CompositionControl`（D3D 合成），需要 CsWinRT 投影程序集，而只有带版本号的 TFM 才提供。

**处置** —— WPF 项目的目标框架用 `net10.0-windows10.0.19041.0`。

---

相关：[参与开发](contributing.zh-CN.md) · [Android 笔记](android.zh-CN.md) · [播放](playback.zh-CN.md) · [新闻流水线](news-pipeline.zh-CN.md) · [文档索引](index.zh-CN.md)
