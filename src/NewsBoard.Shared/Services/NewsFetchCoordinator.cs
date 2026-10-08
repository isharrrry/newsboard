using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>一轮抓取的结果摘要。</summary>
public sealed class FetchResult
{
    /// <summary>本轮来源返回的原始条目数。</summary>
    public int RawCount { get; init; }

    /// <summary>去重后新增入库的条目数。</summary>
    public int NewCount { get; init; }

    /// <summary>经 AI 提炼入库的条目数（联网搜索通道为 0）。</summary>
    public int SummarizedCount { get; init; }

    /// <summary>实际使用的来源通道。</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>降级/跳过原因，无则 null。</summary>
    public string? Warning { get; init; }
}

/// <summary>一轮抓取：并发取原文 → URL 去重 → 仅新条目提炼 → 入库 → 取图。</summary>
public sealed class NewsFetchCoordinator
{
    private const string RssChannel = "RSS";
    private const string WebSearchChannel = "模型联网搜索";
    private const int MaxConcurrency = 3;

    /// <summary>图文模式下每轮最多为多少条「缓存里还缺图」的旧条目补抓配图。</summary>
    private const int MaxImageBackfill = 8;

    /// <summary>每轮最多为多少条补抓原文正文（控制抓取频率）。</summary>
    private const int MaxContentBackfill = 5;

    /// <summary>描述短于这个长度就认为「还没拿到正文」，值得去原文页抓一次。</summary>
    private const int MinDescriptionChars = 300;

    private const int ContentConcurrency = 2;

    private readonly Dictionary<string, INewsSource> _sources = new(StringComparer.Ordinal);
    private readonly NewsSummarizer _summarizer;
    private readonly ImageResolver _imageResolver;
    private readonly ArticleContentFetcher? _contentFetcher;
    private readonly INewsRepository _repository;
    private readonly IConfigProvider _config;

    public NewsFetchCoordinator(
        IEnumerable<INewsSource> sources,
        NewsSummarizer summarizer,
        ImageResolver imageResolver,
        INewsRepository repository,
        IConfigProvider config,
        ArticleContentFetcher? contentFetcher = null)
    {
        foreach (var source in sources) _sources[source.Name] = source;

        _summarizer = summarizer;
        _imageResolver = imageResolver;
        _contentFetcher = contentFetcher;
        _repository = repository;
        _config = config;
    }

    public async Task<FetchResult> FetchAsync(CancellationToken cancellationToken = default)
    {
        var config = _config.Current;
        var now = DateTimeOffset.Now;

        // 冷却期内本轮不发请求（通道 A 的检索本身就是 AI 调用）
        if (_repository.IsWithinAiCooldown(config, now))
        {
            return new FetchResult
            {
                Channel = ChannelOf(config.FetchMode),
                Warning = "距上次 AI 调用不足最小间隔，本轮跳过（下轮重新抓取）",
            };
        }

        var keywords = config.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).ToList();
        var rssSource = FindSource(RssChannel);
        var webSource = FindSource(WebSearchChannel);

        var channel = ChannelOf(config.FetchMode);
        string? warning = null;
        var aiCalled = false;
        List<RawNewsItem> raw;

        if (config.FetchMode == NewsFetchMode.RssThenSummarize)
        {
            channel = RssChannel;
            if (rssSource is null) return Degraded(channel, "未注册「RSS」新闻来源，本轮无数据");
            raw = await FetchRssAsync(rssSource, keywords, config, cancellationToken).ConfigureAwait(false);
        }
        else if (config.FetchMode == NewsFetchMode.WebSearch)
        {
            channel = WebSearchChannel;
            if (webSource is null) return Degraded(channel, "未注册「模型联网搜索」新闻来源，本轮无数据");
            raw = await FetchAllAsync(webSource, keywords, config, cancellationToken).ConfigureAwait(false);
            aiCalled = true;
            if (raw.Count == 0) warning = "联网搜索未返回任何条目";
        }
        else
        {
            channel = WebSearchChannel;
            raw = [];
            if (webSource is not null)
            {
                try
                {
                    raw = await FetchAllAsync(webSource, keywords, config, cancellationToken).ConfigureAwait(false);
                    aiCalled = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warning = $"联网搜索失败：{ex.Message}";
                }
            }
            else
            {
                warning = "未注册「模型联网搜索」新闻来源";
            }

            if (raw.Count == 0)
            {
                if (rssSource is null) return Degraded(channel, $"{warning ?? "联网搜索未返回结果"}；且未注册「RSS」新闻来源");

                warning = $"{warning ?? "联网搜索未返回结果"}，已回退 RSS";
                channel = RssChannel;
                raw = await FetchRssAsync(rssSource, keywords, config, cancellationToken).ConfigureAwait(false);
            }
        }

