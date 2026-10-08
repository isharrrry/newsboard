using System.Text.Json;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// SA9：大模型筛选（重要度评分）+ 三天缓存 + 摘要语言。
/// 覆盖：本地均衡纯函数、两条通道的提示词语言/评分约束、重要度解析、
/// 门槛过滤与随机顺序稳定、按配置天数清理与其后可重新抓取、第二轮同 URL 不再调 AI。
/// </summary>
public class Sa9ImportanceTests
{
    // ---------------- 本地均衡 ----------------

    [Fact]
    public void 整批同分_按秩展开并拉开差距()
    {
        var balanced = ImportanceScoring.Balance([0.9, 0.9, 0.9, 0.9]);

        var values = balanced.Select(v => v!.Value).ToList();
        Assert.Equal(4, values.Distinct().Count());
        Assert.Equal(ImportanceScoring.LowBand, values.Min());
        Assert.Equal(ImportanceScoring.HighBand, values.Max());
        Assert.All(values, v => Assert.InRange(v, ImportanceScoring.LowBand, ImportanceScoring.HighBand));
    }

    [Fact]
    public void 极差小于阈值_视作退化并展开()
    {
        var balanced = ImportanceScoring.Balance([0.5, 0.52, 0.48]);

        var values = balanced.Select(v => v!.Value).ToList();
        Assert.True(values.Max() - values.Min() >= 0.7, $"应被拉开，实际极差 {values.Max() - values.Min():0.##}");
    }

    [Fact]
    public void 八成都堆在高分端_视作退化并展开()
    {
        // 5 条里 4 条 ≥0.85（80%）→ 堆在高分端
        var balanced = ImportanceScoring.Balance([0.9, 0.88, 0.92, 0.95, 0.3]);

        var values = balanced.Select(v => v!.Value).ToList();
        Assert.True(values.Count(v => v >= ImportanceScoring.HighPile) < 5 * ImportanceScoring.PileUpRatio);
        Assert.Equal(5, values.Distinct().Count());
    }

    [Fact]
    public void 八成都堆在低分端_视作退化并展开()
    {
        var balanced = ImportanceScoring.Balance([0.1, 0.12, 0.05, 0.08, 0.7]);

        var values = balanced.Select(v => v!.Value).ToList();
        Assert.Equal(5, values.Distinct().Count());
        Assert.Equal(ImportanceScoring.HighBand, values.Max());
    }

    [Fact]
    public void 分散良好_原样保留模型判断()
    {
        var balanced = ImportanceScoring.Balance([0.2, 0.5, 0.8]);

        Assert.Equal([0.2, 0.5, 0.8], balanced.Select(v => v!.Value));
    }

    [Fact]
    public void 未评分的null保留_且不参与排名()
    {
        var balanced = ImportanceScoring.Balance([null, 0.9, 0.9, 0.9]);

        Assert.Null(balanced[0]);
        var values = balanced.Skip(1).Select(v => v!.Value).ToList();
        Assert.Equal(3, values.Distinct().Count());
        Assert.All(values, v => Assert.InRange(v, ImportanceScoring.LowBand, ImportanceScoring.HighBand));
    }

    [Fact]
    public void 越界值夹到0到1之间()
    {
        var balanced = ImportanceScoring.Balance([1.5, -0.3, 0.5]);

        Assert.Equal(1.0, balanced[0]);
        Assert.Equal(0.0, balanced[1]);
        Assert.Equal(0.5, balanced[2]);
    }

    [Fact]
    public void 只有两条且退化_不强行制造高分()
    {
        var balanced = ImportanceScoring.Balance([0.9, 0.9]);

        Assert.Equal([0.9, 0.9], balanced.Select(v => v!.Value));
    }

    [Fact]
    public void 并列同分_取相同分数()
    {
        // 极差 0.02 (<0.1) → 退化；4 条 0.9 并列，1 条 0.92 略高
        var balanced = ImportanceScoring.Balance([0.9, 0.9, 0.9, 0.9, 0.92]);
        var values = balanced.Select(v => v!.Value).ToList();

        Assert.Equal(values[0], values[1]);
        Assert.Equal(values[0], values[3]);
        Assert.True(values[4] > values[0], "略高的那条要排在前面");
        Assert.True(values[0] > 0, "并列组拿到组内平均秩，不应掉到 0");
    }

