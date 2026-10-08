using System.Net;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

public class Sa1NewsFetchCoordinatorTests
{
    private static (NewsFetchCoordinator Coordinator, FakeHttpHandler Handler, NewsRepository Repository, FakeConfigProvider Config)
        Build(AppConfig config, TempDirectory temp, Func<RecordedRequest, HttpResponseMessage>? responder = null)
    {
        var handler = new FakeHttpHandler(responder ?? Default(config));
        var client = handler.CreateClient();
        var repository = new NewsRepository(temp.Path);
        var provider = new FakeConfigProvider(config);

        var coordinator = new NewsFetchCoordinator(
            [new RssNewsSource(client), new WebSearchNewsSource(client)],
            new NewsSummarizer(client),
            new ImageResolver(client),
            repository,
            provider);

        return (coordinator, handler, repository, provider);
    }

    /// <summary>RSS 与 chat 两个端点分别应答。</summary>
    private static Func<RecordedRequest, HttpResponseMessage> Default(AppConfig config)
        => request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
            {
                var pairs = config.Keywords
                    .SelectMany(keyword => Enumerable.Range(1, config.ItemsPerKeyword)
                        .Select(i => ($"https://example.com/{keyword}/{i}", $"{keyword} 的第 {i} 条摘要")))
                    .ToArray();
                return FakeHttp.Ok(Sa1Data.ChatResponse(pairs));
            }

