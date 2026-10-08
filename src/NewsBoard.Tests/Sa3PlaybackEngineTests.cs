using System.Diagnostics;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

// ---------------------------------------------------------------------------
// 测试替身
//
// 时钟用「缩放后的真实时钟」：虚拟时间 = 真实时间 × 倍率。
// 这样引擎里所有基于 IClock 的等待（看门狗、窗口、黑屏）都真实可跑，
// 但一个 300 秒的黑屏在测试里只需 1.2 秒，且不需要伪造时间流逝。
// ---------------------------------------------------------------------------

internal sealed class ScaledClock : IClock
{
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly double _scale;

    public ScaledClock(double scale)
    {
        _scale = scale;
    }

    public TimeSpan Elapsed => TimeSpan.FromSeconds(_sw.Elapsed.TotalSeconds * _scale);

    public DateTimeOffset Now => DateTimeOffset.UnixEpoch + Elapsed;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        => delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromSeconds(delay.TotalSeconds / _scale), cancellationToken);
}

internal sealed class ScreenCall
{
    public required string Kind { get; init; }

    public required bool Value { get; init; }

    public required TimeSpan At { get; init; }
}

internal sealed class Sa3FakeScreen : IScreenController
{
    private readonly ScaledClock _clock;
    private readonly List<ScreenCall> _calls = [];

    public Sa3FakeScreen(ScaledClock clock)
    {
        _clock = clock;
    }

    public IReadOnlyList<ScreenCall> Calls { get { lock (_calls) { return [.. _calls]; } } }

    public bool? KeepScreenOn { get; private set; }

    public bool Blackout { get; private set; }

    public bool? Fullscreen { get; private set; }

    public Task SetKeepScreenOnAsync(bool on)
    {
        lock (_calls)
        {
            KeepScreenOn = on;
            _calls.Add(new ScreenCall { Kind = "KeepScreenOn", Value = on, At = _clock.Elapsed });
        }

        return Task.CompletedTask;
    }

    public Task SetBlackoutAsync(bool black)
    {
        lock (_calls)
        {
            Blackout = black;
            _calls.Add(new ScreenCall { Kind = "Blackout", Value = black, At = _clock.Elapsed });
        }

        return Task.CompletedTask;
    }

