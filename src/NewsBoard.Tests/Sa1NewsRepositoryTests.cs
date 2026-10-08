using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

public class Sa1NewsRepositoryTests
{
    private static NewsItem Item(string url, string keyword, string title, string summary, DateTimeOffset published)
        => new()
        {
            Id = NewsSummarizer.MakeId(url),
            Title = title,
            Summary = summary,
            Url = url,
            Keyword = keyword,
            Source = "示例站",
            PublishedAt = published,
        };

    [Fact]
    public void 仓储_默认目录为LocalAppData下的NewsBoard()
    {
        var repository = new NewsRepository();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewsBoard");
        Assert.Equal(expected, repository.Directory);
        Assert.Equal(Path.Combine(expected, "news.json"), repository.FilePath);
    }

    [Fact]
    public async Task 仓储_JSON往返后条目与AI时间保持一致()
    {
        using var temp = new TempDirectory();
        var published = DateTimeOffset.Now.AddHours(-2);
        var aiCalledAt = DateTimeOffset.Now.AddMinutes(-3);

        var writer = new NewsRepository(temp.Path);
        writer.AddOrUpdateRange([Item("https://example.com/1", "武汉", "标题一", "摘要一", published)]);
        writer.MarkAiCalled(aiCalledAt);
        await writer.SaveAsync();

        var reader = new NewsRepository(temp.Path);
        await reader.LoadAsync();

        Assert.True(reader.Contains("https://example.com/1"));
        var item = Assert.Single(reader.GetPlaylist(new AppConfig()));
        Assert.Equal("标题一", item.Title);
        Assert.Equal("摘要一", item.Summary);
        Assert.Equal("示例站", item.Source);
        Assert.Equal(published.ToUnixTimeSeconds(), item.PublishedAt.ToUnixTimeSeconds());
        Assert.Equal(aiCalledAt.ToUnixTimeSeconds(), reader.LastAiCallAt!.Value.ToUnixTimeSeconds());
        Assert.False(File.Exists(writer.FilePath + ".tmp"));
    }

