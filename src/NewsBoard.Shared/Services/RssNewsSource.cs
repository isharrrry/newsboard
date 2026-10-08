using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>
/// 共享频道源：模板不含 <c>{q}</c> 时一轮只抓一次，不按关键字重复请求。
/// 协调器通过本接口识别这类来源。
/// </summary>
public interface ISharedChannelNewsSource
{
    Task<IReadOnlyList<RawNewsItem>> FetchSharedAsync(AppConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// 通道 B：按 RSS 模板抓原文，只做解析与过滤，不调用 AI。
/// 模板含 <c>{q}</c> 为「搜索型」（每个关键字各抓一次）；不含则为「共享频道源」（只抓一次）。
/// 主源失败或 0 条时依次尝试备用源，全部失败抛最后一个异常由上层降级。
/// </summary>
public sealed class RssNewsSource : INewsSource, ISharedChannelNewsSource
{
    private const string DefaultKeyword = "新闻";

    /// <summary>共享频道源一次抓取的最少条数（关键字多时按需求量放大）。</summary>
    private const int MinSharedItems = 10;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public RssNewsSource(HttpClient httpClient) => _http = httpClient;

    public string Name => "RSS";

    /// <summary>模板是否含 {q} 占位符（含 = 搜索型，按关键字各抓一次）。</summary>
    public static bool HasKeywordPlaceholder(string? template)
        => !string.IsNullOrEmpty(template) && template.Contains("{q}", StringComparison.Ordinal);

    public Task<IReadOnlyList<RawNewsItem>> FetchAsync(string keyword, AppConfig config, CancellationToken cancellationToken = default)
        => FetchWithFallbackAsync(config, [keyword], cancellationToken);

    /// <summary>共享频道源抓取：一次抓够所有关键字需要的条数。</summary>
    public Task<IReadOnlyList<RawNewsItem>> FetchSharedAsync(AppConfig config, CancellationToken cancellationToken = default)
        => FetchWithFallbackAsync(config, KeywordsOf(config), cancellationToken);

    private async Task<IReadOnlyList<RawNewsItem>> FetchWithFallbackAsync(
        AppConfig config,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (var template in Candidates(config))
        {
            try
            {
                var items = await FetchFromTemplateAsync(template, config, keywords, cancellationToken).ConfigureAwait(false);
                if (items.Count > 0) return items;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 该源不可用，记下原因后尝试下一个备用源
                lastError = ex;
            }
        }

        if (lastError is not null) throw lastError;
        return [];
    }

    private async Task<List<RawNewsItem>> FetchFromTemplateAsync(
        string template,
        AppConfig config,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        if (HasKeywordPlaceholder(template))
        {
            var results = new List<RawNewsItem>();
            foreach (var keyword in keywords)
            {
                var url = template.Replace("{q}", Uri.EscapeDataString(keyword));
                var document = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
                results.AddRange(Select(document, keyword, config, Math.Max(0, config.ItemsPerKeyword)));
            }

            return results;
        }

        var sharedDocument = await DownloadAsync(template, cancellationToken).ConfigureAwait(false);
        var limit = Math.Max(Math.Max(0, config.ItemsPerKeyword) * Math.Max(keywords.Count, 1), MinSharedItems);
        var keywordLabel = ChannelTitle(sharedDocument) ?? DefaultKeyword;
        return Select(sharedDocument, keywordLabel, config, limit);
    }

    private async Task<XDocument> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        string xml;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(RequestTimeout);
            using var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            xml = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }

        // XML 整体损坏时直接抛出，由备用链/上层处理
        return XDocument.Parse(xml);
    }

    private static List<RawNewsItem> Select(XDocument document, string keyword, AppConfig config, int limit)
    {
        var items = new List<RawNewsItem>();
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            try
            {
                var raw = ParseItem(element, keyword);
                if (raw is not null) items.Add(raw);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单条解析失败跳过，不影响其它条目
            }
        }

        var now = DateTimeOffset.Now;
        return items
            .Where(i => !config.OnlyTodayNews || i.PublishedAt is null || i.PublishedAt >= now.AddHours(-24))
            .OrderByDescending(i => i.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(limit)
            .ToList();
    }

    /// <summary>主源 + 备用源，按顺序去重。</summary>
    private static List<string> Candidates(AppConfig config)
    {
        var templates = new List<string>();
        Add(config.RssTemplate);

        var fallbacks = config.RssFallbackTemplates;
        if (fallbacks is not null)
        {
            foreach (var fallback in fallbacks) Add(fallback);
        }

        return templates;

        void Add(string? template)
        {
            if (string.IsNullOrWhiteSpace(template)) return;
            var value = template.Trim();
            if (!templates.Contains(value, StringComparer.Ordinal)) templates.Add(value);
        }
    }

    private static List<string> KeywordsOf(AppConfig config)
        => config.Keywords is null
            ? []
            : [.. config.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim())];

    /// <summary>取频道标题作为共享源条目的关键字标记。</summary>
    private static string? ChannelTitle(XDocument document)
    {
        var channel = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "channel")
            ?? document.Root;
        var title = channel?.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;
        if (string.IsNullOrWhiteSpace(title)) return null;

        var cleaned = CleanText(title);
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static RawNewsItem? ParseItem(XElement element, string keyword)
    {
        var title = CleanText(ChildText(element, "title"));
        var link = ChildText(element, "link");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) return null;

        return new RawNewsItem
        {
            Keyword = keyword,
            Title = title,
            Url = link,
            Description = CleanText(ChildText(element, "description")),
            Source = ChildText(element, "source"),
            PublishedAt = ParseDate(ChildText(element, "pubDate")),
        };
    }

    private static string? ChildText(XElement parent, string localName)
    {
        var text = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>去 HTML 标签、解实体、压缩空白。</summary>
    private static string CleanText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = HtmlTagRegex.Replace(html, " ");
        return WhitespaceRegex.Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
