using System.Globalization;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>播放阶段：亮屏展示 / 黑屏休息。</summary>
public enum PlaybackPhase { Show, Blackout }

/// <summary>
/// 播放状态机（§5）：SHOW 窗口内按 playlist 顺序逐条播放，窗口到点按配置进 BLACKOUT，黑屏结束回到 SHOW。
/// 只依赖 <c>Abstractions</c> 中的接口；所有时间都取自注入的 <see cref="IClock"/>，
/// 因此用可注入时钟即可精确驱动（见 <see cref="AdvanceVirtualTime"/>）。
/// </summary>
public sealed class PlaybackEngine : IAsyncDisposable
{
    /// <summary>中文朗读每字秒数（语速 1.0 标定）。</summary>
    public const double SecondsPerChineseChar = 0.22;

    /// <summary>非中日韩文字按每词秒数估算。</summary>
    public const double SecondsPerWord = 0.32;

    private readonly IConfigProvider _config;
    private readonly INewsRepository _repository;
    private readonly IScreenController _screen;
    private readonly ISpeechService _speech;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _runCts = new();

    private volatile PlaybackPhase _phase = PlaybackPhase.Show;
    private NewsItem? _current;
    private int _currentIndex = -1;
    private DateTimeOffset _currentStartedAt;
    private bool _speechProbed;
    private bool _speechAvailable;
    private bool _inQuietHours;
    private bool _running;
    private Task? _loop;

    // ---- 等待原语：看门狗（走 IClock）+ 手动切换信号 ----
    private TaskCompletionSource _advanceTcs = NewSignal();
    private volatile bool _advanceRequested;

    // ---- 手动控制（上一条/下一条/打断）----
    private volatile int _direction = 1;
    private volatile bool _interruptRequested;
    private volatile bool _forcedBlackout;
    private CancellationTokenSource? _speakCts;

    // ---- 一轮（SHOW 窗口）内的时间账 ----
    private DateTimeOffset _windowStartAt;
    private TimeSpan _windowLength;
    private TimeSpan _carriedSeconds;
    private bool _windowEnded;
    private IReadOnlyList<NewsItem> _playlist = [];

    public PlaybackEngine(
        IConfigProvider config,
        INewsRepository repository,
        IScreenController screen,
        ISpeechService speech,
        IClock clock)
    {
        _config = config;
        _repository = repository;
        _screen = screen;
        _speech = speech;
        _clock = clock;
        _windowStartAt = clock.Now;
        _currentStartedAt = clock.Now;
    }

    public PlaybackPhase Phase => _phase;

    public NewsItem? Current { get { lock (_gate) { return _current; } } }

    public int CurrentIndex { get { lock (_gate) { return _currentIndex; } } }

    /// <summary>本轮的播放顺序。</summary>
    public IReadOnlyList<NewsItem> Playlist { get { lock (_gate) { return _playlist; } } }

    /// <summary>系统语音包探测结果（StartAsync 时探测一次）。</summary>
    public bool IsSpeechAvailable => _speechAvailable;

    /// <summary>当前时刻是否处于静音时段（该期间只显示不朗读）。</summary>
    public bool IsInQuietHours => _inQuietHours;

    /// <summary>当前条开始显示的时钟时刻（供 UI 计算滚动剩余时长）。</summary>
    public DateTimeOffset CurrentStartedAt { get { lock (_gate) { return _currentStartedAt; } } }

    /// <summary>阶段、当前条目或索引变化时触发。订阅方自行切回 UI 线程。</summary>
    public event Action? StateChanged;

    /// <summary>启动：常亮 → 全屏 → 退出黑屏 → 探测语音 → 进入 SHOW 循环。</summary>
    public async Task StartAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        var config = _config.Current;

        await _screen.SetKeepScreenOnAsync(config.KeepScreenOn).ConfigureAwait(false);
        await _screen.SetFullscreenAsync(!config.AndroidShowStatusBar || config.WindowsBorderlessFullscreen).ConfigureAwait(false);
        await _screen.SetBlackoutAsync(false).ConfigureAwait(false);

        await ProbeSpeechAsync().ConfigureAwait(false);

        _inQuietHours = IsQuietHours(config.QuietHours, _clock.Now);
        ReloadPlaylist();