    [Fact]
    public async Task 仓储_缓存文件损坏时不抛异常()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "news.json"), "{ 这不是 JSON");

        var repository = new NewsRepository(temp.Path);
        await repository.LoadAsync();

        Assert.Empty(repository.GetPlaylist(new AppConfig()));
    }

    [Fact]
    public void 仓储_URL去重忽略首尾空白()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange([Item("https://example.com/1", "武汉", "标题一", "摘要一", DateTimeOffset.Now)]);

        Assert.True(repository.Contains("https://example.com/1"));
        Assert.True(repository.Contains("  https://example.com/1  "));
        Assert.False(repository.Contains("https://example.com/2"));
        Assert.False(repository.Contains(""));
    }

    [Fact]
    public void 仓储_按Id合并同一条目()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var url = "https://example.com/1";
        repository.AddOrUpdateRange([Item(url, "武汉", "标题一", "摘要一", DateTimeOffset.Now)]);
        repository.AddOrUpdateRange([Item(url, "武汉", "标题一", "摘要二（更新）", DateTimeOffset.Now)]);

        var item = Assert.Single(repository.GetPlaylist(new AppConfig()));
        Assert.Equal("摘要二（更新）", item.Summary);
    }

    [Fact]
    public void 仓储_丢弃超过保留期的条目()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.Now;
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/new", "武汉", "新", "摘要", now.AddDays(-1)),
            Item("https://example.com/old", "武汉", "旧", "摘要", now.AddDays(-4)),
        ]);

        repository.PruneExpired(now, TimeSpan.FromDays(3));

        var item = Assert.Single(repository.GetPlaylist(new AppConfig()));
        Assert.Equal("新", item.Title);
        Assert.False(repository.Contains("https://example.com/old"));
    }

    [Fact]
    public void 仓储_按关键字顺序排序组内按时间降序()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.Now;
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/a1", "美国", "美国一", "摘要", now.AddHours(-1)),
            Item("https://example.com/b1", "武汉", "武汉旧", "摘要", now.AddHours(-5)),
            Item("https://example.com/b2", "武汉", "武汉新", "摘要", now.AddHours(-2)),
            Item("https://example.com/c1", "不在列表", "其它", "摘要", now),
        ]);

        var config = new AppConfig { Keywords = ["武汉", "美国"], PlayOrder = PlayOrder.ByKeywordOrder };
        var playlist = repository.GetPlaylist(config);

        Assert.Equal(["武汉新", "武汉旧", "美国一", "其它"], playlist.Select(i => i.Title));
    }

    [Fact]
    public void 仓储_按发布时间排序()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.Now;
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/a", "武汉", "中间", "摘要", now.AddHours(-2)),
            Item("https://example.com/b", "武汉", "最新", "摘要", now),
            Item("https://example.com/c", "武汉", "最旧", "摘要", now.AddHours(-5)),
        ]);

        var playlist = repository.GetPlaylist(new AppConfig { PlayOrder = PlayOrder.ByPublishTime });

        Assert.Equal(["最新", "中间", "最旧"], playlist.Select(i => i.Title));
    }

    [Fact]
    public void 仓储_随机排序在同一实例内保持稳定()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.Now;
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange(
        [
            Item("https://example.com/1", "武汉", "一", "摘要", now.AddHours(-1)),
            Item("https://example.com/2", "武汉", "二", "摘要", now.AddHours(-2)),
            Item("https://example.com/3", "武汉", "三", "摘要", now.AddHours(-3)),
            Item("https://example.com/4", "武汉", "四", "摘要", now.AddHours(-4)),
            Item("https://example.com/5", "武汉", "五", "摘要", now.AddHours(-5)),
        ]);

        var config = new AppConfig { PlayOrder = PlayOrder.Random };
        var first = repository.GetPlaylist(config).Select(i => i.Id).ToList();
        var second = repository.GetPlaylist(config).Select(i => i.Id).ToList();

        Assert.Equal(first, second);
        Assert.Equal(5, first.Count);
        Assert.Equal(5, first.Distinct().Count());
    }

    [Fact]
    public void 仓储_随机排序在条目变化后重新洗牌()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.Now;
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange([Item("https://example.com/1", "武汉", "一", "摘要", now)]);
        var config = new AppConfig { PlayOrder = PlayOrder.Random };
        var before = repository.GetPlaylist(config).Select(i => i.Id).ToList();

        repository.AddOrUpdateRange([Item("https://example.com/2", "武汉", "二", "摘要", now)]);
        var after = repository.GetPlaylist(config).Select(i => i.Id).ToList();

        Assert.Equal(2, after.Count);
        Assert.Contains(before[0], after);
    }

    [Fact]
    public void 仓储_冷却期判定()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var config = new AppConfig { AiMinIntervalMinutes = 10 };
        var now = DateTimeOffset.Now;

        Assert.False(repository.IsWithinAiCooldown(config, now));

        repository.MarkAiCalled(now);

        Assert.True(repository.IsWithinAiCooldown(config, now));
        Assert.True(repository.IsWithinAiCooldown(config, now.AddMinutes(9)));
        Assert.False(repository.IsWithinAiCooldown(config, now.AddMinutes(10)));
    }

    [Fact]
    public void 仓储_最小调用间隔为0时不冷却()
    {
        using var temp = new TempDirectory();
        var repository = new NewsRepository(temp.Path);
        var now = DateTimeOffset.Now;
        repository.MarkAiCalled(now);

        Assert.False(repository.IsWithinAiCooldown(new AppConfig { AiMinIntervalMinutes = 0 }, now));
    }

    [Fact]
    public void 仓储_写入与清理时触发Changed事件()
    {
        using var temp = new TempDirectory();
        var now = DateTimeOffset.Now;
        var repository = new NewsRepository(temp.Path);
        var raised = 0;
        repository.Changed += () => raised++;

        repository.AddOrUpdateRange([Item("https://example.com/1", "武汉", "一", "摘要", now.AddDays(-4))]);
        repository.PruneExpired(now, TimeSpan.FromDays(3));

        Assert.Equal(2, raised);
    }
}
