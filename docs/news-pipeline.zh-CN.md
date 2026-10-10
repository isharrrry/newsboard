# 新闻流水线

[English](news-pipeline.md) | **中文** | [Español](news-pipeline.es.md)

> NewsBoard 文档的一部分 —— 返回 [README](../README.zh-CN.md)

这一页跟着一条新闻从来源走到看板：抓取、去重、正文提取、摘要提炼、重要度打分、分类关键字、配图、保留窗口，以及播放列表会被筛空时怎么办。

## 两条通道

| 通道 | 实现类 | 返回什么 | 需要 API Key |
|---|---|---|---|
| RSS 频道源 | `RssNewsSource` | 标题、链接、发布时间、短描述 | 只在提炼时需要 |
| 联网搜索通道 | `WebSearchNewsSource` | 标题、摘要、链接、重要度、分类关键字（已是提炼结果） | 需要 |

跑哪一条由 `FetchMode`（`AppConfig` 里的 `NewsFetchMode`）决定：

| `FetchMode` | 行为 |
|---|---|
| `RssThenSummarize` | 只走 RSS，新条目事后交给模型提炼 |
| `WebSearch` | 只走联网搜索通道 |
| `Auto`（默认） | 先联网搜索；一条都没返回就回退 RSS，并给出警告 `…，已回退 RSS` |

任何请求之前，`NewsFetchCoordinator.FetchAsync` 先查 `IsWithinAiCooldown`。处在冷却窗口内（`AiMinIntervalMinutes`，默认 10 分钟）时本轮直接跳过，警告文本是 `距上次 AI 调用不足最小间隔，本轮跳过（下轮重新抓取）`。

## RSS 频道源

- `RssTemplate` 默认值：`https://www.chinanews.com.cn/rss/scroll-news.xml`。
- 模板**含 `{q}`** ⇒ 搜索型源，每个关键字各发一次请求。
- 模板**不含 `{q}`** ⇒ 共享频道源，一轮只发一次请求，取最新 `max(ItemsPerKeyword × 关键字数, 10)` 条（`MinSharedItems = 10`）。每条的关键字标签取 feed 的频道标题。
- 主源失败或为空时，按顺序回退 `RssFallbackTemplates`：`http://www.people.com.cn/rss/politics.xml`、`https://www.ithome.com/rss/`。
- `OnlyTodayNews`（默认 `true`）只保留当天的条目。

实测（2026-10-08）：本机**没有任何可用的「关键字搜索」RSS** —— Bing `?format=RSS` 返回 `text/html`、百度 `newsrss` 返回安全验证页、Google News RSS 超时。关键字检索因此由联网搜索通道承担。

## 联网搜索通道

- `POST {BaseUrlSearch}/v1/messages`，请求头 `x-api-key` 与 `anthropic-version: 2023-06-01`。
- 工具：`{"type":"web_search_20250305","name":"web_search","max_uses":N}`，其中 `N = SearchMaxUses`（默认 3）。
- 响应同时含 `server_tool_use` 与 `web_search_tool_result` 块，只解析 text 块。
- 每个关键字一次请求。模型必须返回严格 JSON，字段为 `title` / `summary` / `url` / `importance` / `keyword`；缺 url 或缺摘要的条目被丢弃，每个关键字最多保留 `ItemsPerKeyword` 条。

## 去重与 AI 成本

- 按文章 URL 的哈希去重（`NewsSummarizer.MakeId`）；已在缓存里的条目**永不再送模型**。
- 实测：连跑两轮，第二轮 **chat 调用 0 次**。
- 抓取（每 `FetchIntervalMinutes`，默认 5 分钟）与提炼（只处理新条目）解耦；`AiMinIntervalMinutes` 是兜底闸门。

## 正文抓取

`ArticleContentFetcher`（无第三方包、5 秒超时、所有异常一律吞成 `null`）。

