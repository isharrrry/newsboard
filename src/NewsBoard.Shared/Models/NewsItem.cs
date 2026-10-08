namespace NewsBoard.Shared.Models;

/// <summary>采集层拿到的原始条目（未提炼）。</summary>
public sealed class RawNewsItem
{
    public required string Keyword { get; init; }
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Description { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>通道 A（联网搜索）直接产出的摘要；通道 B 为 null。</summary>
    public string? PreSummarized { get; init; }
}

/// <summary>可播放的新闻条目。</summary>
public sealed class NewsItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Url { get; set; } = "";
    public string Keyword { get; set; } = "";
    public string? Source { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string? ImageUrl { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>朗读文本：仅摘要，或标题 + 摘要。</summary>
    public string GetSpeechText(bool includeTitle)
        => includeTitle && !string.IsNullOrWhiteSpace(Title) ? $"{Title}。{Summary}" : Summary;
}
