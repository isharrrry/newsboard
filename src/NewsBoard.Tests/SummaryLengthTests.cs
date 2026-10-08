using System.Net;
using System.Text.Json;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>摘要长度策略：默认 70 字、提示词要求写成 1–2 句、超长仍被本地截断（上限 1.2 倍）。</summary>
public class SummaryLengthTests
{
    [Fact]
    public void 默认配置_摘要上限70字且不显示标题()
    {
        var config = new AppConfig();

        Assert.Equal(70, config.SummaryMaxChars);
        Assert.False(config.ShowTitle);
        Assert.True(config.ShowKeywordLabel);
    }

    [Fact]
    public async Task 提炼提示词_按配置字数要求写1到2句而非一句话()
    {
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(OpenAiResponse("[]")));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var config = new AppConfig { ApiKey = "test-key", SummaryMaxChars = 200 };
        var raw = new[]
        {
            new RawNewsItem { Keyword = "武汉", Url = "https://example.com/a", Title = "标题", Description = "正文片段" },
        };

        await summarizer.SummarizeAsync("武汉", raw, config);

        var body = handler.Recorded.Single().Body;
        Assert.NotNull(body);
        using var document = JsonDocument.Parse(body!);
        var prompt = document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();

        Assert.NotNull(prompt);
        Assert.Contains("200", prompt!);
        Assert.Contains("1–2 句", prompt!);
    }

    [Fact]
    public async Task 模型返回超长摘要_按句号截断到上限的12倍以内()
    {
        var url = "https://example.com/long";
        var longSummary = string.Concat(Enumerable.Repeat("这是完整事实句。", 40));
        Assert.True(longSummary.Length > 120 * 1.2);

        var payload = JsonSerializer.Serialize(new[]
        {
            new { url, summary = longSummary },
        });
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(OpenAiResponse(payload)));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var config = new AppConfig { ApiKey = "test-key", SummaryMaxChars = 120 };
        var raw = new[] { new RawNewsItem { Keyword = "武汉", Url = url, Title = "标题", Description = "正文" } };

        var items = await summarizer.SummarizeAsync("武汉", raw, config);

        var item = Assert.Single(items);
        Assert.True(item.Summary.Length <= 120 * 1.2, $"摘要应被截断，实际 {item.Summary.Length} 字");
        Assert.EndsWith("。", item.Summary);
        Assert.True(item.Summary.Length > 100, "截断不应退化成一句话");
    }

    [Fact]
    public async Task 正常长度摘要_原样保留不被截断()
    {
        var url = "https://example.com/ok";
        var summary = "中国人民银行连续第23个月增持黄金，10月末黄金储备环比继续上升。分析人士认为，在全球央行普遍增持的背景下，这一趋势短期难以逆转。";
        Assert.True(summary.Length <= 70);

        var payload = JsonSerializer.Serialize(new[] { new { url, summary } });
        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(OpenAiResponse(payload)));
        var summarizer = new NewsSummarizer(handler.CreateClient());
        var config = new AppConfig { ApiKey = "test-key" };
        var raw = new[] { new RawNewsItem { Keyword = "中国", Url = url, Title = "标题", Description = "正文" } };

        var items = await summarizer.SummarizeAsync("中国", raw, config);

        Assert.Equal(summary, Assert.Single(items).Summary);
    }

    private static string OpenAiResponse(string content)
        => JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } },
        });

    [Fact]
    public async Task 联网搜索通道_模型超字数时本地按句号截断()
    {
        var url = "https://example.com/web-长文";
        var longSummary = string.Concat(Enumerable.Repeat("检索到的完整事实句。", 40));
        var json = JsonSerializer.Serialize(new[] { new { title = "标题", summary = longSummary, url, keyword = "武汉" } });
        var anthropic = JsonSerializer.Serialize(new
        {
            content = new object[]
            {
                new { type = "server_tool_use", name = "web_search" },
                new { type = "text", text = json },
            },
        });

        var handler = new FakeHttpHandler(_ => FakeHttp.Ok(anthropic));
        var source = new WebSearchNewsSource(handler.CreateClient());
        var config = new AppConfig { ApiKey = "test-key", SummaryMaxChars = 120, Keywords = ["武汉"] };

        var raw = await source.FetchAsync("武汉", config);

        var item = Assert.Single(raw);
        Assert.NotNull(item.PreSummarized);
        Assert.True(item.PreSummarized!.Length <= 120 * 1.2, $"摘要应被截断，实际 {item.PreSummarized.Length} 字");
        Assert.EndsWith("。", item.PreSummarized);
    }

    [Theory]
    [InlineData(null, 120, "")]
    [InlineData("  短摘要。  ", 120, "短摘要。")]
    [InlineData("第一句。第二句。", 3, "第一句。")]   // 上限 4 字：截到最后一个句号
    [InlineData("第一句。第二句。", 2, "第一")]       // 上限 2 字内没有句号：硬截断
    public void SummaryText_规整与截断(string? input, int maxChars, string expected)
    {
        Assert.Equal(expected, SummaryText.Normalize(input, maxChars));
    }
}