- `NewsFetchCoordinator.BackfillRawContentAsync` 对本轮**两条通道**的新条目都生效：挑 `Description` 短于 **300** 字的，**每轮最多 5 条**（`MaxContentBackfill`）、**并发 2**（`ContentConcurrency`）。
- 抓到的正文**写回 `RawNewsItem.Description`**，因此提炼输入**和**缓存里的 `RawContent` 用的都是真正文；随后还会用正文重新归一次类。
- `ExtractMainText` 低于 **60** 字一律返回 `null`，极短快讯因此退回 RSS 描述。
- `(完)` / `（完）` 之后的内容整段丢弃；尾部无句号且不超过 45 字的行按噪声处理。
- 实测（2026-10-08）：重建后跑一轮，9 条里 4 条 `RawContent` 超过 300 字（302 / 620 / 676 / 868）；单独对真实站点探针 **9/9 成功**（中新网 137–720 字、IT之家 332/944 字），无乱码、无超时。

## 摘要提炼

- `NewsSummarizer` 打 `POST {BaseUrl}/chat/completions`（OpenAI 兼容；`System.Text.Json`，不引第三方 SDK）。
- 同一关键字的整批原文一次请求。
- 提示词硬约束：只依据原文、严禁编造数字/时间/人名；每条 ≤ `SummaryMaxChars`（默认 **70**）字、写 1–2 句、不复述标题；信息不足时必须把 `summary` 留空，客户端跳过该条。
- 失败路径：超时 → 重试 1 次 → 仍失败则退化为「原标题截断」。`MaxAttempts = 2`。
- 本地二次校验：`SummaryText.Normalize` 把超过 **1.2 倍** `SummaryMaxChars` 的文本截到最后一个句号。
- `RawContent` 上限是 `NewsSummarizer.MaxRawContentChars = 2000` 字。
- 默认 70 字下实测：RSS 提炼 53–66 字，联网搜索 72–84 字。
- `SummaryLanguage` 会把 `PromptRule()` 写进两条通道的提示词。实测：加这条之前，港台源摘要 9 篇里有 6 篇夹带 1–3 个繁体字；加之后同样复测 **9 篇全 0**。

## 重要度打分

- 两条通道都要求模型给出 `importance`（0.00–1.00，**相对于最近三天**），并要求拉开差距（重大 0.80–1.00、例行 0.20–0.50）。
- `ImportanceScoring.Read` 容错解析数字与数字字符串、越界夹取、非法值返回 `null`。
- `ImportanceScoring.Balance` 在**整批退化**时重排：极差小于 `FlatSpread = 0.1`，或 ≥80%（`PileUpRatio = 0.8`）堆在 ≥ `HighPile = 0.85` / ≤ `LowPile = 0.15`，且条数 ≥ `MinCountForRebalance = 3`；重排按秩在 `[LowBand = 0.15, HighBand = 0.95]` 上展开。
- `NewsRepository.GetPlaylist` **先按 `ImportanceThreshold`（默认 0.7）过滤，再排序**。`Importance` 为 `null` 的条目（旧缓存、或模型失败）一律保留，升级后不可能把库清空。
- 实测（2026-10-08）：缓存 16 条分布 `0.2-0.4: 4 / 0.4-0.6: 5 / 0.6-0.8: 4 / 0.8-1.0: 3`，过 0.7 门槛显示 6 条。

## 分类关键字

共享频道源抓回的是整个频道，条目原本只带频道名。两道处理把它变成真正的分类标签：

1. `KeywordClassifier.Classify(title, content, keywords, fallback)` —— 按配置顺序在**标题**里找第一个命中项，找不到再找**正文**，都没有才用 fallback（通常是频道名）。
2. 提炼提示词要求模型输出 `keyword`（从配置关键字里原样照抄），`KeywordClassifier.Match` 做白名单校验，自造分类不可能出现。模型给的结果优先于字符串匹配。

恢复 6 个关键字后的实测（2026-10-08）：缓存 15 条按关键字分布 中国 2 / 武汉 3 / 美国 3 / 台湾 3 / 国际 2 / AI 3，画面标签显示 `中国`。