    public Task SetFullscreenAsync(bool fullscreen)
    {
        lock (_calls)
        {
            Fullscreen = fullscreen;
            _calls.Add(new ScreenCall { Kind = "Fullscreen", Value = fullscreen, At = _clock.Elapsed });
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<ScreenCall> CallsOf(string kind) => [.. Calls.Where(c => c.Kind == kind)];
}

internal sealed class SpeechAttempt
{
    public required string Text { get; init; }

    public required double Rate { get; init; }

    public required double Pitch { get; init; }

    public required string Language { get; init; }

    public required TimeSpan StartedAt { get; init; }

    public bool Cancelled { get; set; }

    public bool InFlight { get; set; }
}

/// <summary>可控完成时机的 TTS：默认按 SpeechDuration 自动念完，Finish() 可提前结束。</summary>
internal sealed class Sa3FakeSpeech : ISpeechService
{
    private readonly ScaledClock _clock;
    private readonly List<SpeechAttempt> _attempts = [];
    private readonly Dictionary<SpeechAttempt, TaskCompletionSource> _pending = [];
    private readonly object _gate = new();

    public Sa3FakeSpeech(ScaledClock clock)
    {
        _clock = clock;
    }

    public bool Available { get; set; } = true;

    /// <summary>朗读占用时间占估算时长的比例（1.0 = 与估算一致；设很大即「念不完」）。</summary>
    public double SpeechDurationFactor { get; set; } = 1.0;

    public bool AutoFinish { get; set; } = true;

    public int StopCount { get; private set; }

    public int ProbeCount { get; private set; }

    public IReadOnlyList<SpeechAttempt> Attempts { get { lock (_gate) { return [.. _attempts]; } } }

    public SpeechAttempt? LastAttempt
    {
        get
        {
            lock (_gate)
            {
                return _attempts.LastOrDefault(a => !a.Cancelled);
            }
        }
    }

    /// <summary>当前正在朗读但尚未结束的条数。</summary>
    public int InFlightCount
    {
        get
        {
            lock (_gate)
            {
                return _attempts.Count(a => a.InFlight);
            }
        }
    }

    public Task<bool> IsAvailableAsync(string language)
    {
        ProbeCount++;
        return Task.FromResult(Available);
    }

    public Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var attempt = new SpeechAttempt
        {
            Text = text,
            Rate = rate,
            Pitch = pitch,
            Language = language,
            StartedAt = _clock.Elapsed,
            InFlight = true
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _attempts.Add(attempt);
            _pending[attempt] = tcs;
        }

        cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                attempt.Cancelled = true;
                attempt.InFlight = false;
                _pending.Remove(attempt);
            }

            tcs.TrySetResult();
        });

        if (AutoFinish)
        {
            var seconds = PlaybackEngine.EstimateSpeechSeconds(text, rate) * SpeechDurationFactor;
            _ = FinishAfterAsync(attempt, TimeSpan.FromSeconds(seconds));
        }

        return tcs.Task;
    }

    private async Task FinishAfterAsync(SpeechAttempt attempt, TimeSpan after)
    {
        if (after > TimeSpan.Zero)
        {
            await _clock.DelayAsync(after).ConfigureAwait(false);
        }

        Complete(attempt);
    }

    /// <summary>模拟朗读自然结束（完成那一刻 = 该条播放结束）。</summary>
    public void Finish()
    {
        SpeechAttempt? attempt;
        lock (_gate)
        {
            attempt = _attempts.LastOrDefault(a => a.InFlight);
        }

        if (attempt is not null)
        {
            Complete(attempt);
        }
    }

    private void Complete(SpeechAttempt attempt)
    {
        TaskCompletionSource? tcs;
        lock (_gate)
        {
            if (!_pending.Remove(attempt, out tcs))
            {
                return;
            }

            attempt.InFlight = false;
        }

        tcs.TrySetResult();
    }

    public Task StopAsync()
    {
        StopCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeRepository : INewsRepository
{
    private readonly List<NewsItem> _items = [];

    public IReadOnlyList<NewsItem> Items => _items;

    public event Action? Changed;

    public int LoadCount { get; private set; }

    public IReadOnlyList<NewsItem> GetPlaylist(AppConfig config) => _items;

    public bool Contains(string url) => _items.Any(i => i.Url == url);

    public bool IsWithinAiCooldown(AppConfig config, DateTimeOffset now) => false;

    public DateTimeOffset? LastAiCallAt => null;

    public void AddOrUpdateRange(IEnumerable<NewsItem> items) => _items.AddRange(items);

    public void MarkAiCalled(DateTimeOffset at) { }

    public void PruneExpired(DateTimeOffset now, TimeSpan retention) { }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        LoadCount++;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class Sa3FakeConfigProvider : IConfigProvider
{
    public Sa3FakeConfigProvider(AppConfig config)
    {
        Current = config;
    }

    public AppConfig Current { get; set; }

    public event Action<AppConfig>? Changed;

    public void Raise() => Changed?.Invoke(Current);
}

internal sealed class PhaseMark
{
    public required PlaybackPhase Phase { get; init; }

    public required TimeSpan At { get; init; }
}

internal sealed class EngineHarness : IAsyncDisposable
{
    public EngineHarness(ScaledClock clock, AppConfig config, params NewsItem[] items)
    {
        Clock = clock;
        Config = config;
        Provider = new Sa3FakeConfigProvider(config);
        Repository = new FakeRepository();
        Repository.AddOrUpdateRange(items);
        Screen = new Sa3FakeScreen(clock);
        Speech = new Sa3FakeSpeech(clock);
        Engine = new PlaybackEngine(Provider, Repository, Screen, Speech, clock);
        Engine.StateChanged += () =>
        {
            lock (Marks)
            {
                Marks.Add(new PhaseMark { Phase = Engine.Phase, At = clock.Elapsed });
            }
        };
    }

    public ScaledClock Clock { get; }

    public AppConfig Config { get; }

    public Sa3FakeConfigProvider Provider { get; }

    public FakeRepository Repository { get; }

    public Sa3FakeScreen Screen { get; }

    public Sa3FakeSpeech Speech { get; }

    public PlaybackEngine Engine { get; }

    public List<PhaseMark> Marks { get; } = [];

    /// <summary>等到引擎**在 StateChanged 事件里**进入指定阶段（不会漏掉短暂的中间阶段）。</summary>
    public Task WaitForPhaseAsync(PlaybackPhase phase, string what)
    {
        lock (Marks)
        {
            if (Engine.Phase == phase)
            {
                return Task.CompletedTask;
            }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action handler = () =>
        {
            if (Engine.Phase == phase)
            {
                tcs.TrySetResult();
            }
        };

        Engine.StateChanged += handler;
        if (Engine.Phase == phase)
        {
            tcs.TrySetResult();
        }

        return AwaitThenDetachAsync(tcs.Task, handler, what);
    }

    /// <summary>等到引擎进入指定索引。</summary>
    public Task WaitForIndexAsync(int index, string what)
    {
        if (Engine.CurrentIndex == index)
        {
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action handler = () =>
        {
            if (Engine.CurrentIndex == index)
            {
                tcs.TrySetResult();
            }
        };

        Engine.StateChanged += handler;
        if (Engine.CurrentIndex == index)
        {
            tcs.TrySetResult();
        }

        return AwaitThenDetachAsync(tcs.Task, handler, what);
    }

    private async Task AwaitThenDetachAsync(Task signal, Action handler, string what)
    {
        try
        {
            await signal.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"等待超时：{what}");
        }
        finally
        {
            Engine.StateChanged -= handler;
        }
    }

    /// <summary>某一阶段首次被观察到的时刻（由 StateChanged 事件序列证据得出）。</summary>
    public TimeSpan? FirstPhaseAt(PlaybackPhase phase)
    {
        lock (Marks)
        {
            foreach (var mark in Marks)
            {
                if (mark.Phase == phase)
                {
                    return mark.At;
                }
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await Engine.StopAsync();
        await Engine.DisposeAsync();
    }
}

// ---------------------------------------------------------------------------
// 测试
// ---------------------------------------------------------------------------

[Collection(PlaybackCollection.Name)]
public sealed class Sa3PlaybackEngineTests
{
    /// <summary>虚拟时间倍率：60 秒展示窗口在测试里约 0.6 秒，便于观察相位切换。</summary>
    private const double Scale = 100;

    private static NewsItem Item(string id, string summary, string keyword = "武汉")
        => new()
        {
            Id = id,
            Title = $"标题{id}",
            Summary = summary,
            Keyword = keyword,
            Url = $"https://example.com/{id}",
            PublishedAt = DateTimeOffset.Now
        };

    private static AppConfig Config(Action<AppConfig>? tweak = null)
    {
        var config = new AppConfig
        {
            SpeechEnabled = true,
            SpeechContent = SpeechContent.SummaryOnly,
            SpeechRate = 1.0,
            SpeechLeadSeconds = 1,
            ShowDurationSeconds = 60,
            ItemHoldSeconds = 12,
            BlackoutDurationSeconds = 300,
            BlackoutCycleEnabled = true,
            WindowEndBehavior = WindowEndBehavior.FinishCurrentItem,
            TtsUnavailable = TtsUnavailablePolicy.FallbackHold,
            QuietHours = "",
            KeepScreenOn = true
        };
        tweak?.Invoke(config);
        return config;
    }

    private static TimeSpan Estimate(string text, double rate = 1.0)
        => TimeSpan.FromSeconds(PlaybackEngine.EstimateSpeechSeconds(text, rate));

    /// <summary>等待条件成立（真实时间 10 秒上限，避免任何挂起拖死测试）。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"等待超时：{what}（已等 {sw.Elapsed.TotalSeconds:F1}s 真实时间）");
    }

    private static Task WaitForPhaseAsync(EngineHarness h, PlaybackPhase phase, string what)
        => h.WaitForPhaseAsync(phase, what);

    private static Task WaitForIndexAsync(EngineHarness h, int index, string what)
        => h.WaitForIndexAsync(index, what);

    [Fact]
    public async Task Show60s_ThenBlackout300s_ThenShow_WithinOneSecond()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.SpeechEnabled = false;
            c.ItemHoldSeconds = 10;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));

        await h.Engine.StartAsync();
        Assert.Equal(PlaybackPhase.Show, h.Engine.Phase);
        Assert.NotNull(h.Engine.Current);
        Assert.Equal(0, h.Engine.CurrentIndex);

        // 窗口内：55s 时仍在 SHOW（检查点刻意留在 60s 窗口里侧，真实调度抖动被放大 100 倍，
        // 贴着 59s 检查会在机器忙时误判成「已经黑屏」）
        await WaitUntilAsync(() => clock.Elapsed >= TimeSpan.FromSeconds(55), "到达 55s");
        Assert.Equal(PlaybackPhase.Show, h.Engine.Phase);
        Assert.False(h.Screen.Blackout, "55s 时不应进入黑屏");

        // 窗口到点：进 BLACKOUT，误差 < 1s
        var blackoutOnTask = h.WaitForPhaseAsync(PlaybackPhase.Blackout, "进入 BLACKOUT");
        await WaitUntilAsync(() => clock.Elapsed >= TimeSpan.FromSeconds(62), "走过 62s");
        await blackoutOnTask;
        var blackoutOn = h.Screen.CallsOf("Blackout").Single(c => c.Value);
        Assert.InRange(blackoutOn.At.TotalSeconds, 59.0, 61.2);

        // 360s：回 SHOW（同样把检查点留在窗口里侧的 362s）
        var blackoutOffTask = h.WaitForPhaseAsync(PlaybackPhase.Show, "回到 SHOW");
        await WaitUntilAsync(() => clock.Elapsed >= TimeSpan.FromSeconds(362), "走过 362s");
        await blackoutOffTask;
        Assert.False(h.Screen.Blackout);
        var blackoutOff = h.Screen.CallsOf("Blackout").Last(c => !c.Value);
        Assert.InRange(blackoutOff.At.TotalSeconds, 359.0, 361.5);
    }

    [Fact]
    public async Task Blackout_NoSpeechAndNoKeepScreenOn()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.SpeechEnabled = false;
            c.ItemHoldSeconds = 10;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));

        await h.Engine.StartAsync();
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "进入 BLACKOUT");

        // 黑屏中途（进入后 60s）：仍然黑屏、无朗读、常亮状态没被动过
        var blackoutAt = h.FirstPhaseAt(PlaybackPhase.Blackout) ?? TimeSpan.Zero;
        await WaitUntilAsync(() => clock.Elapsed >= blackoutAt + TimeSpan.FromSeconds(60), "黑屏走过 60s");

        Assert.Equal(PlaybackPhase.Blackout, h.Engine.Phase);
        Assert.Empty(h.Speech.Attempts);
        Assert.Single(h.Screen.CallsOf("KeepScreenOn"));
        Assert.True(h.Screen.KeepScreenOn);
        Assert.Equal(1, h.Screen.CallsOf("Blackout").Count(c => c.Value));
        Assert.True(h.Screen.Blackout);
    }

    [Fact]
    public async Task SpeechCompletion_IsTheItemClock()
    {
        var clock = new ScaledClock(Scale);
        var first = Item("a", "武汉今日开通新地铁线路");
        var second = Item("b", "中国发布新能源汽车新政策");
        await using var h = new EngineHarness(clock, Config(), first, second);
        h.Speech.AutoFinish = false;

        await h.Engine.StartAsync();

        // 静默 T_lead = 1s 后才开口
        await WaitUntilAsync(() => h.Speech.Attempts.Count == 1, "开口朗读第一条");
        var attempt = h.Speech.LastAttempt!;
        Assert.Equal(first.Summary, attempt.Text);
        Assert.InRange(attempt.StartedAt.TotalSeconds, 0.8, 4.0);

        // 朗读未结束前不切条
        await Task.Delay(30);
        Assert.Equal(0, h.Engine.CurrentIndex);

        // 朗读完成的时刻 = 切下一条的时刻
        h.Speech.Finish();
        await WaitForIndexAsync(h, 1, "切到第二条");
        Assert.Equal(second.Id, h.Engine.Current!.Id);
        // 容差 20s（引擎时间）＝ 200ms 真实时间：缩放时钟下真实调度抖动会被放大 100 倍，
        // 但 20s 仍远小于 60s 的展示窗口，足以证明「切条是跟着朗读结束走的」。
        Assert.True(clock.Elapsed - attempt.StartedAt < TimeSpan.FromSeconds(20), "切条应紧跟朗读结束");

        await WaitUntilAsync(() => h.Speech.Attempts.Count == 2, "朗读第二条");
        Assert.Equal(second.Summary, h.Speech.LastAttempt!.Text);
    }

    [Fact]
    public async Task TtsUnavailable_WithSkipItemPolicy_SkipsItem()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.TtsUnavailable = TtsUnavailablePolicy.SkipItem;
            c.ShowDurationSeconds = 600;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));
        h.Speech.Available = false;

        await h.Engine.StartAsync();
        Assert.False(h.Engine.IsSpeechAvailable);

        await WaitForIndexAsync(h, 1, "跳过不可朗读的条目");
        await Task.Delay(30);
        Assert.Empty(h.Speech.Attempts);
    }

    [Fact]
    public async Task TtsUnavailable_WithFallbackHold_UsesItemHoldSeconds()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.TtsUnavailable = TtsUnavailablePolicy.FallbackHold;
            c.ItemHoldSeconds = 12;
            c.ShowDurationSeconds = 600;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));
        h.Speech.Available = false;

        await h.Engine.StartAsync();

        // 固定停留 12s 后才切条
        await WaitForIndexAsync(h, 1, "固定停留 12s 后切条");
        var switchedAt = clock.Elapsed;
        Assert.InRange(switchedAt.TotalSeconds, 11.0, 16.0);
        Assert.Empty(h.Speech.Attempts);
    }

    [Fact]
    public async Task QuietHours_NoSpeechButStillAdvances()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.QuietHours = "23:00-07:00";
            c.ItemHoldSeconds = 10;
            c.ShowDurationSeconds = 600;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));

        // 场景时刻设为 23:30，处于跨夜静音时段内
        var quietClock = new OffsetClock(clock, new DateTimeOffset(2025, 1, 1, 23, 30, 0, TimeSpan.Zero));
        var quietEngine = new PlaybackEngine(h.Provider, h.Repository, h.Screen, h.Speech, quietClock);
        var displays = new Sa3DisplayTracker(quietEngine, quietClock);
        quietEngine.StateChanged += displays.Record;

        await quietEngine.StartAsync();
        try
        {
            // 静音时段内照常按 ItemHoldSeconds 切条，只是不朗读
            await WaitUntilAsync(() => displays.Count >= 2, "静音时段内继续切条");

            Assert.True(quietEngine.IsInQuietHours, "23:30 应在静音时段内");
            Assert.Empty(h.Speech.Attempts);
            var shown = displays.Snapshot();
            Assert.True(
                shown[^1].At - shown[0].At >= TimeSpan.FromSeconds(9),
                "两次换条之间应至少经过约一个 ItemHoldSeconds（10s）");
            Assert.Equal(23, quietClock.Now.Hour);
        }
        finally
        {
            await quietEngine.StopAsync();
            await quietEngine.DisposeAsync();
        }
    }

    [Fact]
    public void QuietHours_Parsing()
    {
        var midnight = new DateTimeOffset(2025, 1, 1, 0, 10, 0, TimeSpan.Zero);
        var noon = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var seven = new DateTimeOffset(2025, 1, 1, 7, 0, 0, TimeSpan.Zero);

        Assert.False(PlaybackEngine.IsQuietHours("", noon));
        Assert.False(PlaybackEngine.IsQuietHours(null, noon));
        Assert.False(PlaybackEngine.IsQuietHours("乱写的", noon));
        Assert.False(PlaybackEngine.IsQuietHours("23:00", noon));
        Assert.True(PlaybackEngine.IsQuietHours("23:00-07:00", midnight));
        Assert.False(PlaybackEngine.IsQuietHours("23:00-07:00", seven));
        Assert.False(PlaybackEngine.IsQuietHours("23:00-07:00", noon));
        Assert.True(PlaybackEngine.IsQuietHours("12:00-13:00", noon));
        Assert.True(PlaybackEngine.IsQuietHours("08:00-08:00", noon));
    }

    [Fact]
    public async Task EmptyQuietHours_SpeaksAllDay()
    {
        var clock = new ScaledClock(Scale);
        var first = Item("a", "第一条摘要");
        await using var h = new EngineHarness(clock, Config(), first);

        await h.Engine.StartAsync();
        await WaitUntilAsync(() => h.Speech.Attempts.Count == 1, "开口朗读");

        Assert.False(h.Engine.IsInQuietHours);
        Assert.Equal(first.Summary, h.Speech.LastAttempt?.Text);
    }

    [Fact]
    public async Task BlackoutCycleDisabled_NeverBlackouts()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.BlackoutCycleEnabled = false;
            c.ShowDurationSeconds = 10;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));

        await h.Engine.StartAsync();

        // 走完 5 个展示窗口（50s）
        await WaitUntilAsync(() => clock.Elapsed >= TimeSpan.FromSeconds(50), "走过 5 个窗口");

        Assert.Equal(PlaybackPhase.Show, h.Engine.Phase);
        Assert.DoesNotContain(h.Screen.CallsOf("Blackout"), c => c.Value);
        Assert.True(h.Screen.CallsOf("Blackout").Count >= 1, "启动时应显式退出黑屏");
        Assert.False(h.Screen.Blackout);
        Assert.True(h.Marks.Count(m => m.Phase == PlaybackPhase.Blackout) == 0);
    }

    [Fact]
    public async Task WindowEnd_FinishCurrentItem_WaitsForSpeech()
    {
        var clock = new ScaledClock(Scale);
        var first = Item("a", "一条比较长的摘要内容用于超过展示窗口");
        var config = Config(c =>
        {
            c.WindowEndBehavior = WindowEndBehavior.FinishCurrentItem;
            c.ShowDurationSeconds = 60;
        });
        await using var h = new EngineHarness(clock, config, first);

        await h.Engine.StartAsync();
        h.Speech.AutoFinish = false;
        await WaitUntilAsync(() => clock.Elapsed >= TimeSpan.FromSeconds(62), "到达 62s");

        // 窗口已到点但当前条未念完 → 仍停在 SHOW，不黑屏
        Assert.Equal(PlaybackPhase.Show, h.Engine.Phase);
        Assert.DoesNotContain(h.Screen.CallsOf("Blackout"), c => c.Value);
        Assert.Equal(1, h.Speech.InFlightCount);

        h.Speech.Finish();
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "念完后进 BLACKOUT");
    }

    [Fact]
    public async Task WindowEnd_InterruptImmediately_StopsSpeechAndBlackouts()
    {
        var clock = new ScaledClock(Scale);
        var first = Item("a", "一条比较长的摘要内容用于超过展示窗口");
        var config = Config(c =>
        {
            c.WindowEndBehavior = WindowEndBehavior.InterruptImmediately;
            c.ShowDurationSeconds = 60;
        });
        await using var h = new EngineHarness(clock, config, first);
        h.Speech.AutoFinish = false;

        await h.Engine.StartAsync();
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "立即中断进 BLACKOUT");

        Assert.True(h.Speech.StopCount >= 1);
        Assert.True(h.Speech.Attempts.Count >= 1);
        Assert.True(h.Speech.Attempts[^1].Cancelled, "立即中断模式应取消在途朗读");
    }

    [Fact]
    public async Task ResumeAcrossRounds_ContinuesInterruptedItem()
    {
        var clock = new ScaledClock(Scale);
        var first = Item("a", "第一条摘要");
        var second = Item("b", "第二条摘要");
        var config = Config(c =>
        {
            c.SpeechEnabled = false;
            c.ResumeAcrossRounds = true;
            c.ShowDurationSeconds = 1;
            c.ItemHoldSeconds = 10;
            c.BlackoutDurationSeconds = 20;
        });
        await using var h = new EngineHarness(clock, config, first, second);

        await h.Engine.StartAsync();
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "第一个窗口到点");
        Assert.Equal(0, h.Engine.CurrentIndex);

        // 黑屏结束进入下一轮：仍应从被中断的第一条继续
        await WaitForPhaseAsync(h, PlaybackPhase.Show, "回到 SHOW");
        Assert.Equal(0, h.Engine.CurrentIndex);

        // 续播：这条路会紧接着（窗口到点）再次进黑屏，而不是停在第一条不动
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "续播后再次到点");
    }

    [Fact]
    public async Task DiscardAcrossRounds_RestartsFromFirstItem()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.SpeechEnabled = false;
            c.ResumeAcrossRounds = false;
            c.ShowDurationSeconds = 1;
            c.ItemHoldSeconds = 10;
            c.BlackoutDurationSeconds = 20;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"), Item("b", "第二条摘要"));

        await h.Engine.StartAsync();
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "第一个窗口到点");

        await WaitForPhaseAsync(h, PlaybackPhase.Show, "回到 SHOW");
        await Task.Delay(20);
        Assert.Equal(0, h.Engine.CurrentIndex);
    }

    [Fact]
    public async Task StopAsync_CancelsSpeechAndReleasesScreenState()
    {
        var clock = new ScaledClock(Scale);
        await using var h = new EngineHarness(clock, Config(), Item("a", "第一条摘要"));

        await h.Engine.StartAsync();
        await WaitUntilAsync(() => h.Speech.Attempts.Count == 1, "开口朗读");

        await h.Engine.StopAsync();
        await Task.Delay(30);

        Assert.True(h.Speech.StopCount >= 1);
        Assert.False(h.Screen.Blackout);
        Assert.False(h.Screen.KeepScreenOn);

        var speechAfterStop = h.Speech.Attempts.Count;
        var blackoutAfterStop = h.Screen.CallsOf("Blackout").Count;
        var keepAfterStop = h.Screen.CallsOf("KeepScreenOn").Count;
        var phaseMarksAfterStop = h.Marks.Count;

        await Task.Delay(150);

        Assert.Equal(speechAfterStop, h.Speech.Attempts.Count);
        Assert.Equal(blackoutAfterStop, h.Screen.CallsOf("Blackout").Count);
        Assert.Equal(keepAfterStop, h.Screen.CallsOf("KeepScreenOn").Count);
        Assert.Equal(phaseMarksAfterStop, h.Marks.Count);
    }

    [Fact]
    public void EstimateSpeechSeconds_IsInverseToRate()
    {
        const string text = "武汉今日开通新地铁线";
        Assert.Equal(10, text.Length);
        var at1 = PlaybackEngine.EstimateSpeechSeconds(text, 1.0);
        var at2 = PlaybackEngine.EstimateSpeechSeconds(text, 2.0);
        var atHalf = PlaybackEngine.EstimateSpeechSeconds(text, 0.5);

        Assert.Equal(2.2, at1, 3);
        Assert.Equal(at1 / 2, at2, 6);
        Assert.Equal(at1 * 2, atHalf, 6);
        Assert.Equal(0, PlaybackEngine.EstimateSpeechSeconds("", 1.0));
        Assert.Equal(0, PlaybackEngine.EstimateSpeechSeconds(null, 1.0));
        Assert.True(PlaybackEngine.EstimateSpeechSeconds("hello world", 1.0) > 0);
        Assert.Equal(at1, PlaybackEngine.EstimateSpeechSeconds(text, 0), 6);
    }

    [Fact]
    public async Task NextAsync_SkipsToFollowingItem()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c => c.ShowDurationSeconds = 600);
        // 摘要刻意写长：缩放时钟是 100 倍，短摘要只要 10ms 真实时间就播完了，
        // 断言「还在第 0 条」时容易被下一个条目顶掉（这是偶发红的来源，不是引擎缺陷）。
        var longFirst = Item("a", "武汉今日开通新地铁线路，全线共设车站二十座，串联三个城区，早高峰通行时间缩短约二十分钟。");
        await using var h = new EngineHarness(clock, config, longFirst, Item("b", "第二条摘要"));

        await h.Engine.StartAsync();
        await WaitUntilAsync(() => h.Engine.CurrentIndex == 0 && h.Speech.Attempts.Count >= 1, "第一条开播并开念");
        Assert.Equal(0, h.Engine.CurrentIndex);

        await h.Engine.NextAsync();
        await WaitForIndexAsync(h, 1, "手动切到下一条");
    }

    [Fact]
    public async Task StartAsync_AppliesScreenContract()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.KeepScreenOn = true;
            c.AndroidShowStatusBar = true;
            c.WindowsBorderlessFullscreen = false;
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"));

        await h.Engine.StartAsync();

        var kinds = h.Screen.Calls.Select(c => c.Kind).ToList();
        Assert.Equal(["KeepScreenOn", "Fullscreen", "Blackout"], kinds);
        Assert.Equal("KeepScreenOn", kinds[0]);
        Assert.Equal("Fullscreen", kinds[1]);
        Assert.Equal("Blackout", kinds[2]);
        Assert.True(h.Screen.KeepScreenOn);
        Assert.False(h.Screen.Fullscreen);
        Assert.False(h.Screen.Blackout);
        Assert.Equal(1, h.Speech.ProbeCount);
        Assert.True(h.Engine.IsSpeechAvailable);
    }

    [Fact]
    public async Task EmptyPlaylist_StaysRunningWithoutThrowing()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c => c.BlackoutDurationSeconds = 30);
        await using var h = new EngineHarness(clock, config);

        await h.Engine.StartAsync();
        await WaitForPhaseAsync(h, PlaybackPhase.Blackout, "空列表仍走窗口");

        Assert.Null(h.Engine.Current);
        Assert.Equal(-1, h.Engine.CurrentIndex);
        Assert.Empty(h.Speech.Attempts);

        await WaitForPhaseAsync(h, PlaybackPhase.Show, "空列表仍回 SHOW");
    }

    [Fact]
    public async Task SpeakTestAsync_UsesConfiguredRatePitchLanguage()
    {
        var clock = new ScaledClock(Scale);
        var config = Config(c =>
        {
            c.SpeechRate = 1.5;
            c.SpeechPitch = 1.2;
            c.SpeechLanguage = "zh-CN";
        });
        await using var h = new EngineHarness(clock, config, Item("a", "第一条摘要"));

        await h.Engine.SpeakTestAsync("试听一句话");

        var attempt = Assert.Single(h.Speech.Attempts);
        Assert.Equal("试听一句话", attempt.Text);
        Assert.Equal(1.5, attempt.Rate);
        Assert.Equal(1.2, attempt.Pitch);
        Assert.Equal("zh-CN", attempt.Language);
    }
}

