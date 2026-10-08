using System.Text.Json;
using System.Text.Json.Serialization;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>news.json 缓存：URL 去重 + 轮播排序 + AI 调用成本控制。</summary>
public sealed class NewsRepository : INewsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, NewsItem> _byId = new(StringComparer.Ordinal);
    private readonly HashSet<string> _urls = new(StringComparer.Ordinal);

    private DateTimeOffset? _lastAiCallAt;
    private int _version;
    private List<NewsItem>? _randomPlaylist;
    private int _randomPlaylistVersion = -1;
    private double _randomPlaylistThreshold = double.NaN;

    public NewsRepository(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewsBoard");
        FilePath = Path.Combine(Directory, "news.json");
    }

    public string Directory { get; }

    public string FilePath { get; }

    public DateTimeOffset? LastAiCallAt
    {
        get { lock (_gate) return _lastAiCallAt; }
    }

    public event Action? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        RepoFile? file = null;
        try
        {
            if (File.Exists(FilePath))
            {
                await using var stream = File.OpenRead(FilePath);
                file = await JsonSerializer
                    .DeserializeAsync<RepoFile>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 缓存损坏或不可读时视为空缓存，不影响启动
            file = null;
        }

        lock (_gate)
        {
            _byId.Clear();
            _urls.Clear();
            _lastAiCallAt = file?.LastAiCallAt;

            foreach (var item in file?.Items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Url)) continue;
                item.Url = item.Url.Trim();
                if (string.IsNullOrWhiteSpace(item.Id)) item.Id = NewsSummarizer.MakeId(item.Url);
                _byId[item.Id] = item;
                _urls.Add(item.Url);
            }

            _version++;
        }

        Changed?.Invoke();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        RepoFile snapshot;
        lock (_gate)
        {
            snapshot = new RepoFile
            {
                LastAiCallAt = _lastAiCallAt,
                Items = [.. _byId.Values],
            };
        }

        System.IO.Directory.CreateDirectory(Directory);
        var tempPath = FilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, FilePath, overwrite: true);
    }

    public bool Contains(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        lock (_gate) return _urls.Contains(url.Trim());
    }

    public bool IsWithinAiCooldown(AppConfig config, DateTimeOffset now)
    {
        if (config.AiMinIntervalMinutes <= 0) return false;

        lock (_gate)
        {
            return _lastAiCallAt is { } last && now - last < TimeSpan.FromMinutes(config.AiMinIntervalMinutes);
        }
    }

    public void MarkAiCalled(DateTimeOffset at)
    {
        lock (_gate) _lastAiCallAt = at;
    }

    public void AddOrUpdateRange(IEnumerable<NewsItem> items)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var item in items)
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Url)) continue;

                item.Url = item.Url.Trim();
                if (string.IsNullOrWhiteSpace(item.Id)) item.Id = NewsSummarizer.MakeId(item.Url);

                if (_byId.TryGetValue(item.Id, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(item.Title)) existing.Title = item.Title;
                    if (!string.IsNullOrWhiteSpace(item.Summary)) existing.Summary = item.Summary;
                    if (!string.IsNullOrWhiteSpace(item.Keyword)) existing.Keyword = item.Keyword;
                    if (!string.IsNullOrWhiteSpace(item.Source)) existing.Source = item.Source;
                    if (!string.IsNullOrWhiteSpace(item.ImageUrl)) existing.ImageUrl = item.ImageUrl;
                    if (item.PublishedAt != default) existing.PublishedAt = item.PublishedAt;
                    if (item.Importance is not null) existing.Importance = item.Importance;
                    if (!string.IsNullOrWhiteSpace(item.RawContent)) existing.RawContent = item.RawContent;
                }
                else
                {
                    _byId[item.Id] = item;
                }

                _urls.Add(item.Url);
                changed = true;
            }

            if (changed) _version++;
        }

        if (changed) Changed?.Invoke();
    }

    public void PruneExpired(DateTimeOffset now, TimeSpan retention)
    {
        var cutoff = now - retention;
        List<string>? expired = null;

        lock (_gate)
        {
            foreach (var (id, item) in _byId)
            {
                if (item.PublishedAt < cutoff) (expired ??= []).Add(id);
            }

            foreach (var id in expired ?? [])
            {
                _urls.Remove(_byId[id].Url);
                _byId.Remove(id);
            }

            if (expired is not null) _version++;
        }

        if (expired is not null) Changed?.Invoke();
    }

    public IReadOnlyList<NewsItem> GetPlaylist(AppConfig config)
    {
        lock (_gate)
        {
            // 先按重要度门槛过滤，再排序：Importance 为 null（未评分，例如旧缓存或 AI 全失败）
            // 一律保留，避免升级后被当成低分把整库清空。
            var playlist = _byId.Values
                .Where(i => i.Importance is not { } importance || importance >= config.ImportanceThreshold)
                .ToList();

            // 门槛把缓存里的条目全筛掉时，随机补一条「勉强可播」的，免得画面一直停在「正在获取新闻…」。
            // 补上来的那条摘要末尾带 ~ 后缀，一眼能看出它不是达标条目；给的是副本，不污染缓存里的原文。
            if (playlist.Count == 0 && _byId.Count > 0)
            {
                playlist.Add(WithBelowThresholdMarker(_byId.Values.ElementAt(Random.Shared.Next(_byId.Count))));
            }

            switch (config.PlayOrder)
            {
                case PlayOrder.ByPublishTime:
                    return playlist
                        .OrderByDescending(i => i.PublishedAt)
                        .ThenBy(i => i.Id, StringComparer.Ordinal)
                        .ToList();

                case PlayOrder.Random:
                    // 门槛也算进缓存键：改门槛（设置页）要立刻生效，不能返回上一轮的缓存
                    if (_randomPlaylist is null
                        || _randomPlaylistVersion != _version
                        || _randomPlaylistThreshold != config.ImportanceThreshold)
                    {
                        _randomPlaylist = Shuffle(playlist);
                        _randomPlaylistVersion = _version;
                        _randomPlaylistThreshold = config.ImportanceThreshold;
                    }

                    return [.. _randomPlaylist];

                default:
                    var rank = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (var i = 0; i < config.Keywords.Count; i++) rank[config.Keywords[i].Trim()] = i;

                    return playlist
                        .OrderBy(i => rank.TryGetValue(i.Keyword, out var index) ? index : int.MaxValue)
                        .ThenBy(i => i.Keyword, StringComparer.Ordinal)
                        .ThenByDescending(i => i.PublishedAt)
                        .ToList();
            }
        }
    }

    /// <summary>不达门槛被勉强补进播放列表的条目，摘要末尾会带这个后缀。</summary>
    public const string BelowThresholdSuffix = " ~";

    /// <summary>复制一份并把摘要标记为「低于门槛」（<c>NewsRepository</c> 内部维护用）。</summary>
    private static NewsItem WithBelowThresholdMarker(NewsItem source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Summary = source.Summary.TrimEnd() + BelowThresholdSuffix,
        Url = source.Url,
        Keyword = source.Keyword,
        Source = source.Source,
        PublishedAt = source.PublishedAt,
        ImageUrl = source.ImageUrl,
        FetchedAt = source.FetchedAt,
        RawContent = source.RawContent,
        Importance = source.Importance,
    };

    /// <summary>缓存里全部条目的快照（按发布时间从新到旧），供配图去重等维护性操作使用。</summary>
    public IReadOnlyList<NewsItem> Snapshot()
    {
        lock (_gate)
        {
            return [.. _byId.Values.OrderByDescending(i => i.PublishedAt)];
        }
    }

    private static List<NewsItem> Shuffle(List<NewsItem> source)
    {
        for (var i = source.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (source[i], source[j]) = (source[j], source[i]);
        }

        return source;
    }

    private sealed class RepoFile
    {
        public DateTimeOffset? LastAiCallAt { get; set; }

        public List<NewsItem> Items { get; set; } = [];
    }
}
