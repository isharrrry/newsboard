using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

public class NewsFetchSchedulerTests
{
    [Fact]
    public async Task StartAsync_立即抓取一次_并可按间隔延时_停止后不再运行()
    {
        using var tmp = new SchedTempDir();
        var repo = new NewsRepository(tmp.Path);
        await repo.LoadAsync();

        var config = new AppConfig
        {
            FetchMode = NewsFetchMode.WebSearch,
            Keywords = ["武汉"],
            ItemsPerKeyword = 2,
            OnlyTodayNews = false,
            AiMinIntervalMinutes = 0,
            FetchIntervalMinutes = 5,
        };
        var provider = new SchedConfigProvider(config);
        var clock = new SchedClock();
        var coordinator = new NewsFetchCoordinator(
            [new SchedFakeSource()],
            new NewsSummarizer(new HttpClient()),
            new ImageResolver(new HttpClient()),
            repo,
            provider);

        await using var scheduler = new NewsFetchScheduler(coordinator, provider, clock);

        var firstDone = new TaskCompletionSource();
        scheduler.Changed += () => firstDone.TrySetResult();

        await scheduler.StartAsync();
        var winner = await Task.WhenAny(firstDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(firstDone.Task, winner);

        Assert.True(scheduler.IsRunning);
        Assert.NotNull(scheduler.LastResult);
        Assert.Null(scheduler.LastWarning);
        Assert.NotEmpty(repo.GetPlaylist(config));

        // 第一次抓取结束后进入定时等待，等待时长 = 配置的抓取间隔
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Delays.First());

        await scheduler.StopAsync();
        Assert.False(scheduler.IsRunning);
    }

    [Fact]
    public async Task FetchNowAsync_抓取失败只记警告不抛出()
    {
        using var tmp = new SchedTempDir();
        var repo = new NewsRepository(tmp.Path);
        await repo.LoadAsync();

        var config = new AppConfig
        {
            FetchMode = NewsFetchMode.WebSearch,
            Keywords = ["武汉"],
            AiMinIntervalMinutes = 0,
        };
        var provider = new SchedConfigProvider(config);
        var coordinator = new NewsFetchCoordinator(
            [new SchedFakeSource(throwing: true)],
            new NewsSummarizer(new HttpClient()),
            new ImageResolver(new HttpClient()),
            repo,
            provider);

        await using var scheduler = new NewsFetchScheduler(coordinator, provider, new SchedClock());

        var exception = await Record.ExceptionAsync(() => scheduler.FetchNowAsync());

        Assert.Null(exception);
        Assert.NotNull(scheduler.LastWarning);
        Assert.Empty(repo.GetPlaylist(config));
    }

    [Fact]
    public async Task 抓取间隔最小为1分钟()
    {
        using var tmp = new SchedTempDir();
        var repo = new NewsRepository(tmp.Path);
        await repo.LoadAsync();

        var config = new AppConfig
        {
            FetchMode = NewsFetchMode.WebSearch,
            Keywords = ["武汉"],
            AiMinIntervalMinutes = 0,
            FetchIntervalMinutes = 0,
        };
        var provider = new SchedConfigProvider(config);
        var clock = new SchedClock();
        var coordinator = new NewsFetchCoordinator(
            [new SchedFakeSource()],
            new NewsSummarizer(new HttpClient()),
            new ImageResolver(new HttpClient()),
            repo,
            provider);

        await using var scheduler = new NewsFetchScheduler(coordinator, provider, clock);

        var firstDone = new TaskCompletionSource();
        scheduler.Changed += () => firstDone.TrySetResult();
        await scheduler.StartAsync();
        var winner = await Task.WhenAny(firstDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(firstDone.Task, winner);

        Assert.Equal(TimeSpan.FromMinutes(1), clock.Delays.First());
        await scheduler.StopAsync();
    }

    private sealed class SchedFakeSource(bool throwing = false) : INewsSource
    {
        public string Name => "模型联网搜索";

        public Task<IReadOnlyList<RawNewsItem>> FetchAsync(string keyword, AppConfig config, CancellationToken cancellationToken = default)
        {
            if (throwing)
            {
                throw new InvalidOperationException("模拟联网失败");
            }

            IReadOnlyList<RawNewsItem> items =
            [
                new RawNewsItem
                {
                    Keyword = keyword,
                    Title = $"标题-{keyword}",
                    Url = $"https://example.com/{keyword}/{Guid.NewGuid():N}",
                    PreSummarized = "这是模型直接给出的摘要，用于调度器测试。",
                    Source = "联网搜索",
                    PublishedAt = DateTimeOffset.Now,
                },
            ];
            return Task.FromResult(items);
        }
    }

    private sealed class SchedConfigProvider(AppConfig config) : IConfigProvider
    {
        public AppConfig Current { get; } = config;

        public event Action<AppConfig>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class SchedClock : IClock
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<TimeSpan> Delays { get; } = [];

        public DateTimeOffset Now => DateTimeOffset.Now;

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            Delays.Add(delay);
            using var registration = cancellationToken.Register(() => _gate.TrySetResult());
            await _gate.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class SchedTempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "nb-sched-" + Guid.NewGuid().ToString("N")[..8]);

        public SchedTempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
