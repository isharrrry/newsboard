# News pipeline

**English** | [中文](news-pipeline.zh-CN.md) | [Español](news-pipeline.es.md)

> Part of the NewsBoard documentation — back to [README](../README.md)

This page follows one news item from a source to the board: fetching, deduplication, article-body extraction, summarization, importance scoring, keyword classification, images, retention, and what happens when the playlist would be empty.

## Two channels

| Channel | Source class | What it returns | Needs an API key |
|---|---|---|---|
| RSS channel feed | `RssNewsSource` | title, link, published time, short description | only for summarization |
| Web search channel | `WebSearchNewsSource` | title, summary, url, importance, keyword — already summarized | yes |

`FetchMode` (`NewsFetchMode` in `AppConfig`) decides which one runs:

| `FetchMode` | Behaviour |
|---|---|
| `RssThenSummarize` | RSS only; each new item is summarized by the model afterwards |
| `WebSearch` | web search channel only |
| `Auto` (default) | web search first; if it returns nothing, fall back to RSS and warn `…，已回退 RSS` |

Before any request, `NewsFetchCoordinator.FetchAsync` checks `IsWithinAiCooldown`. Inside the cooldown window (`AiMinIntervalMinutes`, default 10) the round is skipped with the warning `距上次 AI 调用不足最小间隔，本轮跳过（下轮重新抓取）`.

## RSS channel feed

- `RssTemplate` default: `https://www.chinanews.com.cn/rss/scroll-news.xml`.
- If the template contains `{q}` it is a **search-type** feed: one request per keyword.
- If it does not contain `{q}` it is a **shared channel feed**: one request per round, taking `max(ItemsPerKeyword × keywords, 10)` latest items (`MinSharedItems = 10`). Each item's keyword label is the feed's channel title.
- On failure or empty result the fallbacks in `RssFallbackTemplates` are tried in order: `http://www.people.com.cn/rss/politics.xml`, then `https://www.ithome.com/rss/`.
- `OnlyTodayNews` (default `true`) keeps only today's items.

Measured 2026-10-08: there is **no usable keyword-search RSS on this machine** — Bing `?format=RSS` returns `text/html`, Baidu `newsrss` returns a security-check page and Google News RSS times out. Keyword search is therefore carried by the web search channel.

## Web search channel

- `POST {BaseUrlSearch}/v1/messages`, headers `x-api-key` and `anthropic-version: 2023-06-01`.
- Tool: `{"type":"web_search_20250305","name":"web_search","max_uses":N}` where `N = SearchMaxUses` (default 3).
- The response contains `server_tool_use` and `web_search_tool_result` blocks; only the text block is parsed.
- One request per keyword. The model must return strict JSON with `title` / `summary` / `url` / `importance` / `keyword`. Items without a url or summary are dropped, and each keyword keeps at most `ItemsPerKeyword` items.

## Deduplication and AI cost

- Items are deduplicated by a hash of the article URL (`NewsSummarizer.MakeId`); an item already in the cache is never sent to the model again.
- Measured: two consecutive rounds → the second round made **0 chat calls**.
- Fetching (every `FetchIntervalMinutes`, default 5) and summarization (new items only) are decoupled; `AiMinIntervalMinutes` is the backstop.

## Article body extraction

`ArticleContentFetcher` (no third-party packages, 5 s timeout, every exception swallowed into `null`).

- `NewsFetchCoordinator.BackfillRawContentAsync` runs on the new items of **both** channels: those whose `Description` is shorter than **300** characters, **at most 5 per round** (`MaxContentBackfill`), **concurrency 2** (`ContentConcurrency`).
- The extracted body is written back to `RawNewsItem.Description`, so the summarization input **and** the cached `RawContent` both use the real article body; the item is then re-classified with it.
- `ExtractMainText` returns `null` below **60** characters, so very short bulletins fall back to the RSS description.
- Everything after `(完)` / `（完）` is dropped, and tail lines without a period of up to 45 characters are treated as noise.
- Measured 2026-10-08: after a rebuild, 4 of 9 items had `RawContent` longer than 300 characters (302 / 620 / 676 / 868); a standalone probe against real sites succeeded **9/9** (chinanews 137–720 chars, ITHome 332/944 chars), no mojibake and no timeouts.

## Summarization

- `NewsSummarizer` posts to `POST {BaseUrl}/chat/completions` (OpenAI-compatible; `System.Text.Json`, no third-party SDK).
- One request per keyword, covering that keyword's whole batch of articles.
- Prompt constraints: use only the source text and never invent numbers, dates or names; each summary ≤ `SummaryMaxChars` (default **70**), written as 1–2 sentences, without restating the title; if the source is too thin the model must return an empty `summary` and the item is skipped.
- Failure path: timeout → one retry → still failing → degrade to the truncated original title. `MaxAttempts = 2`.
- Local second pass: `SummaryText.Normalize` truncates anything longer than **1.2 ×** `SummaryMaxChars` at the last period.
- `RawContent` is capped at `NewsSummarizer.MaxRawContentChars = 2000` characters.
- Measured with the default 70: RSS summarization 53–66 chars, web search 72–84 chars.
- `SummaryLanguage` writes `PromptRule()` into both prompts. Measured: before that rule, 6 of 9 summaries from Hong Kong/Taiwan sources contained 1–3 traditional characters; after it, 0 of 9.

## Importance scoring