        _phase = PlaybackPhase.Show;
        _windowStartAt = _clock.Now;
        _windowLength = TimeSpan.FromSeconds(Math.Max(0, config.ShowDurationSeconds));
        _carriedSeconds = TimeSpan.Zero;
        _windowEnded = false;
        SelectIndex(_playlist.Count > 0 ? 0 : -1);
        Notify();

        _loop = Task.Run(RunLoopAsync);
    }

    /// <summary>停止播放：取消等待、停朗读、退出黑屏、释放常亮。</summary>
    public async Task StopAsync()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _runCts.Cancel();
        RequestAdvance();

        var loop = _loop;
        _loop = null;
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        try { await _speech.StopAsync().ConfigureAwait(false); } catch { }
        try { await _screen.SetBlackoutAsync(false).ConfigureAwait(false); } catch { }
        try { await _screen.SetKeepScreenOnAsync(false).ConfigureAwait(false); } catch { }

        _phase = PlaybackPhase.Show;
        lock (_gate) { _inQuietHours = false; }
        Notify();
    }

    /// <summary>手动切到下一条，并从该条重新起算本轮窗口。</summary>
    public Task NextAsync() => MoveAsync(1);

    /// <summary>手动切到上一条，并从该条重新起算本轮窗口。</summary>
    public Task PreviousAsync() => MoveAsync(-1);

    /// <summary>
    /// 打断本轮：立刻停朗读、结束当前展示窗口，强制灭屏等待「黑屏时长」秒后开下一轮。
    /// 即使配置关闭了亮熄循环，手动打断也会灭一次屏。下一轮是否续播当前条由「跨轮续播」决定。
    /// </summary>
    public Task InterruptAsync()
    {
        _interruptRequested = true;
        RequestAdvance();
        return Task.CompletedTask;
    }

    private Task MoveAsync(int direction)
    {
        _direction = direction;
        RequestAdvance();
        return Task.CompletedTask;
    }

    /// <summary>设置页「试听」：不进播放窗口，直接念一段。</summary>
    public Task SpeakTestAsync(string text, CancellationToken ct = default)
    {
        var config = _config.Current;
        return _speech.SpeakAsync(text, config.SpeechRate, config.SpeechPitch, config.SpeechLanguage, ct);
    }

    /// <summary>朗读时长估算（§5.2）：中文按字、其它按词，与语速成反比。</summary>
    public static double EstimateSpeechSeconds(string? text, double rate)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var effective = rate <= 0 ? 1.0 : rate;
        var cjk = 0;
        var words = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                cjk++;
                inWord = false;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return (cjk * SecondsPerChineseChar + words * SecondsPerWord) / effective;
    }

    /// <summary>静音时段形如 <c>23:00-07:00</c>，支持跨夜；空 = 全天可播。</summary>
    public static bool IsQuietHours(string? quietHours, DateTimeOffset now)
    {
        if (!TryParseQuietHours(quietHours, out var start, out var end))
        {
            return false;
        }

        var minutes = now.Hour * 60 + now.Minute;
        if (start == end)
        {
            return true;
        }

        return start < end
            ? minutes >= start && minutes < end
            : minutes >= start || minutes < end;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); } catch { }
        _runCts.Dispose();
    }

    // ---------------- SHOW 循环 ----------------

    private async Task RunLoopAsync()
    {
        var token = _runCts.Token;
        while (_running && !token.IsCancellationRequested)
        {
            var config = _config.Current;
            _inQuietHours = IsQuietHours(config.QuietHours, _clock.Now);
            ReloadPlaylist();

            if (_playlist.Count == 0)
            {
                lock (_gate)
                {
                    _current = null;
                    _currentIndex = -1;
                }
                Notify();

                // 列表空时不能干等满一个窗口：抓到新闻要立刻开播（否则首条要等 60s）
                var gotData = await WaitForDataOrWindowEndAsync(token).ConfigureAwait(false);
                if (token.IsCancellationRequested || !_running)
                {
                    return;
                }

                if (gotData)
                {
                    ResetWindow(config);
                    continue;
                }
            }
            else
            {
                if (_currentIndex < 0 || _currentIndex >= _playlist.Count)
                {
                    SelectIndex(0);
                }

                await PlayWindowAsync(token).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested || !_running)
            {
                return;
            }

            var forcedBlackout = _forcedBlackout;
            _forcedBlackout = false;
            if (!config.BlackoutCycleEnabled && !forcedBlackout)
            {
                ResetWindow(config);
                continue;
            }

            await EnterBlackoutAsync(config, token).ConfigureAwait(false);

            if (token.IsCancellationRequested || !_running)
            {
                return;
            }

            ResetWindow(config);
        }
    }

    private void ReloadPlaylist()
    {
        var playlist = _repository.GetPlaylist(_config.Current);
        lock (_gate) { _playlist = playlist; }
    }

    private void SelectIndex(int index)
    {
        lock (_gate)
        {
            _currentIndex = _playlist.Count == 0 ? -1 : index;
            _current = _playlist.Count == 0 ? null : _playlist[_currentIndex];
            _currentStartedAt = _clock.Now;
        }
    }

    /// <summary>一轮播完后的收尾：续播则保留中断条与其已播时长，丢弃则从头重来。</summary>
    private void ResetWindow(AppConfig config)
    {
        _windowStartAt = _clock.Now;
        _windowLength = TimeSpan.FromSeconds(Math.Max(0, config.ShowDurationSeconds));
        _windowEnded = false;

        if (config.ResumeAcrossRounds && _carriedSeconds > TimeSpan.Zero && _currentIndex >= 0 && _currentIndex < _playlist.Count)
        {
            // 保留 _carriedSeconds 与 _currentIndex：下轮从该条剩余处继续
            lock (_gate)
            {
                _current = _playlist[_currentIndex];
                _currentStartedAt = _clock.Now;
            }
        }
        else
        {
            _carriedSeconds = TimeSpan.Zero;
            SelectIndex(_playlist.Count > 0 ? 0 : -1);
        }

        Notify();
    }

    private TimeSpan RemainingWindow()
    {
        var left = _windowLength - (_clock.Now - _windowStartAt);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private async Task PlayWindowAsync(CancellationToken token)
    {
        if (ConsumeInterrupt())
        {
            return;
        }

        while (_running && !token.IsCancellationRequested && RemainingWindow() > TimeSpan.Zero)
        {
            var config = _config.Current;
            if (_currentIndex < 0 || _currentIndex >= _playlist.Count)
            {
                SelectIndex(0);
            }

            var item = _playlist[_currentIndex];
            var remaining = RemainingWindow() - _carriedSeconds;
            if (remaining <= TimeSpan.Zero)
            {
                _windowEnded = true;
                break;
            }

            var spent = await PlayItemAsync(item, remaining, config, token).ConfigureAwait(false);

            if (ConsumeInterrupt())
            {
                return;
            }

            if (_advanceRequested)
            {
                _advanceRequested = false;
                _carriedSeconds = TimeSpan.Zero;
                _windowStartAt = _clock.Now;
                _windowEnded = false;
                SelectIndex(StepIndex(_direction));
                Notify();
                continue;
            }

            if (_windowEnded)
            {
                _carriedSeconds += spent;
                break;
            }

            _carriedSeconds = TimeSpan.Zero;
            SelectIndex(StepIndex(1));
            Notify();
        }
    }

    /// <summary>消费一次「打断本轮」请求：本轮立即结束，且必须灭一次屏。</summary>
    private bool ConsumeInterrupt()
    {
        if (!_interruptRequested)
        {
            return false;
        }

        _interruptRequested = false;
        _advanceRequested = false;
        _windowEnded = true;
        _forcedBlackout = true;
        return true;
    }

    /// <summary>按方向取相邻条目下标（循环）。</summary>
    private int StepIndex(int direction)
    {
        var count = _playlist.Count;
        if (count == 0)
        {
            return -1;
        }

        return ((_currentIndex + direction) % count + count) % count;
    }


    /// <summary>播放单条，返回该条实际占用时长；窗口到点或手动切换会提前返回。</summary>
    private async Task<TimeSpan> PlayItemAsync(NewsItem item, TimeSpan limit, AppConfig config, CancellationToken token)
    {
        var before = _clock.Now;
        _windowEnded = false;

        lock (_gate)
        {
            _current = item;
            _currentStartedAt = _clock.Now;
        }
        Notify();

        var usable = SpeechUsable(config);
        if (!usable && config.TtsUnavailable == TtsUnavailablePolicy.SkipItem)
        {
            // 语音不可用且策略为跳过：本条不占时间，直接下一条
            return _clock.Now - before;
        }

        if (usable)
        {
            // §5.2：切到该条 → 静默 T_lead → 朗读（朗读完成即该条结束）
            await DelayUntilAsync(_clock.Now + TimeSpan.FromSeconds(Math.Max(0, config.SpeechLeadSeconds)), limit, token).ConfigureAwait(false);
            if (Stopped(token) || _advanceRequested || _windowEnded)
            {
                return _clock.Now - before;
            }

            await SpeakAsync(item, config, limit, token).ConfigureAwait(false);
        }
        else
        {
            // TTS 关闭 / 不可用回退 / 静音时段：固定停留
            await DelayUntilAsync(_clock.Now + TimeSpan.FromSeconds(Math.Max(0, config.ItemHoldSeconds)), limit, token).ConfigureAwait(false);
        }

        if (Stopped(token))
        {
            return _clock.Now - before;
        }

        if (RemainingWindow() <= TimeSpan.Zero)
        {
            _windowEnded = true;
        }

        return _clock.Now - before;
    }

    private async Task SpeakAsync(NewsItem item, AppConfig config, TimeSpan limit, CancellationToken token)
    {
        var text = item.GetSpeechText(config.SpeechContent == SpeechContent.TitleAndSummary);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _speakCts = itemCts;
        try
        {
            var speakTask = _speech.SpeakAsync(text, config.SpeechRate, config.SpeechPitch, config.SpeechLanguage, itemCts.Token);
            var deadlineTask = DeliverAfterAsync(limit, token);
            var advanceTask = _advanceTcs.Task;

            var completed = await Task.WhenAny(speakTask, deadlineTask, advanceTask).ConfigureAwait(false);

            if (completed == advanceTask)
            {
                // 手动切条 / 打断本轮：立刻停朗读
                itemCts.Cancel();
                await StopSpeechQuietlyAsync().ConfigureAwait(false);
            }
            else if (completed == deadlineTask)
            {
                if (config.WindowEndBehavior == WindowEndBehavior.InterruptImmediately)
                {
                    itemCts.Cancel();
                    await _speech.StopAsync().ConfigureAwait(false);
                }
                else
                {
                    // 念完当前条：窗口已到点，继续等朗读自然结束
                    await speakTask.ConfigureAwait(false);
                }
            }

            // 让朗读任务落定，避免未观察异常
            await speakTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_speakCts, itemCts))
            {
                _speakCts = null;
            }
        }
    }

    /// <summary>窗口到点：黑屏 + 停朗读 + 等黑屏时长；黑屏期间不动常亮状态。</summary>
    private async Task EnterBlackoutAsync(AppConfig config, CancellationToken token)
    {
        // 黑屏期间再按「打断」不当作一次新打断（否则会把黑屏等待也打断掉）
        _interruptRequested = false;
        _phase = PlaybackPhase.Blackout;
        _inQuietHours = IsQuietHours(config.QuietHours, _clock.Now);
        Notify();

        await _speech.StopAsync().ConfigureAwait(false);
        await _screen.SetBlackoutAsync(true).ConfigureAwait(false);

        await DelayUntilAsync(_clock.Now + TimeSpan.FromSeconds(Math.Max(0, config.BlackoutDurationSeconds)), limit: null, token).ConfigureAwait(false);
        if (Stopped(token))
        {
            return;
        }

        await _screen.SetBlackoutAsync(false).ConfigureAwait(false);
        _phase = PlaybackPhase.Show;
        _inQuietHours = IsQuietHours(config.QuietHours, _clock.Now);
        Notify();
    }

    private async Task WaitForWindowEndAsync(CancellationToken token)
    {
        var remaining = RemainingWindow();
        await DeliverAfterAsync(remaining, token).ConfigureAwait(false);
    }

    /// <summary>
    /// 播放列表为空时的等待：每 500ms 复查仓储，出现条目立刻返回 <c>true</c>（不等窗口到点）；
    /// 窗口到点或被打断则返回 <c>false</c>，交回主循环走正常黑屏流程。
    /// </summary>
    private async Task<bool> WaitForDataOrWindowEndAsync(CancellationToken token)
    {
        while (_running && !token.IsCancellationRequested)
        {
            ReloadPlaylist();
            if (_playlist.Count > 0)
            {
                return true;
            }

            if (ConsumeInterrupt())
            {
                return false;
            }

            if (_advanceRequested)
            {
                _advanceRequested = false;
                return false;
            }

            if (RemainingWindow() <= TimeSpan.Zero)
            {
                _windowEnded = true;
                return false;
            }

            var slice = _clock.Now + TimeSpan.FromMilliseconds(500);
            await WaitForSignalAsync(slice, null, token).ConfigureAwait(false);
        }

        return false;
    }

    // ---------------- 语音可用性 / 静音时段 ----------------

    private async Task ProbeSpeechAsync()
    {
        _speechProbed = true;
        try
        {
            _speechAvailable = await _speech.IsAvailableAsync(_config.Current.SpeechLanguage).ConfigureAwait(false);
        }
        catch
        {
            _speechAvailable = false;
        }
    }

    private bool SpeechUsable(AppConfig config)
        => config.SpeechEnabled
           && _speechProbed
           && _speechAvailable
           && !IsQuietHours(config.QuietHours, _clock.Now);

    private static bool TryParseQuietHours(string? raw, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2
               && TryParseMinute(parts[0], out start)
               && TryParseMinute(parts[1], out end);
    }

    private static bool TryParseMinute(string text, out int minutes)
    {
        minutes = 0;
        var bits = text.Split(':', StringSplitOptions.TrimEntries);
        if (bits.Length != 2
            || !int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            || h is < 0 or > 23
            || m is < 0 or > 59)
        {
            return false;
        }

        minutes = h * 60 + m;
        return true;
    }

    private static bool IsCjk(char ch) => ch is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';

    // ---------------- 等待原语 ----------------

    /// <summary>
    /// 等到 <paramref name="deadline"/> 或被手动切换/取消；
    /// <paramref name="limit"/> 非空时表示「本轮窗口剩余」，到点即置 <see cref="_windowEnded"/>。
    /// </summary>
    private async Task DelayUntilAsync(DateTimeOffset deadline, TimeSpan? limit, CancellationToken token)
    {
        var before = _clock.Now;
        await WaitForSignalAsync(deadline, limit, token).ConfigureAwait(false);

        if (limit is not null
            && !Stopped(token)
            && !_advanceRequested
            && RemainingWindow() <= TimeSpan.Zero
            && _clock.Now - before >= limit.Value)
        {
            _windowEnded = true;
        }
    }

    /// <summary>等到 deadline 或被手动切换/取消；<paramref name="windowRemaining"/> 为窗口剩余，用于提前结束本轮。</summary>
    private async Task WaitForSignalAsync(DateTimeOffset deadline, TimeSpan? windowRemaining, CancellationToken token)
    {
        while (!Stopped(token) && !_advanceRequested)
        {
            var now = _clock.Now;
            var untilDeadline = deadline - now;
            if (untilDeadline <= TimeSpan.Zero)
            {
                return;
            }

            var wait = untilDeadline;
            if (windowRemaining is not null)
            {
                var left = RemainingWindow();
                if (left <= TimeSpan.Zero)
                {
                    return;
                }

                if (left < wait)
                {
                    wait = left;
                }
            }

            var advanceTask = _advanceTcs.Task;
            var watchdog = WatchdogAsync(wait, token);
            var completed = await Task.WhenAny(watchdog, advanceTask).ConfigureAwait(false);
            if (completed == advanceTask)
            {
                return;
            }

            await watchdog.ConfigureAwait(false);
        }
    }

    /// <summary>看门狗：用注入时钟等待一段真实时间后返回；被取消时抛 TaskCanceledException。</summary>
    private async Task WatchdogAsync(TimeSpan after, CancellationToken token)
    {
        try
        {
            await _clock.DelayAsync(after, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private Task DeliverAfterAsync(TimeSpan after, CancellationToken token)
        => after <= TimeSpan.Zero ? Task.CompletedTask : WatchdogAsync(after, token);

    private void RequestAdvance()
    {
        _advanceRequested = true;
        CancelSpeech();
        var tcs = _advanceTcs;
        _advanceTcs = NewSignal();
        tcs.TrySetResult();
    }

    /// <summary>打断在途朗读：手动切条/打断本轮必须立刻生效，不能等念完。</summary>
    private void CancelSpeech()
    {
        var cts = _speakCts;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _ = StopSpeechQuietlyAsync();
    }

    private async Task StopSpeechQuietlyAsync()
    {
        try
        {
            await _speech.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private bool Stopped(CancellationToken token) => token.IsCancellationRequested || !_running;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Notify() => StateChanged?.Invoke();
}
