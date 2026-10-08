using System.Net;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>共享频道源（模板不含 {q}）与备用源链。</summary>
public class Sa1SharedChannelTests
{
    private const string Primary = "https://primary.example.com/rss.xml";
    private const string Backup1 = "https://backup1.example.com/rss.xml";
    private const string Backup2 = "https://backup2.example.com/rss.xml";

    private static (NewsFetchCoordinator Coordinator, NewsRepository Repository) Build(
        AppConfig config, TempDirectory temp, FakeHttpHandler handler)
    {
        var client = handler.CreateClient();
        var repository = new NewsRepository(temp.Path);
        var coordinator = new NewsFetchCoordinator(
            [new RssNewsSource(client), new WebSearchNewsSource(client)],
            new NewsSummarizer(client),
            new ImageResolver(client),
            repository,
            new FakeConfigProvider(config));

        return (coordinator, repository);
    }

    /// <summary>共享源应答：RSS 给 count 条，chat 给对应摘要。</summary>
    private static Func<RecordedRequest, HttpResponseMessage> SharedResponder(int count)
        => request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
            {
                var pairs = Enumerable.Range(1, count)
                    .Select(i => ($"https://example.com/标题{i}", $"第 {i} 条摘要"))
                    .ToArray();
                return FakeHttp.Ok(Sa1Data.ChatResponse(pairs));
            }

