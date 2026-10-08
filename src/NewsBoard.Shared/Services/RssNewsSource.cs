using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>通道 B：按 RSS 模板抓原文，只做解析与过滤，不调用 AI。</summary>
public sealed class RssNewsSource : INewsSource
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly HttpClient _http;

    public RssNewsSource(HttpClient httpClient) => _http = httpClient;

    public string Name => "RSS";

    public async Task<IReadOnlyList<RawNewsItem>> FetchAsync(string keyword, AppConfig config, CancellationToken cancellationToken = default)
    {
        var url = config.RssTemplate.Replace("{q}", Uri.EscapeDataString(keyword));

        string xml;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(RequestTimeout);
            using var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            xml = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }

        // XML 整体损坏时直接抛出，由上层（协调器）决定降级
        var document = XDocument.Parse(xml);

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
            .Take(Math.Max(0, config.ItemsPerKeyword))
            .ToList();
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