- Both channels ask the model for `importance` between 0.00 and 1.00 **relative to the last three days**, with the instruction to spread the values (major 0.80–1.00, routine 0.20–0.50).
- `ImportanceScoring.Read` parses numbers and numeric strings, clamps out-of-range values and returns `null` for anything invalid.
- `ImportanceScoring.Balance` rebalances a batch when it is **flat** (`FlatSpread = 0.1`: spread < 0.1) or **piled up** (`PileUpRatio = 0.8`: ≥80 % at ≥ `HighPile = 0.85` or ≤ `LowPile = 0.15`) and the batch has at least `MinCountForRebalance = 3` items; it then spreads the values by rank over `[LowBand = 0.15, HighBand = 0.95]`.
- `NewsRepository.GetPlaylist` filters by `ImportanceThreshold` (**default 0.7**) *before* sorting. Items whose `Importance` is `null` (old cache, or the model failed) are always kept, so an upgrade can never empty the library.
- Measured 2026-10-08: a cache of 16 items was distributed `0.2-0.4: 4 / 0.4-0.6: 5 / 0.6-0.8: 4 / 0.8-1.0: 3`, and 6 items passed the 0.7 threshold.

## Keyword classification

A shared channel feed returns a whole channel, so its items originally carry only the channel name. Two mechanisms turn that into a real label:

1. `KeywordClassifier.Classify(title, content, keywords, fallback)` — the first configured keyword found in the **title**, then in the **content**, otherwise the fallback (usually the channel name).
2. The summarization prompt asks the model for a `keyword` copied verbatim from the configured keywords, and `KeywordClassifier.Match` whitelists it so a self-invented category can never appear. The model's answer wins over string matching.

Measured 2026-10-08 with the six keywords restored: 15 cached items distributed China 2 / Wuhan 3 / US 3 / Taiwan 3 / International 2 / AI 3, and the on-screen label showed `中国`.

**This is "no hit", not "a lost category".** With the keywords `武汉/AI` and the chinanews scroll feed, all 9 items of that day were about National Day, a Japanese nuclear submarine or A-shares: neither the titles nor the bodies contained either keyword, and the model returned an empty keyword, so the label fell back to the channel name `中新网即时新闻`. There is **no** "show only items that match a keyword" filter (see [known limitations](../README.md)).

## Images

Text + image mode only. `ImageResolver` tries four levels in order: `og:image` → `twitter:image` → `link[rel=image_src]` → **the first image inside the article body** (inside the body container, after a `<p>`, skipping logos, icons, placeholders and template assets older than 60 days — `StaleImageDays = 60`). Every candidate is validated with a `Range: bytes=0-2047` request and must really answer `image/*` (`MaxConcurrency = 3`).

Measured 2026-10-08: chinanews and people.com.cn article pages have **no** `og:image` or `twitter:image`, and the RSS has no `enclosure` or `media:*`, so meta-only extraction would never find an image; the chinanews image host `i2.chinanews.com.cn` is hotlink-protected and answers with HTML.

**One image belongs to one item only**: `ImageResolver.DedupeImages(items)` keeps the newest item by published time and clears the rest, and `ApplyImagesAsync(..., reservedImageUrls)` forbids reusing an image another item already shows. An item that cannot get an exclusive image is treated as "no image in the body" and shows none. Every round also re-deduplicates the whole cache.

In text + image mode each round additionally backfills images for cached items that still have none, **at most 8 per round** (`MaxImageBackfill`).

Measured: before the fix, 9/9 items had an image but with **3 duplicate groups** (5 / 4 / 4 items sharing one template image); after the fix, **5/9 items had an image and 0 duplicate groups**.

## Retention and empty-playlist fallback

- `RetentionWindow` buckets by **calendar day**: `RetentionDays = 3` (default) keeps today plus the two previous days, the window starting at the local midnight of the day before yesterday (`TimeZoneInfo.Local`). Morning and late evening therefore see the same calendar days, unlike a sliding `now − N×24h` window.
- The fetch path passes `RetentionWindow.RetentionSpan(now, config.RetentionDays)` to `PruneExpired`. Expired items are removed **together with their URL deduplication record**, so the same article can be fetched again later.
- If the importance threshold removes every cached item, `GetPlaylist` adds one random cached item as a **copy** (the cache itself is untouched) and appends `NewsRepository.BelowThresholdSuffix` (`" ~"`) to its summary.
- Measured: with the threshold temporarily raised to 0.95 the board displayed `…移送检察机关审查起诉。 ~`.

## Where the code lives

| Concern | File |
|---|---|
| Round orchestration, backfill limits | `src/NewsBoard.Shared/Services/NewsFetchCoordinator.cs` |
| RSS channel feed | `src/NewsBoard.Shared/Services/RssNewsSource.cs` |
| Web search channel | `src/NewsBoard.Shared/Services/WebSearchNewsSource.cs` |
| Summarization | `src/NewsBoard.Shared/Services/NewsSummarizer.cs` |
| Summary length and language | `src/NewsBoard.Shared/Services/SummaryText.cs`, `src/NewsBoard.Shared/Services/AppConfig.cs` |
| Importance | `src/NewsBoard.Shared/Services/ImportanceScoring.cs` |
| Article body | `src/NewsBoard.Shared/Services/ArticleContentFetcher.cs` |
| Images | `src/NewsBoard.Shared/Services/ImageResolver.cs` |
| Cache, playlist, retention | `src/NewsBoard.Shared/Services/NewsRepository.cs`, `src/NewsBoard.Shared/Services/RetentionWindow.cs` |
| Keyword label | `src/NewsBoard.Shared/Services/KeywordClassifier.cs` |
