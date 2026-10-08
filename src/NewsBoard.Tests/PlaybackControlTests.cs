using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>播放页手动控制：上一条 / 下一条 / 打断本轮。</summary>
[Collection(PlaybackCollection.Name)]
public class PlaybackControlTests
{
    [Fact]
    public async Task NextAsync_切到下一条()
    {
        var b = Build();
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Engine.Current?.Id == "1");

        await b.Engine.NextAsync();

        await WaitUntilAsync(() => b.Engine.Current?.Id == "2");
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task PreviousAsync_切到上一条并从末尾回绕()
    {
        var b = Build();
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Engine.Current?.Id == "1");

        await b.Engine.PreviousAsync();

        await WaitUntilAsync(() => b.Engine.Current?.Id == "3");
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task PreviousAsync_从中间往前切()
    {
        var b = Build();
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Engine.Current?.Id == "1");
        await b.Engine.NextAsync();
        await WaitUntilAsync(() => b.Engine.Current?.Id == "2");

        await b.Engine.PreviousAsync();

        await WaitUntilAsync(() => b.Engine.Current?.Id == "1");
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task 朗读中按下一跳应立刻切条_不必等念完()
    {
        var b = Build(speechEnabled: true, speechNeverCompletes: true);
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Speech.SpeakCount >= 1 && b.Engine.Current?.Id == "1");

        await b.Engine.NextAsync();

        await WaitUntilAsync(() => b.Engine.Current?.Id == "2", seconds: 2);
        Assert.True(b.Speech.StopCount >= 1, "切条时必须停掉在途朗读");
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task 朗读中打断应立刻进入黑屏并停朗读()
    {
        var b = Build(speechEnabled: true, speechNeverCompletes: true);
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Speech.SpeakCount >= 1);

        await b.Engine.InterruptAsync();

        await WaitUntilAsync(() => b.Engine.Phase == PlaybackPhase.Blackout, seconds: 2);
        Assert.True(b.Speech.StopCount >= 1, "打断时必须停掉在途朗读");
        Assert.True(b.Screen.BlackoutOnCount >= 1, "打断必须真的关屏");
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task 打断后_黑屏时长到点自动回到下一轮()
    {
        var b = Build(blackoutSeconds: 1);
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Engine.Current is not null);

        await b.Engine.InterruptAsync();
        await WaitUntilAsync(() => b.Engine.Phase == PlaybackPhase.Blackout, seconds: 2);
        await WaitUntilAsync(() => b.Engine.Phase == PlaybackPhase.Show, seconds: 5);

        Assert.True(b.Screen.BlackoutOffCount >= 1, "黑屏到点必须恢复画面");
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task 关闭亮熄循环时_手动打断仍然灭一次屏()
    {
        var b = Build(blackoutSeconds: 1, blackoutCycle: false);
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Engine.Current is not null);

        await b.Engine.InterruptAsync();

        await WaitUntilAsync(() => b.Engine.Phase == PlaybackPhase.Blackout, seconds: 2);
        await WaitUntilAsync(() => b.Engine.Phase == PlaybackPhase.Show, seconds: 5);
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task 黑屏期间再按打断_不会把黑屏等待也打断()
    {
        var b = Build(blackoutSeconds: 2);
        await b.Engine.StartAsync();
        await WaitUntilAsync(() => b.Engine.Current is not null);

        await b.Engine.InterruptAsync();
        await WaitUntilAsync(() => b.Engine.Phase == PlaybackPhase.Blackout, seconds: 2);

        await b.Engine.InterruptAsync();
        await Task.Delay(400);

        Assert.Equal(PlaybackPhase.Blackout, b.Engine.Phase);
        await b.Engine.StopAsync();
    }

    [Fact]
    public async Task 首轮无缓存时_抓到新闻立刻开播而不必等满一个窗口()
    {
        var b = Build(showSeconds: 60, items: []);
        await b.Engine.StartAsync();
        await Task.Delay(400);
        Assert.Null(b.Engine.Current);

        b.Repository.SetItems(
        [
            new NewsItem { Id = "n1", Title = "刚抓到的新闻", Summary = "摘要", Keyword = "test", Url = "https://example.com/n1" },
        ]);

        await WaitUntilAsync(() => b.Engine.Current?.Id == "n1", seconds: 3);
        Assert.Equal(PlaybackPhase.Show, b.Engine.Phase);
        await b.Engine.StopAsync();
    }

