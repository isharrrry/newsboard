using System.Net;
using System.Text.RegularExpressions;
using NewsBoard.Shared.Models;

namespace NewsBoard.Shared.Services;

/// <summary>从文章页抓 og:image（兜底 twitter:image），仅图文模式使用。</summary>
public sealed class ImageResolver
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private const int MaxConcurrency = 3;

    private static readonly Regex MetaTagRegex = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        @"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))",
        RegexOptions.Compiled);

    private readonly HttpClient _http;

    public ImageResolver(HttpClient httpClient) => _http = httpClient;

    /// <summary>取不到或抓取失败返回 null，不抛异常。</summary>
    public async Task<string?> ResolveAsync(string? articleUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(articleUrl)) return null;

        string html;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            using var response = await _http.GetAsync(articleUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            html = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var source = ExtractImage(html);
        return source is null ? null : MakeAbsolute(articleUrl, source);
    }

    /// <summary>仅图文模式执行，并发上限 3。</summary>
    public async Task ApplyImagesAsync(
        IReadOnlyList<NewsItem> items,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        if (config.PlaybackMode != PlaybackMode.TextAndImage || items.Count == 0) return;

        using var gate = new SemaphoreSlim(MaxConcurrency);
        var tasks = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Url) && string.IsNullOrWhiteSpace(i.ImageUrl))
            .Select(async item =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    item.ImageUrl = await ResolveAsync(item.Url, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static string? ExtractImage(string html)
    {
        string? openGraph = null;
        string? twitter = null;

        foreach (Match match in MetaTagRegex.Matches(html))
        {
            var attributes = ParseAttributes(match.Value);
            if (!attributes.TryGetValue("content", out var content) || string.IsNullOrWhiteSpace(content)) continue;

            if (attributes.TryGetValue("property", out var property)
                && (Is(property, "og:image") || Is(property, "og:image:url")) && openGraph is null)
            {
                openGraph = content;
            }

            if (attributes.TryGetValue("name", out var name))
            {
                if (openGraph is null && Is(name, "og:image")) openGraph = content;
                if (twitter is null && (Is(name, "twitter:image") || Is(name, "twitter:image:src"))) twitter = content;
            }
        }

        return openGraph ?? twitter;
    }

    private static bool Is(string value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(tag))
        {
            var value = match.Groups[2].Success ? match.Groups[2].Value
                : match.Groups[3].Success ? match.Groups[3].Value
                : match.Groups[4].Success ? match.Groups[4].Value
                : string.Empty;
            attributes[match.Groups[1].Value] = WebUtility.HtmlDecode(value);
        }

        return attributes;
    }

    private static string? MakeAbsolute(string articleUrl, string source)
    {
        if (!Uri.TryCreate(articleUrl, UriKind.Absolute, out var baseUri)) return null;
        if (!Uri.TryCreate(baseUri, source, out var combined)) return null;
        return combined.Scheme == Uri.UriSchemeHttp || combined.Scheme == Uri.UriSchemeHttps
            ? combined.ToString()
            : null;
    }
}