            return FakeHttp.Ok(Sa1Data.Rss(Sa1Data.FreshItems(count)), "application/rss+xml");
        };

    [Fact]
    public async Task 共享源_一轮只发一次请求且条数按关键字数放大()
    {
        using var temp = new TempDirectory();
        // 3 个关键字 × 3 条 = 9，低于共享源下限 10 → 取 10 条
        var config = Sa1Data.SharedConfig("武汉", "美国", "AI");
        var handler = new FakeHttpHandler(SharedResponder(12));
        var (coordinator, repository) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal("RSS", result.Channel);
        Assert.Equal(1, handler.CountOf(Sa1Data.RssHost));
        Assert.Equal(10, result.RawCount);
        Assert.Equal(10, result.NewCount);

        // 同一共享源条目都归到一个关键字下 → 一次提炼
        Assert.Equal(1, handler.CountOf("/chat/completions"));

        var playlist = repository.GetPlaylist(config);
        Assert.Equal(10, playlist.Count);
        Assert.All(playlist, item => Assert.Equal("测试源", item.Keyword));
        // 降序取最新 10 条，最旧的 2 条被丢弃
        Assert.DoesNotContain(playlist, item => item.Url == "https://example.com/标题11");
        Assert.DoesNotContain(playlist, item => item.Url == "https://example.com/标题12");
    }

    [Fact]
    public async Task 共享源_条数上限随关键字数与每关键字条数放大()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉", "美国");
        config.ItemsPerKeyword = 8; // 2 × 8 = 16 > 下限 10
        var handler = new FakeHttpHandler(SharedResponder(20));
        var (coordinator, _) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(16, result.RawCount);
        Assert.Equal(1, handler.CountOf(Sa1Data.RssHost));
    }

    [Fact]
    public async Task 共享源_条目关键字取频道标题()
    {
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.Rss(Sa1Data.FreshItems(2)))).CreateClient());

        var items = await source.FetchSharedAsync(Sa1Data.SharedConfig("武汉"));

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("测试源", item.Keyword));
    }

    [Fact]
    public async Task 共享源_取不到频道标题时用新闻标记()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel>
            <item><title>标题</title><link>https://example.com/a</link></item>
            </channel></rss>
            """;
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(xml, "application/rss+xml")).CreateClient());
        var config = Sa1Data.SharedConfig("武汉");
        config.OnlyTodayNews = false;

        var items = await source.FetchSharedAsync(config);

        Assert.Equal("新闻", Assert.Single(items).Keyword);
    }

    [Fact]
    public async Task 搜索型模板_仍按关键字各抓一次()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉", "美国", "AI");
        config.ItemsPerKeyword = 2;
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
            {
                var pairs = config.Keywords
                    .SelectMany(keyword => Enumerable.Range(1, config.ItemsPerKeyword)
                        .Select(i => ($"https://example.com/{keyword}/{i}", $"{keyword} 第 {i} 条")))
                    .ToArray();
                return FakeHttp.Ok(Sa1Data.ChatResponse(pairs));
            }

            var match = System.Text.RegularExpressions.Regex.Match(request.Url, @"q=([^&]+)");
            var keyword = Uri.UnescapeDataString(match.Groups[1].Value);
            return FakeHttp.Ok(
                Sa1Data.Rss([.. Enumerable.Range(1, config.ItemsPerKeyword)
                    .Select(i => ($"标题 {keyword} {i}", $"https://example.com/{keyword}/{i}", (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-i)))]),
                "application/rss+xml");
        });
        var (coordinator, _) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(3, handler.CountOf(Sa1Data.RssHost));
        Assert.Equal(3, handler.CountOf("/chat/completions"));
        Assert.Equal(6, result.RawCount);
    }

    [Fact]
    public async Task 备用源_主源HTTP失败时按序回退()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        config.RssTemplate = Primary;
        config.RssFallbackTemplates = [Backup1, Backup2];
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
                return FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/标题1", "摘要一")));

            if (request.Url.Contains(Backup2, StringComparison.Ordinal))
                return FakeHttp.Ok(Sa1Data.Rss(Sa1Data.FreshItems(1)), "application/rss+xml");

            return FakeHttp.Status(HttpStatusCode.InternalServerError);
        });
        var (coordinator, repository) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal("RSS", result.Channel);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(3, handler.Recorded.Count(r => !r.Url.Contains("/chat/completions", StringComparison.Ordinal)));
        Assert.Equal(
            [Primary, Backup1, Backup2],
            handler.Recorded
                .Where(r => !r.Url.Contains("/chat/completions", StringComparison.Ordinal))
                .Select(r => r.Url)
                .ToArray());
        Assert.Single(repository.GetPlaylist(config));
    }

    [Fact]
    public async Task 备用源_主源解析不出条目时回退()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        config.RssTemplate = Primary;
        config.RssFallbackTemplates = [Backup1];
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
                return FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/标题1", "摘要一")));

            return request.Url.Contains(Primary, StringComparison.Ordinal)
                ? FakeHttp.Ok(Sa1Data.Rss(), "application/rss+xml") // 0 条
                : FakeHttp.Ok(Sa1Data.Rss(Sa1Data.FreshItems(1)), "application/rss+xml");
        });
        var (coordinator, _) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(1, result.RawCount);
        Assert.Equal(2, handler.CountOf("example.com/rss.xml"));
    }

    [Fact]
    public async Task 备用源_主源XML损坏时回退()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        config.RssTemplate = Primary;
        config.RssFallbackTemplates = [Backup1];
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
                return FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/标题1", "摘要一")));

            return request.Url.Contains(Primary, StringComparison.Ordinal)
                ? FakeHttp.Ok("<rss><channel>", "application/rss+xml")
                : FakeHttp.Ok(Sa1Data.Rss(Sa1Data.FreshItems(1)), "application/rss+xml");
        });
        var (coordinator, _) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(1, result.NewCount);
    }

    [Fact]
    public async Task 备用源_全部失败时抛最后一个异常()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        config.RssTemplate = Primary;
        config.RssFallbackTemplates = [Backup1, Backup2];
        var handler = new FakeHttpHandler(_ => FakeHttp.Status(HttpStatusCode.InternalServerError));
        var (coordinator, _) = Build(config, temp, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.FetchAsync());
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task 备用源_全部无条目时返回空不抛异常()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        config.RssTemplate = Primary;
        config.RssFallbackTemplates = [Backup1];
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.Rss(), "application/rss+xml"));
        var (coordinator, _) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(0, result.RawCount);
        Assert.Equal(0, result.NewCount);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task Auto模式_回退共享源时也只抓一次()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉", "美国");
        config.FetchMode = NewsFetchMode.Auto;
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/v1/messages", StringComparison.Ordinal))
                return FakeHttp.Status(HttpStatusCode.InternalServerError);

            return SharedResponder(10)(request);
        });
        var (coordinator, repository) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal("RSS", result.Channel);
        Assert.Contains("已回退 RSS", result.Warning);
        Assert.Equal(1, handler.CountOf(Sa1Data.RssHost));
        Assert.Equal(10, result.NewCount);
        Assert.Equal(10, repository.GetPlaylist(config).Count);
    }

    [Fact]
    public async Task Auto模式_联网成功时不请求共享源()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        config.FetchMode = NewsFetchMode.Auto;
        var handler = new FakeHttpHandler(request =>
            request.Url.Contains("/v1/messages", StringComparison.Ordinal)
                ? FakeHttp.Ok(Sa1Data.WebSearchResponse("""[{"title":"标题","summary":"摘要","url":"https://news.example.com/1"}]"""))
                : FakeHttp.Ok(Sa1Data.Rss(Sa1Data.FreshItems(1)), "application/rss+xml"));
        var (coordinator, _) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal("模型联网搜索", result.Channel);
        Assert.Equal(0, handler.CountOf(Sa1Data.RssHost));
        Assert.Equal(1, result.NewCount);
    }
}
