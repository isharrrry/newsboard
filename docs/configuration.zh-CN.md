# 配置与数据

[English](configuration.md) | **中文** | [Español](configuration.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

## 文件在哪

NewsBoard 是**跨平台**的：**一套 Razor 组件**（`NewsBoard.Shared`，零平台依赖）跑在**三个宿主**之下 —— Android（`NewsBoard.Maui`）、`NewsBoard.Wpf`，以及 `NewsBoard.Maui` 的 Windows 头。各宿主把自己的配置、缓存与 API Key 放在各自的位置：

| 平台 / 宿主 | 配置文件 | 新闻缓存 | API Key |
|---|---|---|---|
| Android —— `NewsBoard.Maui` | app 私有目录（`LocalApplicationData`，包名 `com.newsboard.app`） | 同左 | MAUI `SecureStorage`（Android Keystore，不是明文文件） |
| Windows —— `NewsBoard.Wpf` | `%LOCALAPPDATA%\NewsBoard\config.json` | `%LOCALAPPDATA%\NewsBoard\news.json` | `%APPDATA%\NewsBoard\secret.dat`（DPAPI `CurrentUser` + entropy `NewsBoard.SecretStore.v1`；磁盘上是密文） |
| Windows —— `NewsBoard.Maui`（Windows 头） | 与 `NewsBoard.Wpf` 同一个文件 —— 两个 Windows 宿主完全共用 | 同一个文件 | 同一个文件（两端密钥互通） |

- **改数据目录**：设置环境变量 `NEWSBOARD_DATA_DIR`（非空即生效，三个宿主通用）。`ConfigStore` 与 `NewsRepository` 共用这个唯一的解析点。自动化或探针脚本**必须**把它指向隔离目录、绝不指向真实目录 —— 有个探针脚本曾把用户的 6 个关键字覆盖成 2 个（见 `plans/news-board.plan.md` §13）。
- **误改可回滚**：每次覆盖写盘前，`ConfigStore` 会把上一版留成 `config.json.bak`。
- **`ApiKey` 永不进 JSON**：该字段带 `[JsonIgnore]`，由平台安全存储在 `ConfigStore.ApiKeySecretName`（`deepseek-api-key`）名下注入。
- **Key 怎么进各宿主**：两个 Windows 宿主共用 `%APPDATA%\NewsBoard\secret.dat`（DPAPI 按当前用户加密 + 上面的 entropy），所以填一次两边都能用。该文件绑机器，拷到手机没有用 —— Android 上要在设备本身的「设置 → API / 采集」里重新粘贴一次 DeepSeek API Key，它会存进 Android Keystore。
- **开机自启**：两个 Windows 宿主写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`；Android 用 `BootReceiver` 加带常驻通知的前台服务，厂商自启动管理（如 MIUI）仍可能拦截 —— 见 [Android](android.zh-CN.md)。

## 设置页

`Settings.razor` 按 iOS 分组列表排版（`.settings-card`），顶部栏与底部操作条用 `position:fixed` 贴真实视口。

| 分组 | 里面有什么 |
|---|---|
| **显示模式** | 播放模式（纯文字 / 文字+图片）与派生的「显示新闻图片」开关 —— 同一个开关的两种呈现 |
| **API / 采集** | 获取方式、BaseUrl / Model / 联网搜索端点与 max_uses、API Key、关键字列表、每关键字篇数、抓取间隔、摘要字数、摘要语言、AI 最小调用间隔、RSS 源与备用源、只用当天新闻、重要度门槛、保留天数 |
| **外观** | 背景 / 前景 / 强调色、正文字号与字重、行距、内边距、关键字标签字号、对齐、显示时钟、时钟字号与字体、显示关键字 / 标题 / 来源 |
| **播放 / 语音** | 语音播报开关、播报内容、语言、语速、音调、播报前静默、静音时段、TTS 不可用时策略、亮屏时长、单条基础停留、滚动速度、黑屏时长、亮熄循环、黑屏方式、窗口到点行为、图片位置与占屏比例、轮播顺序、跨轮续播 |
| **系统 / 宿主** | 保持屏幕常亮（Android）、开机自启（三个宿主）、仅 WiFi 抓取、Windows 无边框全屏（WPF）、Android 显示系统状态栏 |

底部操作条：**保存 / 取消 / 恢复默认**。「恢复默认」只重置本页正在编辑的值 —— 不会清除已保存的 API Key —— 且不点「保存」就不生效。开机自启那一行通过 `IAutoStart` 读平台真实状态；本组还有「黑屏 2 秒」自检按钮，自检不改配置。

控件 id 与字段名一一对应（camelCase）：`playbackMode`、`showImages`、`summaryLanguage`、`summaryMaxChars`、`importanceThreshold`、`retentionDays`、`clockFontSize`、`clockFontFamily`、`speechEnabled`、`showDuration`、`blackoutDuration`、`winFullscreen` 等。

## 配置项表

字段名取自 `src/NewsBoard.Shared/Services/AppConfig.cs` 的标识符，默认值就是该文件的默认值。`plans/news-board.plan.md` §4 用同样的四个分组列出同一批条目。

### API / 采集

| 字段 | 默认值 | 含义 |
|---|---|---|
| `FetchMode` | `Auto` | `RssThenSummarize` / `WebSearch` / `Auto`。`Auto` 先试联网搜索通道，失败回退 RSS |
| `BaseUrl` | `https://api.deepseek.com` | OpenAI 兼容端点，RSS 提炼通道用（`POST /chat/completions`） |
| `Model` | `deepseek-chat` | 模型名，两条通道共用（后端实际回显 `deepseek-v4-flash`） |
| `BaseUrlSearch` | `https://api.deepseek.com/anthropic` | Anthropic 兼容端点，联网搜索通道用（`POST /v1/messages` + `web_search` 工具） |
| `SearchMaxUses` | `3` | 每个关键字允许模型调用搜索工具的次数 |
| `ApiKey` | 空 | 只存在平台安全存储里，永不写进 JSON |
| `Keywords` | `武汉`、`中国`、`美国`、`台湾`、`国际`、`AI` | 关键字列表；顺序即 `PlayOrder.ByKeywordOrder` 用的顺序，保存时自动去空、去重、去首尾空格 |
| `ItemsPerKeyword` | `3` | 每个关键字各取几篇 |
| `FetchIntervalMinutes` | `5` | 定时抓取周期 |
| `SummaryMaxChars` | `70` | 提示词软约束（写到 55–70 字最好）；两条通道还要过本地 `SummaryText.Normalize`：超过 1.2 倍（84 字）就截到最后一个句号 —— 实测模型会超字数 |
| `SummaryLanguage` | `SimplifiedChinese` | `SimplifiedChinese` / `TraditionalChinese` / `English`；作为硬性要求写进两条通道的提示词 |
| `AiMinIntervalMinutes` | `10` | AI 最小调用间隔，防止源站抖动重复烧 token |
| `RssTemplate` | `https://www.chinanews.com.cn/rss/scroll-news.xml` | 模板含 `{q}` ⇒ 按关键字各抓一次；不含 ⇒ 共享频道源，总共只抓一次 |
| `RssFallbackTemplates` | `http://www.people.com.cn/rss/politics.xml`、`https://www.ithome.com/rss/` | 主源失败时按顺序依次尝试 |
| `OnlyTodayNews` | `true` | 丢弃发布超过 24 小时的条目（没有发布时间的条目保留） |
| `ImportanceThreshold` | `0.7` | 只播放模型评分 ≥ 此值的条目 |
| `RetentionDays` | `3` | 缓存保留天数，按自然日计 |

### 外观

| 字段 | 默认值 | 含义 |
|---|---|---|
| `BackgroundColor` | `#000000` | 看板背景色 |
| `ForegroundColor` | `#FFFFFF` | 正文颜色 |
| `AccentColor` | `#00D4AA` | 关键字标签与时钟 |
| `FontSizeSp` | `56` | 正文字号 |
| `FontWeight` | `Bold` | `Normal` / `Bold` |
| `LineHeight` | `1.35` | 行距倍数 |
| `PagePaddingPx` | `48` | 页面内边距 |
| `KeywordLabelFontSizeSp` | `28` | 关键字标签字号 |
| `TextAlign` | `Center` | `Left` / `Center` / `Right` |
| `ShowClock` | `true` | 显示时钟 |
| `ClockFontSizeSp` | `32` | 时钟字号；不再由关键字标签字号推导 |
| `ClockFontFamily` | 空 | 空 = 跟随系统默认字体 |
| `ShowKeywordLabel` | `true` | 显示关键字标签 |
| `ShowTitle` | `false` | 默认关：标题与摘要内容常高度重合，同屏显示等于重复一遍 |
| `ShowSource` | `false` | 显示新闻来源 |

### 播放 / 语音

| 字段 | 默认值 | 含义 |
|---|---|---|
| `PlaybackMode` | `TextOnly` | `TextOnly` / `TextAndImage`。设置页把它放在第一组「显示模式」里，与派生的 `showImages` 开关同源 |
| `SpeechEnabled` | `true` | 关掉则回退到固定的单条基础停留 |
| `SpeechContent` | `SummaryOnly` | `SummaryOnly` / `TitleAndSummary` |
| `SpeechRate` | `1.0` | 0.5–2.0 |
| `SpeechPitch` | `1.0` | 0.5–2.0 |
| `SpeechLanguage` | `zh-CN` | 走系统 TTS 语音包 |
| `SpeechLeadSeconds` | `1` | 切到该条后先静默这么久再开口 |
| `QuietHours` | 空 | 形如 `23:00-07:00`，支持跨夜；该时段内只显示不朗读，留空 = 全天可播 |
| `TtsUnavailable` | `FallbackHold` | `FallbackHold`（回退固定停留）/ `SkipItem`（跳过该条） |
| `ShowDurationSeconds` | `60` | 一轮展示窗口长度 |
| `ItemHoldSeconds` | `12` | 仅语音关闭时生效 |
| `ScrollSpeedPxPerSecond` | `60` | 仅语音关闭时生效；语音开启时滚动速度跟随朗读时长 |
| `BlackoutDurationSeconds` | `300` | 黑屏持续多久 |
| `BlackoutCycleEnabled` | `true` | 关掉则一直亮屏；手动「打断」仍会灭一次 |
| `BlackoutMode` | `BrightnessZeroAndBlack` | `BrightnessZeroAndBlack` / `BlackOnly`（保留背光） |
| `WindowEndBehavior` | `FinishCurrentItem` | `FinishCurrentItem` / `InterruptImmediately` |
| `ImagePosition` | `TopImageBottomText` | `TopImageBottomText` / `LeftImageRightText` |
| `ImageScreenPercent` | `40` | 图文模式下图片占屏比例 |
| `PlayOrder` | `Random` | `ByKeywordOrder` / `ByPublishTime` / `Random` |
| `ResumeAcrossRounds` | `true` | 被窗口打断的条目下一轮接着播 |

### 系统 / 宿主

| 字段 | 默认值 | 含义 |
|---|---|---|
| `KeepScreenOn` | `true` | 仅 Android 生效：用 `KEEP_SCREEN_ON` 窗口标志实现，黑屏期间也不撤。两个 Windows 宿主都实现为空操作 |
| `AutoStart` | `true` | 开机自启，三个宿主都支持：Windows 宿主写 `HKCU\…\Run` 键，Android 起 `BootReceiver` 与前台服务；见 `plans/news-board.plan.md` §3.4 |
| `WifiOnlyFetch` | `false` | 关 = 允许移动数据抓取 |
| `WindowsBorderlessFullscreen` | `false` | 关 = 带标题栏的普通窗口；可在设置页打开。在 `NewsBoard.Wpf` 生效（F11 进 / Esc 退）；`NewsBoard.Maui` 的 Windows 头把 `SetFullscreenAsync` 实现为空操作，所以该项在那里没有效果 |
| `AndroidShowStatusBar` | `true` | 仅 Android 生效：开 = 时钟 / 电量可见；关 = 沉浸式全屏 |

## 几个容易弄错的字段

- **`ImportanceThreshold`（0.7）** —— 重要度低于它的条目在**排序之前**就被过滤掉。`Importance == null` 的条目（旧缓存，或 AI 调用失败）一律保留，所以升级不会把整库清空。如果过滤后缓存为空，`GetPlaylist` 会随机补回一条**副本**，并在摘要末尾加 ` ~`：那条其实没达标。随机顺序的缓存键包含门槛，所以改门槛立即重排。
- **`RetentionDays`（3）** —— 这是**按自然日分桶**，不是滑动窗口。`3` 表示保留「今天 + 前两天」共 3 个自然日，窗口起点是最早保留那天的本地零点（`RetentionWindow.EarliestKeptInstant`）。实际差别：早上 6 点与晚上 23 点看到的是同一个桶边界，而不是其中一头把第三天切掉一半。
- **`SummaryLanguage`** —— 默认简体中文，同一个值既决定呈现也决定朗读。切换它**不会**重写缓存里已有的摘要：清掉 `news.json`，或等自然日过期。实测：加提示词约束之前，联网搜索通道检索港台源时 9 篇里有 6 篇夹带繁体字；加之后 9 篇全部为 0。
- **`PlayOrder`（`Random`）** —— 顺序在门槛过滤之后应用。`ByKeywordOrder` 按关键字列表顺序，`ByPublishTime` 最新的在前，`Random` 打散一次后复用，直到缓存或门槛变化。
- **`OnlyTodayNews` 与 `RetentionDays`** —— 前者在抓取时过滤（超过 24 小时的条目），后者按自然日清理缓存。两个默认都开，各干各的活。
- **用脚本改配置** —— 先设 `NEWSBOARD_DATA_DIR` 指向隔离目录；万一写坏了，`config.json.bak` 里是上一版。
