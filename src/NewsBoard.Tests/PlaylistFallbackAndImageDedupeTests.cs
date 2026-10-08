using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>门槛过滤后播放列表为空要补一条（带 ~ 标记）；同一张配图只能出现在一条新闻里。</summary>
public class PlaylistFallbackAndImageDedupeTests
{
    [Fact]
    public void 门槛把条目全筛掉时_随机补一条并在摘要末尾加波浪号()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange([Item("a", 0.3), Item("b", 0.5)]);

        var playlist = repository.GetPlaylist(new AppConfig { ImportanceThreshold = 0.7 });

        var only = Assert.Single(playlist);
        Assert.EndsWith(NewsRepository.BelowThresholdSuffix, only.Summary);
        Assert.Contains(only.Id, new[] { "a", "b" });
    }

    [Fact]
    public void 补上来的那条只是副本_缓存里的原始摘要不被污染()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange([Item("a", 0.3)]);

        repository.GetPlaylist(new AppConfig { ImportanceThreshold = 0.7 });

        Assert.Equal("a", repository.Snapshot()[0].Summary);
    }

    [Fact]
    public void 有达标条目时不做补齐()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        repository.AddOrUpdateRange([Item("good", 0.9), Item("low", 0.2)]);

        var playlist = repository.GetPlaylist(new AppConfig { ImportanceThreshold = 0.7 });

        var only = Assert.Single(playlist);
        Assert.Equal("good", only.Id);
        Assert.DoesNotContain(NewsRepository.BelowThresholdSuffix, only.Summary);
    }

    [Fact]
    public void 缓存为空时播放列表也是空的()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);

        Assert.Empty(repository.GetPlaylist(new AppConfig()));
    }

    [Fact]
    public void 未评分的旧缓存条目不算被门槛筛掉_不需要补()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        var item = Item("legacy", null);
        repository.AddOrUpdateRange([item]);

        var playlist = repository.GetPlaylist(new AppConfig { ImportanceThreshold = 0.7 });

        Assert.Equal("legacy", Assert.Single(playlist).Id);
    }

    [Fact]
    public void 同一张配图只留给最新的一条_其余按无图处理()
    {
        var older = Item("older", 0.9);
        older.ImageUrl = "https://img.example.com/shared.jpg";
        older.PublishedAt = new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.FromHours(8));
        var newer = Item("newer", 0.9);
        newer.ImageUrl = "https://img.example.com/shared.jpg";
        newer.PublishedAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(8));
        var solo = Item("solo", 0.9);
        solo.ImageUrl = "https://img.example.com/own.jpg";
        solo.PublishedAt = new DateTimeOffset(2026, 10, 8, 11, 0, 0, TimeSpan.FromHours(8));

        var cleared = ImageResolver.DedupeImages([older, newer, solo]);

        Assert.Equal(1, cleared);
        Assert.Null(older.ImageUrl);
        Assert.Equal("https://img.example.com/shared.jpg", newer.ImageUrl);
        Assert.Equal("https://img.example.com/own.jpg", solo.ImageUrl);
    }

    [Fact]
    public void 配图去重对空图与空白图不计数()
    {
        var a = Item("a", 0.9);
        a.ImageUrl = "   ";
        var b = Item("b", 0.9);

        Assert.Equal(0, ImageResolver.DedupeImages([a, b]));
    }

    private static NewsItem Item(string id, double? importance) => new()
    {
        Id = id,
        Title = id,
        Summary = id,
        Url = $"https://example.com/{id}",
        Keyword = "武汉",
        PublishedAt = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.FromHours(8)),
        Importance = importance,
    };

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "nb-playlist-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }
}