        // 先清过期（保留天数来自配置，默认 3 天，按自然日分桶：今天 + 前两天）：清掉的 URL
        // 同时从去重集合移除，所以本轮就能重新抓回来，不会被永久挡住。
        _repository.PruneExpired(now, RetentionWindow.RetentionSpan(now, config.RetentionDays));

        // 共享频道源抓回整频道，条目只带频道名当分类；按配置的关键字重新归类，
        // 画面上那条标签才是「武汉 / AI」这种真正的分类（标题优先，描述兜底）。
        if (channel == RssChannel && !RssNewsSource.HasKeywordPlaceholder(config.RssTemplate))
        {
            foreach (var item in raw)
            {
                item.Keyword = KeywordClassifier.Classify(item.Title, item.Description, keywords, item.Keyword);
            }
        }

        var fresh = raw
            .Where(r => !string.IsNullOrWhiteSpace(r.Url) && !_repository.Contains(r.Url))
            .ToList();

        // 能抓到正文的尽量抓：RSS 的 description 常常只有几十字（甚至只是标题复述），
        // 联网搜索通道更是一个字都没有。抓到的正文既用来提炼（摘要质量的关键），也随条目一起存下来。
        await BackfillRawContentAsync(fresh, keywords, cancellationToken).ConfigureAwait(false);

        var newItems = new List<NewsItem>();
        var summarizedCount = 0;

        if (channel == WebSearchChannel)
        {
            newItems.AddRange(fresh
                .Where(r => !string.IsNullOrWhiteSpace(r.PreSummarized))
                .Select(r => new NewsItem
                {
                    Id = NewsSummarizer.MakeId(r.Url!.Trim()),
                    Title = r.Title ?? string.Empty,
                    Summary = r.PreSummarized!.Trim(),
                    Url = r.Url!.Trim(),
                    Keyword = r.Keyword,
                    Source = r.Source,
                    PublishedAt = r.PublishedAt ?? now,
                    FetchedAt = now,
                    RawContent = NewsSummarizer.TruncateRawContent(r.Description),
                    Importance = r.Importance,
                }));
            if (newItems.Count > 0) summarizedCount = 0;
        }
        else if (fresh.Count > 0)
        {
            // 同一关键字的原文一次请求提炼
            foreach (var group in fresh.GroupBy(r => r.Keyword, StringComparer.Ordinal))
            {
                var produced = await _summarizer
                    .SummarizeAsync(group.Key, [.. group], config, cancellationToken)
                    .ConfigureAwait(false);
                newItems.AddRange(produced);
            }

            aiCalled = true;
            summarizedCount = newItems.Count;
        }

        // 本轮两条通道的全部新条目一起做重要度均衡：模型常给出一整批雷同的分数，
        // 这里按秩拉开差距，免得门槛过滤后「要么全留、要么全丢」。
        ApplyBalancedImportance(newItems);

        if (aiCalled) _repository.MarkAiCalled(now);

