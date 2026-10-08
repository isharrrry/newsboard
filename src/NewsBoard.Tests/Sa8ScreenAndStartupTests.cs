using System.Text.Json;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// SA8：真灭屏的安全护栏/幂等（任务 1），以及「首帧闸门」——
/// 首条朗读必须等画面画出首帧，避免「已经在念、画面还在加载」（任务 2）。
/// </summary>
public sealed class Sa8ScreenAndStartupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nb-sa8-" + Guid.NewGuid().ToString("N")[..8]);

    public Sa8ScreenAndStartupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    // ---------------- 任务 1：真灭屏的判定与幂等 ----------------

    [Fact]
    public void 护栏_非前台且非全屏时不允许关屏()
    {
        Assert.False(MonitorPowerPolicy.CanCutMonitor(appIsForeground: false, borderlessFullscreen: false));
        Assert.True(MonitorPowerPolicy.CanCutMonitor(appIsForeground: true, borderlessFullscreen: false));
        Assert.True(MonitorPowerPolicy.CanCutMonitor(appIsForeground: false, borderlessFullscreen: true));
    }

    [Fact]
    public void 护栏挡住时_黑屏只保留遮罩_不发任何显示器指令()
    {
        var policy = new MonitorPowerPolicy();

        Assert.Equal(MonitorPowerAction.None, policy.Next(black: true, appIsForeground: false, borderlessFullscreen: false));
        Assert.False(policy.IsOff);

        // 显示期间（black=false）也不该白发一次「开屏」
        Assert.Equal(MonitorPowerAction.None, policy.Next(black: false, appIsForeground: true, borderlessFullscreen: false));
    }

    [Fact]
    public void 前台时关屏_重复进入黑屏不会重复关屏()
    {
        var policy = new MonitorPowerPolicy();

        Assert.Equal(MonitorPowerAction.Off, policy.Next(black: true, appIsForeground: true, borderlessFullscreen: false));
        Assert.True(policy.IsOff);

        // 幂等：还在黑屏里再报一次，什么都不做
        Assert.Equal(MonitorPowerAction.None, policy.Next(black: true, appIsForeground: true, borderlessFullscreen: false));
        Assert.True(policy.IsOff);
    }

    [Fact]
    public void 退出黑屏开屏一次_之后不再重复开屏()
    {
        var policy = new MonitorPowerPolicy();
        policy.Next(black: true, appIsForeground: true, borderlessFullscreen: false);

        Assert.Equal(MonitorPowerAction.On, policy.Next(black: false, appIsForeground: true, borderlessFullscreen: false));
        Assert.False(policy.IsOff);
        Assert.Equal(MonitorPowerAction.None, policy.Next(black: false, appIsForeground: true, borderlessFullscreen: false));
    }

    [Fact]
    public void 关屏状态下窗口失去前台_退出黑屏仍然会把屏幕打开()
    {
        var policy = new MonitorPowerPolicy();
        policy.Next(black: true, appIsForeground: true, borderlessFullscreen: false);

        // 关屏后用户点了别处（前台判定变 false）：绝不能因此把「开屏」也吞掉，否则屏幕醒不过来
        Assert.Equal(MonitorPowerAction.On, policy.Next(black: false, appIsForeground: false, borderlessFullscreen: false));
        Assert.False(policy.IsOff);
    }

    [Fact]
    public void 一整轮熄亮循环_每次黑屏都关一次屏()
    {
        var policy = new MonitorPowerPolicy();
        var actions = new List<MonitorPowerAction>();
        for (var i = 0; i < 2; i++)
        {
            actions.Add(policy.Next(true, true, false));
            actions.Add(policy.Next(false, true, false));
        }

        Assert.Equal(
            new[] { MonitorPowerAction.Off, MonitorPowerAction.On, MonitorPowerAction.Off, MonitorPowerAction.On },
            actions);
    }

    // ---------------- 任务 2：首帧闸门 ----------------

    [Fact]
    public async Task 未开启首帧闸门时_首条朗读不受影响()
    {
        var store = await BuildStoreAsync();
        var speech = new CountingSpeech();
        await using var engine = new PlaybackEngine(store.Config, store.Repository, new SilentScreen(), speech, new SystemClock());

        await engine.StartAsync();
        await WaitUntilAsync(() => speech.SpeakCount >= 1);

        Assert.True(speech.SpeakCount >= 1);
        await engine.StopAsync();
    }

    [Fact]
    public async Task 开启首帧闸门后_首条朗读要等首帧报告()
    {
        var store = await BuildStoreAsync();
        var speech = new CountingSpeech();
        await using var engine = new PlaybackEngine(store.Config, store.Repository, new SilentScreen(), speech, new SystemClock());

        engine.ArmFirstFrameGate();
        await engine.StartAsync();

        // 条目已经选中（画面该画了），但还没报告首帧 → 不能出声
        await WaitUntilAsync(() => engine.Current is not null);
        await Task.Delay(600);
        Assert.Equal(0, speech.SpeakCount);

        // 报告首帧 → 立刻放行
        engine.MarkFirstFrameRendered();
        await WaitUntilAsync(() => speech.SpeakCount >= 1);
        Assert.True(speech.SpeakCount >= 1);

        await engine.StopAsync();
    }

    [Fact]
    public async Task 首帧闸门只拦第一条_后续条目不再等()
    {
        var store = await BuildStoreAsync();
        var speech = new CountingSpeech();
        await using var engine = new PlaybackEngine(store.Config, store.Repository, new SilentScreen(), speech, new SystemClock());

        engine.ArmFirstFrameGate();
        await engine.StartAsync();
        await WaitUntilAsync(() => engine.Current is not null);
        engine.MarkFirstFrameRendered();

        await WaitUntilAsync(() => speech.SpeakCount >= 1);

        // 之后即便重新 arm，也不该再拦（闸门只对首条生效）
        engine.ArmFirstFrameGate();
        var before = speech.SpeakCount;
        await WaitUntilAsync(() => speech.SpeakCount > before);

        await engine.StopAsync();
    }

    // ---------------- 测试脚手架 ----------------

    private async Task<(IConfigProvider Config, INewsRepository Repository)> BuildStoreAsync()
    {
        var config = new AppConfig
        {
            SpeechEnabled = true,
            SpeechLeadSeconds = 0,
            ShowDurationSeconds = 60,
            BlackoutCycleEnabled = false,
            Keywords = ["测试"],
            OnlyTodayNews = false,
            TtsUnavailable = TtsUnavailablePolicy.FallbackHold,
        };

        var items = new[]
        {
            new NewsItem
            {
                Id = "sa8-1",
                Title = "第一条",
                Summary = "第一条摘要。",
                Url = "https://example.com/1",
                Keyword = "测试",
                Source = "单测",
                PublishedAt = DateTimeOffset.Now,
            },
            new NewsItem
            {
                Id = "sa8-2",
                Title = "第二条",
                Summary = "第二条摘要。",
                Url = "https://example.com/2",
                Keyword = "测试",
                Source = "单测",
                PublishedAt = DateTimeOffset.Now,
            },
        };

        var payload = new { LastAiCallAt = (DateTimeOffset?)null, Items = items };
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "news.json"),
            JsonSerializer.Serialize(payload));

        var repository = new NewsRepository(_dir);
        await repository.LoadAsync();
        return (new FixedConfig(config), repository);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "等待条件超时");
    }

    private sealed class FixedConfig(AppConfig config) : IConfigProvider
    {
        public AppConfig Current { get; } = config;

        public event Action<AppConfig>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class SilentScreen : IScreenController
    {
        public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

        public Task SetBlackoutAsync(bool black) => Task.CompletedTask;

        public Task SetFullscreenAsync(bool fullscreen) => Task.CompletedTask;
    }

    private sealed class CountingSpeech : ISpeechService
    {
        private int _speakCount;

        public int SpeakCount => Volatile.Read(ref _speakCount);

        public Task<bool> IsAvailableAsync(string language) => Task.FromResult(true);

        public async Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _speakCount);
            // 模拟「念一句要花点时间」，避免引擎空转
            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync() => Task.CompletedTask;
    }
}