    [Fact]
    public void 全null或空列表_原样返回()
    {
        Assert.Empty(ImportanceScoring.Balance([]));
        Assert.Equal([null, null], ImportanceScoring.Balance([null, null]));
    }

    [Theory]
    [InlineData("0.82", 0.82)]
    [InlineData("\"0.82\"", 0.82)]     // 字符串数字
    [InlineData("1.4", 1.0)]           // 越界 → 夹取
    [InlineData("-0.2", 0.0)]          // 越界 → 夹取
    [InlineData("\"高\"", null)]        // 非数字 → null
    [InlineData("null", null)]         // 显式 null → null
    public void 重要度解析_合法非法与越界(string rawJson, double? expected)
    {
        using var document = JsonDocument.Parse($"{{\"importance\":{rawJson}}}");

        Assert.Equal(expected, ImportanceScoring.Read(document.RootElement));
    }

    [Fact]
    public void 重要度解析_缺字段返回null()
    {
        using var document = JsonDocument.Parse("{\"url\":\"https://example.com/a\"}");

        Assert.Null(ImportanceScoring.Read(document.RootElement));
    }

    // ---------------- 提示词：语言约束 + 重要度要求 ----------------

    [Fact]
    public async Task 提炼通道提示词_含简体约束与重要度评分要求()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Chat(("https://example.com/a", "摘要。", 0.8))));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var config = new AppConfig { ApiKey = "test-key" };

        await summarizer.SummarizeAsync("武汉", [Sa1Data.Raw("https://example.com/a", "标题")], config);

        var prompt = ChatPrompt(handler);
        Assert.Contains("简体", prompt);
        Assert.Contains("繁体", prompt);            // 简体时必须明确禁止繁体字
        Assert.Contains("importance", prompt);
        Assert.Contains("0.80", prompt);
        Assert.Contains("0.20", prompt);
    }

    [Fact]
    public async Task 联网通道提示词_含简体约束与重要度评分要求()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(WebSearch(("https://example.com/a", "摘要。", 0.8))));
        var source = new WebSearchNewsSource(handler.CreateClient());
        var config = new AppConfig { ApiKey = "test-key" };

        await source.FetchAsync("武汉", config);

        var prompt = WebPrompt(handler);
        Assert.Contains("简体", prompt);
        Assert.Contains("繁体", prompt);
        Assert.Contains("importance", prompt);
        Assert.Contains("0.80", prompt);
        Assert.Contains("0.20", prompt);
    }

    [Fact]
    public async Task 英文摘要语言_两条通道都要求英文()
    {
        var chatHandler = new FakeHttpHandler(_ => FakeHttp.Ok(Chat(("https://example.com/a", "Summary.", 0.8))));
        await new NewsSummarizer(chatHandler.CreateClient()).SummarizeAsync(
            "武汉",
            [Sa1Data.Raw("https://example.com/a", "标题")],
            new AppConfig { ApiKey = "test-key", SummaryLanguage = SummaryLanguageOption.English });

        var webHandler = new FakeHttpHandler(_ => FakeHttp.Ok(WebSearch(("https://example.com/a", "Summary.", 0.8))));
        await new WebSearchNewsSource(webHandler.CreateClient()).FetchAsync(
            "武汉",
            new AppConfig { ApiKey = "test-key", SummaryLanguage = SummaryLanguageOption.English });

        Assert.Contains("English", ChatPrompt(chatHandler));
        Assert.Contains("English", WebPrompt(webHandler));
    }

    [Fact]
    public async Task 繁体摘要语言_两条通道都要求繁体()
    {
        var chatHandler = new FakeHttpHandler(_ => FakeHttp.Ok(Chat(("https://example.com/a", "摘要。", 0.8))));
        await new NewsSummarizer(chatHandler.CreateClient()).SummarizeAsync(
            "武汉",
            [Sa1Data.Raw("https://example.com/a", "标题")],
            new AppConfig { ApiKey = "test-key", SummaryLanguage = SummaryLanguageOption.TraditionalChinese });

        var webHandler = new FakeHttpHandler(_ => FakeHttp.Ok(WebSearch(("https://example.com/a", "摘要。", 0.8))));
        await new WebSearchNewsSource(webHandler.CreateClient()).FetchAsync(
            "武汉",
            new AppConfig { ApiKey = "test-key", SummaryLanguage = SummaryLanguageOption.TraditionalChinese });

        Assert.Contains("繁体中文", ChatPrompt(chatHandler));
        Assert.Contains("繁体中文", WebPrompt(webHandler));
    }

    // ---------------- 通道产出：重要度与原始内容 ----------------

    [Fact]
    public async Task 提炼通道_重要度与原始内容写进条目_原始内容超长截断()
    {
        const string url = "https://example.com/a1";
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Chat((url, "摘要一。", 0.82))));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var raw = new[]
        {
            new RawNewsItem
            {
                Keyword = "武汉",
                Url = url,
                Title = "标题",
                Description = new string('字', NewsSummarizer.MaxRawContentChars + 500),
            },
        };

        var items = await summarizer.SummarizeAsync("武汉", raw, new AppConfig { ApiKey = "test-key" });

        var item = Assert.Single(items);
        Assert.Equal(0.82, item.Importance);
        Assert.NotNull(item.RawContent);
        Assert.Equal(NewsSummarizer.MaxRawContentChars, item.RawContent!.Length);
    }

    [Fact]
    public async Task 提炼通道_模型未给重要度时条目为null()
    {
        const string url = "https://example.com/a2";
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Chat((url, "摘要。", null))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", [Sa1Data.Raw(url, "标题")], new AppConfig { ApiKey = "test-key" });

        Assert.Null(Assert.Single(items).Importance);
    }

    [Fact]
    public async Task 联网通道_解析重要度()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(WebSearch(("https://example.com/w1", "摘要。", 0.91))));
        var source = new WebSearchNewsSource(handler.CreateClient());

        var raw = await source.FetchAsync("武汉", new AppConfig { ApiKey = "test-key" });

        Assert.Equal(0.91, Assert.Single(raw).Importance);
    }

    // ---------------- 协调器：同批均衡 + 门槛 ----------------

    [Fact]
    public async Task 联网通道入库_一批同分被均衡_门槛过滤后不再全留或全丢()
    {
        using var temp = new TempDirectory();
        var config = WebConfig(4);
        var entries = Enumerable.Range(1, 4)
            .Select(i => ($"https://news.example.com/{i}", $"摘要{i}。", (double?)0.9))
            .ToArray();
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(WebSearch(entries)));
        var repository = new NewsRepository(temp.Path);
        var coordinator = BuildCoordinator(handler, repository, config);

        var result = await coordinator.FetchAsync();

        Assert.Equal(4, result.NewCount);

        var readConfig = config.Clone();
        readConfig.ImportanceThreshold = 0;
        var all = repository.GetPlaylist(readConfig);
        Assert.Equal(4, all.Count);
        var importances = all.Select(i => i.Importance!.Value).ToList();
        Assert.Equal(4, importances.Distinct().Count());
        Assert.True(importances.Max() - importances.Min() > 0.5, "同批同分应被拉开");

        // 默认门槛 0.7：均衡后只有最高那几条留下，而不是 4 条全留
        var gated = repository.GetPlaylist(config);
        Assert.InRange(gated.Count, 1, 2);
    }

    [Fact]
    public async Task 提炼通道入库_重要度从模型评分落到条目()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.AiMinIntervalMinutes = 0;
        const string url = "https://example.com/rss-1";
        var rss = Sa1Data.Rss(("标题一", url, DateTimeOffset.Now));
        var handler = new FakeHttpHandler(request => request.Url.Contains(Sa1Data.RssHost)
            ? FakeHttp.Ok(rss, "application/rss+xml")
            : FakeHttp.Ok(Chat((url, "摘要。", 0.66))));
        var repository = new NewsRepository(temp.Path);
        var coordinator = BuildCoordinator(handler, repository, config);

        var result = await coordinator.FetchAsync();

        Assert.Equal(1, result.NewCount);
        var readConfig = config.Clone();
        readConfig.ImportanceThreshold = 0;
        Assert.Equal(0.66, Assert.Single(repository.GetPlaylist(readConfig)).Importance);
    }

    // ---------------- 仓储：门槛过滤 + 随机顺序 ----------------

    [Fact]
    public void 低于门槛的条目不进播放列表_未评分的保留_等于门槛的保留()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var now = DateTimeOffset.Now;
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/low", 0.6, now),
            Item("https://example.com/edge", 0.7, now),
            Item("https://example.com/none", null, now),
            Item("https://example.com/high", 0.95, now),
        ]);
        var config = new AppConfig { ImportanceThreshold = 0.7, PlayOrder = PlayOrder.ByPublishTime };

        var playlist = repository.GetPlaylist(config);

        Assert.Equal(3, playlist.Count);
        Assert.DoesNotContain(playlist, i => i.Url.EndsWith("/low", StringComparison.Ordinal));
        Assert.Contains(playlist, i => i.Url.EndsWith("/edge", StringComparison.Ordinal));
        Assert.Contains(playlist, i => i.Url.EndsWith("/none", StringComparison.Ordinal));
        Assert.Contains(playlist, i => i.Url.EndsWith("/high", StringComparison.Ordinal));
    }

    [Fact]
    public void 按关键字顺序播放时_过滤先于排序_顺序仍按关键字()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var now = DateTimeOffset.Now;
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/w2", 0.9, now, "中国"),
            Item("https://example.com/w1", 0.9, now, "武汉"),
            Item("https://example.com/w3", 0.1, now, "武汉"),   // 低分：应被过滤
        ]);
        var config = new AppConfig
        {
            ImportanceThreshold = 0.7,
            PlayOrder = PlayOrder.ByKeywordOrder,
            Keywords = ["武汉", "中国"],
        };

        var playlist = repository.GetPlaylist(config);

        Assert.Equal(2, playlist.Count);
        Assert.Equal(["武汉", "中国"], playlist.Select(i => i.Keyword));
    }

    [Fact]
    public void 随机顺序_同一版本两次取列表顺序一致()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var now = DateTimeOffset.Now;
        repository.AddOrUpdateRange(Enumerable.Range(1, 6)
            .Select(i => Item($"https://example.com/r{i}", 0.9, now.AddMinutes(-i))));
        var config = new AppConfig { ImportanceThreshold = 0.7, PlayOrder = PlayOrder.Random };

        var first = repository.GetPlaylist(config).Select(i => i.Id).ToList();
        var second = repository.GetPlaylist(config).Select(i => i.Id).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void 改门槛立即生效_不返回旧门槛下的随机缓存()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var now = DateTimeOffset.Now;
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/high", 0.9, now),
            Item("https://example.com/mid", 0.3, now),
        ]);
        var config = new AppConfig { ImportanceThreshold = 0.7, PlayOrder = PlayOrder.Random };

        Assert.Single(repository.GetPlaylist(config));

        config.ImportanceThreshold = 0.2;

        Assert.Equal(2, repository.GetPlaylist(config).Count);
    }

    // ---------------- 保留天数与去重 ----------------

    [Fact]
    public async Task 超过保留天数的条目被清理_且该URL下一轮可以重新抓取()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.OnlyTodayNews = false;
        config.RetentionDays = 1;
        config.AiMinIntervalMinutes = 0;
        const string url = "https://example.com/old";
        var rss = Sa1Data.Rss(("老新闻", url, DateTimeOffset.Now.AddDays(-3)));
        var handler = new FakeHttpHandler(request => request.Url.Contains(Sa1Data.RssHost)
            ? FakeHttp.Ok(rss, "application/rss+xml")
            : FakeHttp.Ok(Chat((url, "摘要。", 0.8))));
        var repository = new NewsRepository(temp.Path);
        var coordinator = BuildCoordinator(handler, repository, config);

        var first = await coordinator.FetchAsync();
        Assert.Equal(1, first.NewCount);

        // 第二轮：先清理（3 天前发布 > 保留 1 天），URL 同时从去重集合移除 → 能重新抓回来
        var second = await coordinator.FetchAsync();

        Assert.Equal(1, second.NewCount);
        Assert.True(repository.Contains(url));
        Assert.Equal(2, handler.CountOf("chat/completions"));   // 两轮各提炼一次
    }

    [Fact]
    public async Task 保留期内的条目不会被清理()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.OnlyTodayNews = false;
        config.RetentionDays = 3;
        config.AiMinIntervalMinutes = 0;
        const string url = "https://example.com/fresh";
        var rss = Sa1Data.Rss(("两天前的新闻", url, DateTimeOffset.Now.AddDays(-2)));
        var handler = new FakeHttpHandler(request => request.Url.Contains(Sa1Data.RssHost)
            ? FakeHttp.Ok(rss, "application/rss+xml")
            : FakeHttp.Ok(Chat((url, "摘要。", 0.8))));
        var repository = new NewsRepository(temp.Path);
        var coordinator = BuildCoordinator(handler, repository, config);

        await coordinator.FetchAsync();
        var second = await coordinator.FetchAsync();

        Assert.Equal(0, second.NewCount);      // 还在缓存里 → 去重挡住
        Assert.Equal(1, handler.CountOf("chat/completions"));
    }

    [Fact]
    public async Task 第二轮同URL不再重复提炼()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.AiMinIntervalMinutes = 0;
        const string url = "https://example.com/today";
        var rss = Sa1Data.Rss(("今天的新闻", url, DateTimeOffset.Now));
        var handler = new FakeHttpHandler(request => request.Url.Contains(Sa1Data.RssHost)
            ? FakeHttp.Ok(rss, "application/rss+xml")
            : FakeHttp.Ok(Chat((url, "摘要。", 0.8))));
        var repository = new NewsRepository(temp.Path);
        var coordinator = BuildCoordinator(handler, repository, config);

        var first = await coordinator.FetchAsync();
        var second = await coordinator.FetchAsync();

        Assert.Equal(1, first.NewCount);
        Assert.Equal(0, second.NewCount);
        Assert.Equal(1, handler.CountOf("chat/completions"));
    }

    // ---------------- 辅助 ----------------

    private static AppConfig WebConfig(int perKeyword) => new()
    {
        FetchMode = NewsFetchMode.WebSearch,
        Keywords = ["武汉"],
        ItemsPerKeyword = perKeyword,
        SummaryMaxChars = 70,
        AiMinIntervalMinutes = 0,
        ApiKey = "test-key",
        ImportanceThreshold = 0.7,
    };

    private static NewsFetchCoordinator BuildCoordinator(FakeHttpHandler handler, NewsRepository repository, AppConfig config)
        => new(
            [new RssNewsSource(handler.CreateClient()), new WebSearchNewsSource(handler.CreateClient())],
            new NewsSummarizer(handler.CreateClient()),
            new ImageResolver(handler.CreateClient()),
            repository,
            new FakeConfigProvider(config));

    private static NewsItem Item(string url, double? importance, DateTimeOffset published, string keyword = "武汉")
        => new()
        {
            Id = NewsSummarizer.MakeId(url),
            Url = url,
            Title = "标题",
            Summary = "摘要。",
            Keyword = keyword,
            PublishedAt = published,
            Importance = importance,
        };

    /// <summary>构造带重要度的 chat/completions 响应。</summary>
    private static string Chat(params (string Url, string Summary, double? Importance)[] entries)
    {
        var payload = JsonSerializer.Serialize(entries.Select(e => Row(e.Url, e.Summary, e.Importance)));
        return JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = payload } } },
        });
    }

    /// <summary>构造带重要度的联网搜索响应（Anthropic 兼容 + 检索工具块）。</summary>
    private static string WebSearch(params (string Url, string Summary, double? Importance)[] entries)
    {
        var rows = entries
            .Select(e =>
            {
                var row = Row(e.Url, e.Summary, e.Importance);
                row["title"] = "标题";
                row["keyword"] = "武汉";
                return row;
            })
            .ToList();
        return Sa1Data.WebSearchResponse(JsonSerializer.Serialize(rows));
    }

    private static Dictionary<string, object?> Row(string url, string summary, double? importance)
    {
        var row = new Dictionary<string, object?> { ["url"] = url, ["summary"] = summary };
        if (importance is { } value) row["importance"] = value;
        return row;
    }

    private static string ChatPrompt(FakeHttpHandler handler)
    {
        var body = handler.Recorded.Last(r => r.Url.Contains("/chat/completions", StringComparison.Ordinal)).Body;
        Assert.NotNull(body);
        using var document = JsonDocument.Parse(body!);
        return document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
    }

    private static string WebPrompt(FakeHttpHandler handler)
    {
        var body = handler.Recorded.Last(r => r.Url.Contains("/v1/messages", StringComparison.Ordinal)).Body;
        Assert.NotNull(body);
        using var document = JsonDocument.Parse(body!);
        return document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
    }
}
