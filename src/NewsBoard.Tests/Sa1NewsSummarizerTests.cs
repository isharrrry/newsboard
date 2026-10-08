using System.Net;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

public class Sa1NewsSummarizerTests
{
    private static readonly DateTimeOffset Published = new(2025, 9, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task 提炼_解析严格JSON并生成条目()
    {
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "原标题一", publishedAt: Published) };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/1", "武汉新增三条地铁线路，年底通车。"))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        var item = Assert.Single(items);
        Assert.Equal(NewsSummarizer.MakeId("https://example.com/1"), item.Id);
        Assert.Equal(16, item.Id.Length);
        Assert.Equal("武汉新增三条地铁线路，年底通车。", item.Summary);
        Assert.Equal("原标题一", item.Title);
        Assert.Equal(Published, item.PublishedAt);
        Assert.Equal("武汉", item.Keyword);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task 提炼_同一关键字全部原文一次请求()
    {
        var raw = new[]
        {
            Sa1Data.Raw("https://example.com/1", "标题一"),
            Sa1Data.Raw("https://example.com/2", "标题二"),
            Sa1Data.Raw("https://example.com/3", "标题三"),
        };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(
            ("https://example.com/1", "摘要一"),
            ("https://example.com/2", "摘要二"),
            ("https://example.com/3", "摘要三"))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        Assert.Equal(3, items.Count);
        Assert.Equal(1, handler.Count);
        var body = Assert.Single(handler.Recorded).Body!;
        Assert.Contains("https://example.com/1", body);
        Assert.Contains("https://example.com/2", body);
        Assert.Contains("https://example.com/3", body);
    }

    [Fact]
    public async Task 提炼_请求带Bearer鉴权且指向chat兼容端点()
    {
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "标题一") };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/1", "摘要"))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        var request = Assert.Single(handler.Recorded);
        Assert.Equal("https://api.deepseek.com/chat/completions", request.Url);
        Assert.Equal("Bearer test-key", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task 提炼_超长摘要按最后一个句号截断()
    {
        const string longSummary = "一二三四五六七八九十。十一十二。十三十四十五。";
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "标题一") };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/1", longSummary))));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var config = Sa1Data.Config("武汉");
        config.SummaryMaxChars = 10;

        var items = await summarizer.SummarizeAsync("武汉", raw, config);

        var summary = Assert.Single(items).Summary;
        Assert.Equal("一二三四五六七八九十。", summary);
        Assert.True(summary.Length <= (int)(config.SummaryMaxChars * 1.2));
    }

    [Fact]
    public async Task 提炼_未超上限时原样保留()
    {
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "标题一") };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/1", "刚好五十字以内的摘要。"))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        Assert.Equal("刚好五十字以内的摘要。", Assert.Single(items).Summary);
    }

    [Fact]
    public async Task 提炼_模型未给摘要的条目被跳过()
    {
        var raw = new[]
        {
            Sa1Data.Raw("https://example.com/1", "标题一"),
            Sa1Data.Raw("https://example.com/2", "标题二"),
        };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/1", "摘要一"))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        Assert.Equal("https://example.com/1", Assert.Single(items).Url);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task 提炼_失败重试一次后仍失败则退化为标题截断()
    {
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "这是一条很长的原标题需要截断", publishedAt: Published) };
        var handler = new FakeHttpHandler(_ => FakeHttp.Status(HttpStatusCode.InternalServerError));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var config = Sa1Data.Config("武汉");
        config.SummaryMaxChars = 5;

        var items = await summarizer.SummarizeAsync("武汉", raw, config);

        var item = Assert.Single(items);
        Assert.Equal("这是一条很", item.Summary);
        Assert.Equal(2, handler.Count);
        Assert.Equal(Published, item.PublishedAt);
    }

    [Fact]
    public async Task 提炼_模型返回非法JSON时退化为标题截断()
    {
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "标题一") };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok("""{"choices":[{"message":{"content":"抱歉，我无法完成。"}}]}"""));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        Assert.Equal("标题一", Assert.Single(items).Summary);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task 提炼_缺失发布时间的条目用当前时间()
    {
        var raw = new[] { Sa1Data.Raw("https://example.com/1", "标题一") };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.ChatResponse(("https://example.com/1", "摘要一"))));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var before = DateTimeOffset.Now;
        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        var item = Assert.Single(items);
        Assert.True((item.PublishedAt - before).Duration() < TimeSpan.FromMinutes(1));
        Assert.True((item.FetchedAt - before).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task 提炼_无原文时不发请求()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok("{}"));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", [], Sa1Data.Config("武汉"));

        Assert.Empty(items);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task 提炼_无链接的原文被跳过()
    {
        var raw = new[] { new RawNewsItem { Keyword = "武汉", Title = "无链接", Description = "正文" } };
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok("{}"));
        var summarizer = new NewsSummarizer(handler.CreateClient());

        var items = await summarizer.SummarizeAsync("武汉", raw, Sa1Data.Config("武汉"));

        Assert.Empty(items);
        Assert.Equal(0, handler.Count);
    }
}
