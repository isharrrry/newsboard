using System.Xml;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

public class Sa1RssNewsSourceTests
{
    [Fact]
    public async Task Rss_解析标题链接时间来源并清理正文()
    {
        var now = DateTimeOffset.UtcNow;
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.Rss(("武汉 &amp; 测试", "https://example.com/a1", now.AddMinutes(-30))), "application/rss+xml"));
        var source = new RssNewsSource(handler.CreateClient());

        var items = await source.FetchAsync("武汉", Sa1Data.Config("武汉"));

        var item = Assert.Single(items);
        Assert.Equal("武汉 & 测试", item.Title);
        Assert.Equal("https://example.com/a1", item.Url);
        Assert.Equal("示例站", item.Source);
        Assert.Equal("正文 & 片段", item.Description);
        Assert.Equal("武汉", item.Keyword);
        Assert.Null(item.PreSummarized);
        Assert.NotNull(item.PublishedAt);
        Assert.True((now - item.PublishedAt!.Value).Duration() < TimeSpan.FromMinutes(31));
    }

    [Fact]
    public async Task Rss_请求地址使用模板并转义关键字()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.Rss()));
        var source = new RssNewsSource(handler.CreateClient());
        var config = Sa1Data.Config("武汉 疫情");
        config.RssTemplate = "https://rss.example.com/search?q={q}&format=RSS";

        await source.FetchAsync("武汉 疫情", config);

        var url = Assert.Single(handler.Recorded).Url;
        Assert.StartsWith("https://rss.example.com/search?q=", url);
        Assert.Contains(Uri.EscapeDataString("武汉 疫情"), url);
        Assert.EndsWith("&format=RSS", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public async Task Rss_OnlyTodayNews_过滤超24小时条目()
    {
        var now = DateTimeOffset.UtcNow;
        var xml = Sa1Data.Rss(
            ("今天", "https://example.com/today", now.AddHours(-2)),
            ("前天", "https://example.com/old", now.AddHours(-30)));
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(xml)).CreateClient());

        var items = await source.FetchAsync("武汉", Sa1Data.Config("武汉"));

        Assert.Equal("今天", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Rss_关闭OnlyTodayNews_保留旧条目()
    {
        var now = DateTimeOffset.UtcNow;
        var xml = Sa1Data.Rss(("前天", "https://example.com/old", now.AddHours(-30)));
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(xml)).CreateClient());
        var config = Sa1Data.Config("武汉");
        config.OnlyTodayNews = false;

        var items = await source.FetchAsync("武汉", config);

        Assert.Single(items);
    }

    [Fact]
    public async Task Rss_解析不出时间时保留条目()
    {
        var xml = Sa1Data.Rss(("无时间", "https://example.com/notime", null));
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(xml)).CreateClient());

        var items = await source.FetchAsync("武汉", Sa1Data.Config("武汉"));

        var item = Assert.Single(items);
        Assert.Null(item.PublishedAt);
    }

    [Fact]
    public async Task Rss_按发布时间降序并截断到每关键字条数()
    {
        var now = DateTimeOffset.UtcNow;
        var xml = Sa1Data.Rss(
            ("第三新", "https://example.com/3", now.AddHours(-3)),
            ("最新", "https://example.com/1", now.AddHours(-1)),
            ("第二新", "https://example.com/2", now.AddHours(-2)),
            ("第四新", "https://example.com/4", now.AddHours(-4)));
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(xml)).CreateClient());
        var config = Sa1Data.Config("武汉");
        config.ItemsPerKeyword = 2;

        var items = await source.FetchAsync("武汉", config);

        Assert.Equal(2, items.Count);
        Assert.Equal("最新", items[0].Title);
        Assert.Equal("第二新", items[1].Title);
    }

    [Fact]
    public async Task Rss_缺标题或缺链接的条目被跳过()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel><title>测试源</title>
            <item><title>无链接</title><pubDate>Mon, 22 Sep 2025 10:00:00 GMT</pubDate></item>
            <item><link>https://example.com/nolink-title</link></item>
            <item><title>正常</title><link>https://example.com/ok</link></item>
            </channel></rss>
            """;
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok(xml)).CreateClient());
        var config = Sa1Data.Config("武汉");
        config.OnlyTodayNews = false;

        var items = await source.FetchAsync("武汉", config);

        Assert.Equal("正常", Assert.Single(items).Title);
    }

    [Fact]
    public async Task Rss_XML整体损坏时抛异常由上层处理()
    {
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok("<rss><channel>", "application/rss+xml")).CreateClient());

        await Assert.ThrowsAsync<XmlException>(() => source.FetchAsync("武汉", Sa1Data.Config("武汉")));
    }

    [Fact]
    public async Task Rss_HTTP失败时抛异常由上层处理()
    {
        var source = new RssNewsSource(new FakeHttpHandler(_ => FakeHttp.Status(System.Net.HttpStatusCode.InternalServerError)).CreateClient());

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync("武汉", Sa1Data.Config("武汉")));
    }
}
