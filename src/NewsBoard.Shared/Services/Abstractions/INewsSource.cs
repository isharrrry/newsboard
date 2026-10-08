using NewsBoard.Shared.Models;

namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>新闻来源通道（RSS 原文 / 模型联网搜索）。</summary>
public interface INewsSource
{
    string Name { get; }

    Task<IReadOnlyList<RawNewsItem>> FetchAsync(string keyword, AppConfig config, CancellationToken cancellationToken = default);
}

/// <summary>可注入时钟，便于状态机测试。</summary>
public interface IClock
{
    DateTimeOffset Now { get; }

    /// <summary>等待指定时长，可被取消。</summary>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

/// <summary>系统时钟实现。</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        => Task.Delay(delay, cancellationToken);
}