    // ---------------- 测试装置 ----------------

    private static Bundle Build(
        bool speechEnabled = false,
        bool speechNeverCompletes = false,
        int showSeconds = 30,
        int blackoutSeconds = 1,
        bool blackoutCycle = true,
        IReadOnlyList<NewsItem>? items = null)
    {
        var config = new AppConfig
        {
            SpeechEnabled = speechEnabled,
            SpeechLeadSeconds = 0,
            ItemHoldSeconds = 30,
            ShowDurationSeconds = showSeconds,
            BlackoutDurationSeconds = blackoutSeconds,
            BlackoutCycleEnabled = blackoutCycle,
            ResumeAcrossRounds = false,
            Keywords = ["test"],
            PlaybackMode = PlaybackMode.TextOnly,
        };

        var provider = new PcConfigProvider(config);
        var repository = new PcRepository(items ??
        [
            new NewsItem { Id = "1", Title = "第一条", Summary = "摘要一", Keyword = "test", Url = "https://example.com/1" },
            new NewsItem { Id = "2", Title = "第二条", Summary = "摘要二", Keyword = "test", Url = "https://example.com/2" },
            new NewsItem { Id = "3", Title = "第三条", Summary = "摘要三", Keyword = "test", Url = "https://example.com/3" },
        ]);
        var screen = new PcScreen();
        var speech = new PcSpeech(speechNeverCompletes);
        var engine = new PlaybackEngine(provider, repository, screen, speech, new SystemClock());

        return new Bundle(engine, provider, screen, speech, repository);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "等待条件超时");
    }

    private sealed record Bundle(PlaybackEngine Engine, PcConfigProvider Config, PcScreen Screen, PcSpeech Speech, PcRepository Repository);

    private sealed class PcConfigProvider(AppConfig config) : IConfigProvider
    {
        public AppConfig Current { get; } = config;

        public event Action<AppConfig>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class PcRepository(IReadOnlyList<NewsItem> items) : INewsRepository
    {
        private IReadOnlyList<NewsItem> _items = items;

        /// <summary>测试里模拟「抓取完成、仓储拿到新条目」。</summary>
        public void SetItems(IReadOnlyList<NewsItem> newItems)
        {
            _items = newItems;
            Changed?.Invoke();
        }

        public IReadOnlyList<NewsItem> GetPlaylist(AppConfig config) => _items;

        public bool Contains(string url) => _items.Any(i => i.Url == url);

        public bool IsWithinAiCooldown(AppConfig config, DateTimeOffset now) => false;

        public DateTimeOffset? LastAiCallAt => null;

        public void AddOrUpdateRange(IEnumerable<NewsItem> newItems)
        {
        }

        public void MarkAiCalled(DateTimeOffset at)
        {
        }

        public void PruneExpired(DateTimeOffset now, TimeSpan retention)
        {
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public event Action? Changed;
    }

    private sealed class PcScreen : IScreenController
    {
        private int _blackoutOn;
        private int _blackoutOff;

        public int BlackoutOnCount => _blackoutOn;

        public int BlackoutOffCount => _blackoutOff;

        public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

        public Task SetBlackoutAsync(bool black)
        {
            if (black)
            {
                Interlocked.Increment(ref _blackoutOn);
            }
            else
            {
                Interlocked.Increment(ref _blackoutOff);
            }

            return Task.CompletedTask;
        }

        public Task SetFullscreenAsync(bool fullscreen) => Task.CompletedTask;
    }

    private sealed class PcSpeech(bool neverCompletes) : ISpeechService
    {
        private int _speakCount;
        private int _stopCount;

        public int SpeakCount => _speakCount;

        public int StopCount => _stopCount;

        public Task<bool> IsAvailableAsync(string language) => Task.FromResult(true);

        public async Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _speakCount);
            if (!neverCompletes)
            {
                return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
            await tcs.Task;
        }

        public Task StopAsync()
        {
            Interlocked.Increment(ref _stopCount);
            return Task.CompletedTask;
        }
    }
}
