using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>
/// 定时抓取调度：启动即抓一次，之后按「抓取间隔」循环（plan §3.2 的计时器）。
/// 抓取失败不向外抛（联网抖动不该让看板停摆），记入 <see cref="LastWarning"/> 并通过
/// <see cref="Changed"/> 通知 UI。
/// </summary>
public sealed class NewsFetchScheduler : IAsyncDisposable
{
    private readonly NewsFetchCoordinator _coordinator;
    private readonly IConfigProvider _config;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public NewsFetchScheduler(NewsFetchCoordinator coordinator, IConfigProvider config, IClock clock)
    {
        _coordinator = coordinator;
        _config = config;
        _clock = clock;
    }

    /// <summary>最近一次抓取结果；从未成功过时为 null。</summary>
    public FetchResult? LastResult { get; private set; }

    /// <summary>最近一次抓取的警告或异常文本（成功且无警告时为 null）。</summary>
    public string? LastWarning { get; private set; }

    /// <summary>后台是否在跑。</summary>
    public bool IsRunning => _loop is { IsCompleted: false };

    /// <summary>每完成一次抓取（无论成败）触发一次。</summary>
    public event Action? Changed;

    /// <summary>启动调度；重复调用无副作用。</summary>
    public Task StartAsync()
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => LoopAsync(token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>停止调度，等待在途抓取收尾。</summary>
    public async Task StopAsync()
    {
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts.Dispose();
    }

    /// <summary>立刻抓一次（不改变定时节奏；设置页手动刷新/测试用）。</summary>
    public Task FetchNowAsync(CancellationToken cancellationToken = default)
        => FetchOnceAsync(cancellationToken);

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await FetchOnceAsync(token).ConfigureAwait(false);

            var minutes = Math.Max(1, _config.Current.FetchIntervalMinutes);
            try
            {
                await _clock.DelayAsync(TimeSpan.FromMinutes(minutes), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task FetchOnceAsync(CancellationToken token)
    {
        // 定时与手动触发可能撞车：同一时刻只跑一次抓取
        if (!await _fetchGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var result = await _coordinator.FetchAsync(token).ConfigureAwait(false);
            LastResult = result;
            LastWarning = result.Warning;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastWarning = ex.Message;
        }
        finally
        {
            _fetchGate.Release();
            Changed?.Invoke();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _fetchGate.Dispose();
    }
}
