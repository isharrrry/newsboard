using NewsBoard.Shared.Models;

namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>条目缓存 + URL 去重 + AI 调用成本控制。</summary>
public interface INewsRepository
{
    /// <summary>按配置的轮播顺序取出可播放条目。</summary>
    IReadOnlyList<NewsItem> GetPlaylist(AppConfig config);

    /// <summary>是否已存在该 URL 的条目（去重：已提炼过的永不再调 AI）。</summary>
    bool Contains(string url);

    /// <summary>AI 是否仍在最小调用间隔内（true = 应当跳过本次 AI 调用）。</summary>
    bool IsWithinAiCooldown(AppConfig config, DateTimeOffset now);

    DateTimeOffset? LastAiCallAt { get; }

    /// <summary>写入/更新条目（按 Id 或 Url 去重合并）。</summary>
    void AddOrUpdateRange(IEnumerable<NewsItem> items);

    /// <summary>记录一次 AI 调用发生的时间。</summary>
    void MarkAiCalled(DateTimeOffset at);

    /// <summary>丢弃超过保留期的条目。</summary>
    void PruneExpired(DateTimeOffset now, TimeSpan retention);

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    event Action? Changed;
}