        if (newItems.Count > 0)
        {
            _repository.AddOrUpdateRange(newItems);
            await _repository.SaveAsync(cancellationToken).ConfigureAwait(false);

            await ApplyImagesAsync(newItems, config, cancellationToken).ConfigureAwait(false);
            await _repository.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (config.PlaybackMode == PlaybackMode.TextAndImage)
        {
            // 本轮没新条目：也把缓存里还缺图的旧条目补上（切到图文模式后旧条目同样要出图）
            await ApplyImagesAsync([], config, cancellationToken).ConfigureAwait(false);
            await _repository.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // 本轮没有新条目也要落盘：上面的过期清理可能刚删掉了东西
            await _repository.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        return new FetchResult
        {
            RawCount = raw.Count,
            NewCount = newItems.Count,
            SummarizedCount = summarizedCount,
            Channel = channel,
            Warning = warning,
        };
    }

    /// <summary>对一轮的全部新条目做重要度均衡（纯本地计算，null 保持 null）。</summary>
    private static void ApplyBalancedImportance(List<NewsItem> items)
    {
        if (items.Count == 0) return;

        var balanced = ImportanceScoring.Balance([.. items.Select(i => i.Importance)]);
        for (var i = 0; i < items.Count; i++) items[i].Importance = balanced[i];
    }

    private INewsSource? FindSource(string name)
        => _sources.TryGetValue(name, out var source) ? source : null;

    /// <summary>
    /// 为描述太短的条目补抓原文正文（每轮最多 <see cref="MaxContentBackfill"/> 条、并发 2）。
    /// 抓到就把正文写回描述（提炼与入库都用它），并用正文重新归一次类 ——
    /// 标题里没写关键字的新闻，往往在正文里才看得出属于哪个分类。
    /// </summary>
    private async Task BackfillRawContentAsync(
        List<RawNewsItem> fresh,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        if (_contentFetcher is null) return;

        var targets = fresh
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Where(r => (r.Description?.Length ?? 0) < MinDescriptionChars)
            .Take(MaxContentBackfill)
            .ToList();

        if (targets.Count == 0) return;

        using var gate = new SemaphoreSlim(ContentConcurrency);
        var tasks = targets.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var text = await _contentFetcher.FetchAsync(item.Url!, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text)) return;

                item.Description = text;
                item.Keyword = KeywordClassifier.Classify(item.Title, text, keywords, item.Keyword);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 正文抓取失败不影响本轮抓取：条目照旧用 RSS 的描述走。
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 本轮配图：先把缓存里已确认的图去重（同一张图只归一条新闻），再给「新条目 + 缓存里缺图的旧条目」补抓，
    /// 且新抓的图不得复用在挂的图 —— 拿不到独占配图的条目按「正文没图」处理：宁可不出图，也不出重复图。
    /// </summary>
    private async Task ApplyImagesAsync(
        IReadOnlyList<NewsItem> newItems,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        if (config.PlaybackMode != PlaybackMode.TextAndImage) return;

        // Snapshot 返回的是同批条目引用，去重会直接改到缓存里的对象上（正是要的效果：清掉历史重复图）。
        var all = _repository is NewsRepository repository ? repository.Snapshot() : _repository.GetPlaylist(config);
        ImageResolver.DedupeImages(all);

        var reserved = all
            .Where(i => !string.IsNullOrWhiteSpace(i.ImageUrl))
            .Select(i => i.ImageUrl!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targets = WithCachedMissingImages(newItems, config);
        if (targets.Count == 0) return;

        await _imageResolver.ApplyImagesAsync(targets, config, cancellationToken, reserved).ConfigureAwait(false);
    }

    /// <summary>本轮新条目 + 缓存里还缺图的旧条目（每次最多 <see cref="MaxImageBackfill"/> 条）。</summary>
    private List<NewsItem> WithCachedMissingImages(IReadOnlyList<NewsItem> newItems, AppConfig config)
    {
        if (config.PlaybackMode != PlaybackMode.TextAndImage)
        {
            return [.. newItems];
        }

        var result = new List<NewsItem>(newItems);
        var seen = new HashSet<string>(newItems.Select(i => i.Id), StringComparer.Ordinal);
        var limit = newItems.Count + MaxImageBackfill;

        foreach (var item in _repository.GetPlaylist(config))
        {
            if (result.Count >= limit) break;
            if (!string.IsNullOrWhiteSpace(item.ImageUrl)) continue;
            if (string.IsNullOrWhiteSpace(item.Url)) continue;
            if (!seen.Add(item.Id)) continue;

            result.Add(item);
        }

        return result;
    }

    private static string ChannelOf(NewsFetchMode mode) => mode switch
    {
        NewsFetchMode.WebSearch => WebSearchChannel,
        NewsFetchMode.RssThenSummarize => RssChannel,
        _ => WebSearchChannel,
    };

    private static FetchResult Degraded(string channel, string warning)
        => new() { Channel = channel, Warning = warning };

    /// <summary>
    /// 抓 RSS：模板含 {q} 的搜索型源按关键字并发各抓一次；
    /// 不含 {q} 的共享频道源整轮只抓一次，避免按关键字重复请求同一频道。
    /// </summary>
    private static async Task<List<RawNewsItem>> FetchRssAsync(
        INewsSource source,
        IReadOnlyList<string> keywords,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        if (source is ISharedChannelNewsSource shared && !RssNewsSource.HasKeywordPlaceholder(config.RssTemplate))
        {
            var sharedItems = await shared.FetchSharedAsync(config, cancellationToken).ConfigureAwait(false);
            return [.. sharedItems];
        }

        return await FetchAllAsync(source, keywords, config, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<RawNewsItem>> FetchAllAsync(
        INewsSource source,
        IReadOnlyList<string> keywords,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        var results = new List<RawNewsItem>();
        using var gate = new SemaphoreSlim(MaxConcurrency);

        var tasks = keywords.Select(async keyword =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var items = await source.FetchAsync(keyword, config, cancellationToken).ConfigureAwait(false);
                lock (results) results.AddRange(items);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
