using System.Text.Json;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

public class Sa1WebSearchNewsSourceTests
{
    private const string TwoItems =
        """[{"title":"标题一","summary":"摘要一","url":"https://news.example.com/1","keyword":"武汉"},{"title":"标题二","summary":"摘要二","url":"https://news.example.com/2","keyword":"武汉"}]""";

    [Fact]
    public async Task 联网搜索_未配置ApiKey时抛异常()
    {
        var source = new WebSearchNewsSource(new FakeHttpHandler(_ => FakeHttp.Ok("{}")).CreateClient());
        var config = Sa1Data.Config("武汉");
        config.ApiKey = null;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.FetchAsync("武汉", config));
        Assert.Equal("未配置 API Key", ex.Message);
    }

    [Fact]
    public async Task 联网搜索_解析围栏JSON并忽略工具块()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.WebSearchResponse(TwoItems)));
        var source = new WebSearchNewsSource(handler.CreateClient());
        var config = Sa1Data.Config("武汉");

        var before = DateTimeOffset.Now;
        var items = await source.FetchAsync("武汉", config);

        Assert.Equal(2, items.Count);
        Assert.Equal("标题一", items[0].Title);
        Assert.Equal("摘要一", items[0].PreSummarized);
        Assert.Equal("https://news.example.com/1", items[0].Url);
        Assert.Equal("联网搜索", items[0].Source);
        Assert.Equal("武汉", items[0].Keyword);
        Assert.NotNull(items[0].PublishedAt);
        Assert.True((items[0].PublishedAt!.Value - before).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task 联网搜索_请求体与请求头符合Anthropic联网搜索协议()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.WebSearchResponse(TwoItems)));
        var source = new WebSearchNewsSource(handler.CreateClient());
        var config = Sa1Data.Config("武汉");
        config.SearchMaxUses = 5;

        await source.FetchAsync("武汉", config);

        var request = Assert.Single(handler.Recorded);
        Assert.Equal("https://api.deepseek.com/anthropic/v1/messages", request.Url);
        Assert.Equal("test-key", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("deepseek-chat", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(2500, body.RootElement.GetProperty("max_tokens").GetInt32());

        var tool = body.RootElement.GetProperty("tools")[0];
        Assert.Equal("web_search_20250305", tool.GetProperty("type").GetString());
        Assert.Equal("web_search", tool.GetProperty("name").GetString());
        Assert.Equal(5, tool.GetProperty("max_uses").GetInt32());

        var prompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("武汉", prompt);
        Assert.Contains("严禁编造", prompt);
        Assert.Contains("JSON", prompt);
    }

    [Fact]
    public async Task 联网搜索_按每关键字条数截断()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.WebSearchResponse(TwoItems)));
        var source = new WebSearchNewsSource(handler.CreateClient());
        var config = Sa1Data.Config("武汉");
        config.ItemsPerKeyword = 1;

        var items = await source.FetchAsync("武汉", config);

        Assert.Equal("标题一", Assert.Single(items).Title);
    }

    [Fact]
    public async Task 联网搜索_文本块不是数组时返回空()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(Sa1Data.WebSearchResponse("没有找到相关新闻")));
        var source = new WebSearchNewsSource(handler.CreateClient());

        var items = await source.FetchAsync("武汉", Sa1Data.Config("武汉"));

        Assert.Empty(items);
    }
}
