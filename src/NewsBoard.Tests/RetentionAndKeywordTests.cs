using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>保留窗口按自然日分桶 + 共享频道源的条目按配置关键字归类。</summary>
public class RetentionAndKeywordTests
{
    private static readonly TimeZoneInfo Beijing =
        TimeZoneInfo.CreateCustomTimeZone("test-utc+8", TimeSpan.FromHours(8), "test-utc+8", "test-utc+8");

    [Fact]
    public void 保留三天_窗口起点是前天的本地零点()
    {
        var now = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(8));

        var earliest = RetentionWindow.EarliestKeptInstant(now, 3, Beijing);

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(8)), earliest);
    }

    [Fact]
    public void 保留一天_窗口起点是今天零点()
    {
        var now = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.FromHours(8)),
            RetentionWindow.EarliestKeptInstant(now, 1, Beijing));
    }

    [Fact]
    public void 保留天数非正数时按一天处理()
    {
        var now = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(
            RetentionWindow.EarliestKeptInstant(now, 1, Beijing),
            RetentionWindow.EarliestKeptInstant(now, 0, Beijing));
    }

    [Fact]
    public void 保留跨度等于当前时刻到窗口起点()
    {
        var now = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(8));

        Assert.Equal(TimeSpan.FromHours(63), RetentionWindow.RetentionSpan(now, 3, Beijing));
    }

    [Fact]
    public void 自然日分桶_同一天里无论几点看到的是同一个窗口()
    {
        var morning = new DateTimeOffset(2026, 10, 8, 6, 0, 0, TimeSpan.FromHours(8));
        var night = new DateTimeOffset(2026, 10, 8, 23, 30, 0, TimeSpan.FromHours(8));

        Assert.Equal(
            RetentionWindow.EarliestKeptInstant(morning, 3, Beijing),
            RetentionWindow.EarliestKeptInstant(night, 3, Beijing));
    }

    [Fact]
    public void 清理按自然日切_前天零点整保留_再早一分钟清掉()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        var now = new DateTimeOffset(2026, 10, 8, 15, 0, 0, TimeSpan.FromHours(8));

        repository.AddOrUpdateRange(
        [
            Item("keep", new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.FromHours(8))),
            Item("drop", new DateTimeOffset(2026, 10, 5, 23, 59, 0, TimeSpan.FromHours(8))),
        ]);

        repository.PruneExpired(now, RetentionWindow.RetentionSpan(now, 3, Beijing));

        var left = repository.Snapshot();
        Assert.Single(left);
        Assert.Equal("keep", left[0].Id);
    }

    [Fact]
    public void 关键字归类_标题命中时按配置顺序给分类()
    {
        var keywords = new[] { "武汉", "AI" };

        Assert.Equal("武汉", KeywordClassifier.Classify("武汉地铁新线开通", "正文", keywords, "频道"));
        Assert.Equal("AI", KeywordClassifier.Classify("新一代AI芯片发布", "正文", keywords, "频道"));
    }

    [Fact]
    public void 关键字归类_标题没命中时用正文兜底()
    {
        var keywords = new[] { "AI" };

        Assert.Equal("AI", KeywordClassifier.Classify("新一代芯片发布", "该模型由人工智能实验室推出，主打国产 AI 算力。", keywords, "频道"));
    }

    [Fact]
    public void 关键字归类_都不命中时回落到频道名()
    {
        var keywords = new[] { "武汉", "AI" };

        Assert.Equal("中新网即时新闻", KeywordClassifier.Classify("国庆假期旅游数据出炉", "多地景区客流创新高", keywords, "中新网即时新闻"));
    }

    [Fact]
    public void 关键字归类_没有配置关键字时直接回落()
    {
        Assert.Equal("频道", KeywordClassifier.Classify("任意标题", "任意正文", [], "频道"));
        Assert.Equal("频道", KeywordClassifier.Classify(null, null, ["武汉"], "频道"));
    }

    [Fact]
    public void 关键字归类_英文关键字不区分大小写且返回配置里的写法()
    {
        Assert.Equal("ai", KeywordClassifier.Classify("AI 算力需求激增", null, ["ai"], "频道"));
    }

    private static NewsItem Item(string id, DateTimeOffset publishedAt) => new()
    {
        Id = id,
        Title = id,
        Summary = id,
        Url = $"https://example.com/{id}",
        Keyword = "武汉",
        PublishedAt = publishedAt,
    };

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "nb-retention-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }
}