/// <summary>记录每次「换一条」（索引变化 + 虚拟时刻），用于验证切条节奏。</summary>
internal sealed class Sa3DisplayTracker
{
    private readonly PlaybackEngine _engine;
    private readonly OffsetClock _clock;
    private readonly List<PhaseMark> _shown = [];
    private int _lastIndex = int.MinValue;

    public Sa3DisplayTracker(PlaybackEngine engine, OffsetClock clock)
    {
        _engine = engine;
        _clock = clock;
    }

    public int Count { get { lock (_shown) { return _shown.Count; } } }

    public IReadOnlyList<PhaseMark> Snapshot() { lock (_shown) { return [.. _shown]; } }

    public void Record()
    {
        lock (_shown)
        {
            var index = _engine.CurrentIndex;
            if (index == _lastIndex)
            {
                return;
            }

            _lastIndex = index;
            _shown.Add(new PhaseMark { Phase = _engine.Phase, At = _clock.Elapsed });
        }
    }
}

/// <summary>在缩放时钟之上叠加一个固定起点的时钟，用于验证静音时段（跨午夜）。</summary>
internal sealed class OffsetClock : IClock
{
    private readonly IClock _inner;
    private readonly DateTimeOffset _origin;

    public OffsetClock(IClock inner, DateTimeOffset origin)
    {
        _inner = inner;
        _origin = origin;
    }

    public DateTimeOffset Now => _origin + (_inner.Now - DateTimeOffset.UnixEpoch);

    public TimeSpan Elapsed => _inner.Now - DateTimeOffset.UnixEpoch;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        => _inner.DelayAsync(delay, cancellationToken);
}