**这是「没有命中」，不是「分类丢失」。** 关键字为 `武汉/AI`、源为中新网滚动新闻频道时，当天 9 条全是国庆、日本核潜艇、A 股这类新闻：标题与正文都不含这两个关键字，模型也留空，标签于是回落成频道名 `中新网即时新闻`。目前**没有**「只显示命中关键字的新闻」过滤开关（见 [已知限制](../README.zh-CN.md)）。

## 配图

仅图文模式。`ImageResolver` 按四级取图：`og:image` → `twitter:image` → `link[rel=image_src]` → **正文首图**（正文容器内、`<p>` 之后，跳过 logo、图标、占位图以及早于文章日期 60 天以上的模板素材，`StaleImageDays = 60`）。每个候选都发一次 `Range: bytes=0-2047` 校验，必须真的返回 `image/*`（`MaxConcurrency = 3`）。

实测（2026-10-08）：中新网与人民网文章页**都没有** `og:image` 或 `twitter:image`，RSS 也没有 `enclosure` 或 `media:*`，所以只看 meta 永远取不到图；中新网图床 `i2.chinanews.com.cn` 有防盗链，直接取回的是 HTML。

**同一张图只能属于一条新闻**：`ImageResolver.DedupeImages(items)` 按发布时间从新到旧保留第一条、其余清空；`ApplyImagesAsync(..., reservedImageUrls)` 禁止复用别的条已显示的图。拿不到独占图的条目按「正文没图」处理，**不出图**。每轮还会对全量缓存再统一去一次重。

图文模式下每轮还会为「缓存里已存在但没图」的旧条目补抓配图，**每轮最多 8 条**（`MaxImageBackfill`）。

实测：修复前 9/9 有图但出现 **3 组重复**（5 条 / 4 条 / 4 条各共用一张模板图）；修复后 **5/9 有图、重复组 0**。

## 保留窗口与空列表兜底

- `RetentionWindow` 按**自然日**分桶：`RetentionDays = 3`（默认）保留今天加前两天，窗口起点是前天的本地零点（`TimeZoneInfo.Local`）。因此早上与深夜看到的是同一批自然日，这与滑动的 `now − N×24h` 窗口不同。
- 抓取侧把 `RetentionWindow.RetentionSpan(now, config.RetentionDays)` 传给 `PruneExpired`。过期条目会**连同它的 URL 去重记录一起**删掉，所以同一篇文章以后可以重新抓回来。
- 门槛把缓存条目全筛掉时，`GetPlaylist` 随机补一条缓存条目的**副本**（不污染缓存本体），并在摘要末尾追加 `NewsRepository.BelowThresholdSuffix`（`" ~"`）。
- 实测：把门槛临时调到 0.95 后，画面显示 `…移送检察机关审查起诉。 ~`。

## 代码在哪

| 关注点 | 文件 |
|---|---|
| 一轮抓取的编排与补抓上限 | `src/NewsBoard.Shared/Services/NewsFetchCoordinator.cs` |
| RSS 频道源 | `src/NewsBoard.Shared/Services/RssNewsSource.cs` |
| 联网搜索通道 | `src/NewsBoard.Shared/Services/WebSearchNewsSource.cs` |
| 摘要提炼 | `src/NewsBoard.Shared/Services/NewsSummarizer.cs` |
| 摘要长度与语言 | `src/NewsBoard.Shared/Services/SummaryText.cs`、`src/NewsBoard.Shared/Services/AppConfig.cs` |
| 重要度 | `src/NewsBoard.Shared/Services/ImportanceScoring.cs` |
| 正文 | `src/NewsBoard.Shared/Services/ArticleContentFetcher.cs` |
| 配图 | `src/NewsBoard.Shared/Services/ImageResolver.cs` |
| 缓存、播放列表、保留窗口 | `src/NewsBoard.Shared/Services/NewsRepository.cs`、`src/NewsBoard.Shared/Services/RetentionWindow.cs` |
| 分类标签 | `src/NewsBoard.Shared/Services/KeywordClassifier.cs` |