            var match = System.Text.RegularExpressions.Regex.Match(request.Url, @"q=([^&]+)");
            var keyword = match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : "未知";
            var items = Enumerable.Range(1, config.ItemsPerKeyword)
                .Select(i => ($"标题 {keyword} {i}", $"https://example.com/{keyword}/{i}", (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-i)))
                .ToArray();
            return FakeHttp.Ok(Sa1Data.Rss(items), "application/rss+xml");
        };

    [Fact]
    public async Task 协调器_RSS通道提取炼入库并标记AI调用()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉", "美国");
        var (coordinator, handler, repository, _) = Build(config, temp);

        var result = await coordinator.FetchAsync();

        Assert.Equal("RSS", result.Channel);
        Assert.Null(result.Warning);
        Assert.Equal(6, result.RawCount);
        Assert.Equal(6, result.NewCount);
        Assert.Equal(6, result.SummarizedCount);
        Assert.Equal(2, handler.CountOf("/chat/completions"));
        Assert.NotNull(repository.LastAiCallAt);

        var playlist = repository.GetPlaylist(config);
        Assert.Equal(6, playlist.Count);
        Assert.Equal(16, playlist[0].Id.Length);
    }

    [Fact]
    public async Task 协调器_连跑两轮第二轮不再调用AI()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉", "美国");
        var (coordinator, handler, repository, _) = Build(config, temp);

        var first = await coordinator.FetchAsync();
        var callsAfterFirst = handler.CountOf("/chat/completions");

        repository.MarkAiCalled(DateTimeOffset.Now.AddMinutes(-30));
        var second = await coordinator.FetchAsync();

        Assert.Equal(2, callsAfterFirst);
        Assert.Equal(6, first.NewCount);
        Assert.Equal(0, second.NewCount);
        Assert.Equal(0, second.SummarizedCount);
        Assert.Equal(6, second.RawCount);
        Assert.Equal(callsAfterFirst, handler.CountOf("/chat/completions"));
        Assert.Equal(6, repository.GetPlaylist(config).Count);
        Assert.False(File.Exists(repository.FilePath + ".tmp"));
    }

    [Fact]
    public async Task 协调器_冷却期内不调AI也不入库()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        var (coordinator, handler, repository, _) = Build(config, temp);
        repository.MarkAiCalled(DateTimeOffset.Now);

        var result = await coordinator.FetchAsync();

        Assert.Equal(0, result.NewCount);
        Assert.Equal(0, result.RawCount);
        Assert.NotNull(result.Warning);
        Assert.Contains("最小间隔", result.Warning);
        Assert.Equal(0, handler.Count);
        Assert.Empty(repository.GetPlaylist(config));
    }

    [Fact]
    public async Task 协调器_联网搜索通道不走提炼()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.FetchMode = NewsFetchMode.WebSearch;
        config.ItemsPerKeyword = 2;
        var (coordinator, handler, repository, _) = Build(config, temp, request =>
            request.Url.Contains("/v1/messages", StringComparison.Ordinal)
                ? FakeHttp.Ok(Sa1Data.WebSearchResponse(
                    """[{"title":"标题一","summary":"摘要一","url":"https://news.example.com/1"},{"title":"标题二","summary":"摘要二","url":"https://news.example.com/2"}]"""))
                : FakeHttp.Status(HttpStatusCode.NotFound));

        var result = await coordinator.FetchAsync();

        Assert.Equal("模型联网搜索", result.Channel);
        Assert.Equal(2, result.NewCount);
        Assert.Equal(0, result.SummarizedCount);
        Assert.Null(result.Warning);
        Assert.Equal(0, handler.CountOf("/chat/completions"));

        var playlist = repository.GetPlaylist(config);
        Assert.Equal(2, playlist.Count);
        Assert.All(playlist, item => Assert.Equal("联网搜索", item.Source));
        Assert.NotNull(repository.LastAiCallAt);
    }

    [Fact]
    public async Task 协调器_Auto模式联网失败时回退RSS并写警告()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.FetchMode = NewsFetchMode.Auto;
        config.ItemsPerKeyword = 1;
        var (coordinator, handler, repository, _) = Build(config, temp, request =>
            request.Url.Contains("/v1/messages", StringComparison.Ordinal)
                ? FakeHttp.Status(HttpStatusCode.InternalServerError)
                : Default(config)(request));

        var result = await coordinator.FetchAsync();

        Assert.Equal("RSS", result.Channel);
        Assert.NotNull(result.Warning);
        Assert.Contains("已回退 RSS", result.Warning);
        Assert.Equal(1, result.NewCount);
        Assert.Single(repository.GetPlaylist(config));
        Assert.Equal(1, handler.CountOf("/chat/completions"));
    }

    [Fact]
    public async Task 协调器_Auto模式联网返回空时回退RSS()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.FetchMode = NewsFetchMode.Auto;
        config.ItemsPerKeyword = 1;
        var (coordinator, _, repository, _) = Build(config, temp, request =>
            request.Url.Contains("/v1/messages", StringComparison.Ordinal)
                ? FakeHttp.Ok(Sa1Data.WebSearchResponse("[]"))
                : Default(config)(request));

        var result = await coordinator.FetchAsync();

        Assert.Equal("RSS", result.Channel);
        Assert.Contains("已回退 RSS", result.Warning);
        Assert.Equal(1, result.NewCount);
        Assert.Single(repository.GetPlaylist(config));
    }

    [Fact]
    public async Task 协调器_未注册来源时降级为警告而不抛异常()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok("{}"));
        var repository = new NewsRepository(temp.Path);
        var coordinator = new NewsFetchCoordinator(
            [],
            new NewsSummarizer(handler.CreateClient()),
            new ImageResolver(handler.CreateClient()),
            repository,
            new FakeConfigProvider(config));

        var result = await coordinator.FetchAsync();

        Assert.Equal(0, result.NewCount);
        Assert.Contains("未注册", result.Warning);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task 协调器_图文模式下按条目抓取封面图()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.ItemsPerKeyword = 1;
        config.PlaybackMode = PlaybackMode.TextAndImage;
        var (coordinator, handler, repository, _) = Build(config, temp, request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal)) return Default(config)(request);
            if (request.Url.Contains("/news/search", StringComparison.Ordinal)) return Default(config)(request);
            return FakeHttp.Ok(
                """<html><head><meta property="og:image" content="/img/cover.jpg"></head></html>""",
                "text/html");
        });

        var result = await coordinator.FetchAsync();

        Assert.Equal(1, result.NewCount);
        var item = Assert.Single(repository.GetPlaylist(config));
        Assert.Equal("https://example.com/img/cover.jpg", item.ImageUrl);
    }

    [Fact]
    public async Task 协调器_纯文字模式不抓取封面图()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.ItemsPerKeyword = 1;
        var (coordinator, handler, _, _) = Build(config, temp);

        await coordinator.FetchAsync();

        Assert.Equal(1, handler.CountOf("/news/search"));
        Assert.Equal(1, handler.CountOf("/chat/completions"));
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task 协调器_RSS整体失败时异常向上抛出()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        var (coordinator, _, _, _) = Build(config, temp, _ => FakeHttp.Status(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.FetchAsync());
    }

    [Fact]
    public async Task 协调器_入库条目按保留期清理()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.Config("武汉");
        config.ItemsPerKeyword = 1;
        var (coordinator, _, repository, _) = Build(config, temp);
        repository.AddOrUpdateRange(
        [
            new NewsItem
            {
                Id = NewsSummarizer.MakeId("https://example.com/stale"),
                Url = "https://example.com/stale",
                Keyword = "武汉",
                Title = "过期",
                Summary = "过期摘要",
                PublishedAt = DateTimeOffset.Now.AddDays(-10),
            },
        ]);

        await coordinator.FetchAsync();

        Assert.False(repository.Contains("https://example.com/stale"));
        Assert.Single(repository.GetPlaylist(config));
    }
}
