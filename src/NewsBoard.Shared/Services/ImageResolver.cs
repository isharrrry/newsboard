using System.Net;
using System.Text.RegularExpressions;
using NewsBoard.Shared.Models;

namespace NewsBoard.Shared.Services;

/// <summary>
/// 从文章页取配图：先 og:image / twitter:image / link[rel=image_src]，都没有再退化到正文首图。
/// 实测：中新网、人民网的文章页都没有 og:image（RSS 也没有 enclosure/media 字段），
/// 只看 meta 会让图文模式永远取不到图，因此必须有正文首图兜底。
/// </summary>
public sealed class ImageResolver
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
    private const int MaxConcurrency = 3;

    private static readonly Regex MetaTagRegex = new(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LinkTagRegex = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ImageTagRegex = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        @"([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))",
        RegexOptions.Compiled);

    /// <summary>正文容器标记：命中后只在该区域内找配图，避免抓到导航/广告图。</summary>
    private static readonly string[] ContentMarkers =
    [
        "cont_1_1_2", "id=\"content\"", "class=\"content", "class=\"article",
        "article-content", "rm_txt_con", "main-content", "id=\"article",
    ];

    /// <summary>站点装饰图特征，命中即丢弃。</summary>
    private static readonly string[] NoiseMarkers =
    [
        "logo", "icon", "spacer", "blank.", "/blank", "toparr", "qw.jpg", "jw.jpg",
        "2022homepage", "share", "qrcode", "weixin", "weibo", "banner", "advert",
        "ad_", "pixel.gif", "loading.gif", "avatar", "default.jpg",
        // 实测：这两个站点把模板图/页脚图混在正文里
        "peopleindex", "/default/", "template", "copy_py", "sj_wx", "_wx.", "2020wbc",
    ];

    /// <summary>配图 URL 里嵌的日期比文章日期早这么多天，就判定为站点模板素材。</summary>
    private const int StaleImageDays = 60;

    private static readonly Regex UrlDateRegex = new(@"(\d{4})[-_/]?(\d{2})[-_/]?(\d{2})", RegexOptions.Compiled);


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

        // 候选逐个校验：必须真能当图片加载（实测中新网的图床防盗链，直接取回的是 html）
        foreach (var candidate in ExtractCandidates(html, ExtractArticleDate(articleUrl)))
        {
            var absolute = MakeAbsolute(articleUrl, candidate);
            if (absolute is null) continue;

            if (await IsLoadableImageAsync(absolute, cancellationToken).ConfigureAwait(false))
            {
                return absolute;
            }
        }

        return null;
    }

    /// <summary>用 Range 只取头部若干字节，判断该地址是否真的返回图片（不下载整张图）。</summary>
    private async Task<bool> IsLoadableImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("Range", "bytes=0-2047");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return false;

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return false;

            var length = response.Content.Headers.ContentLength;
            return length is null or > 1024;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
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

    /// <summary>按优先级给出候选配图（og → twitter → link[rel=image_src] → 正文首图…），调用方逐个校验可用性。</summary>
    private static List<string> ExtractCandidates(string html, DateOnly? articleDate)
    {
        var candidates = new List<string>();

        void Add(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return;
            if (candidates.Contains(trimmed, StringComparer.Ordinal)) return;
            candidates.Add(trimmed);
        }

        string? openGraph = null;
        string? twitter = null;
        string? imageSrcLink = null;

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

        Add(openGraph);
        Add(twitter);

        foreach (Match match in LinkTagRegex.Matches(html))
        {
            var attributes = ParseAttributes(match.Value);
            if (!attributes.TryGetValue("rel", out var rel) || !Is(rel, "image_src")) continue;
            if (attributes.TryGetValue("href", out var href) && !string.IsNullOrWhiteSpace(href))
            {
                imageSrcLink = href;
                break;
            }
        }

        Add(imageSrcLink);

        // 正文首图兜底（中新网/人民网这类没有 og:image 的站点）
        foreach (var image in ExtractContentImages(ScopedToContent(html), articleDate)) Add(image);
        foreach (var image in ExtractContentImages(html, articleDate)) Add(image);

        return candidates;
    }

    /// <summary>从文章 URL 里取发布日期；取不到返回 null。</summary>
    internal static DateOnly? ExtractArticleDate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var match = UrlDateRegex.Match(url);
        while (match.Success)
        {
            if (TryMakeDate(match, out var date)) return date;
            match = match.NextMatch();
        }

        return null;
    }

    /// <summary>图片 URL 里嵌的日期比文章早超过 <see cref="StaleImageDays"/> 天即视为模板素材。</summary>
    private static bool IsStaleTemplateImage(string source, DateOnly? articleDate)
    {
        if (articleDate is null) return false;

        var match = UrlDateRegex.Match(source);
        while (match.Success)
        {
            if (TryMakeDate(match, out var date))
            {
                return articleDate.Value.DayNumber - date.DayNumber > StaleImageDays;
            }

            match = match.NextMatch();
        }

        return false;
    }

    private static bool TryMakeDate(Match match, out DateOnly date)
    {
        date = default;
        if (!int.TryParse(match.Groups[1].Value, out var year)) return false;
        if (!int.TryParse(match.Groups[2].Value, out var month)) return false;
        if (!int.TryParse(match.Groups[3].Value, out var day)) return false;
        if (year is < 2000 or > 2100 || month is < 1 or > 12 || day is < 1 or > 31) return false;

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>把 HTML 收窄到正文容器之后的部分；找不到容器就返回原文。</summary>
    private static string ScopedToContent(string html)
    {
        var best = -1;
        foreach (var marker in ContentMarkers)
        {
            var index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best)) best = index;
        }

        if (best < 0) return html;

        var end = html.IndexOf("</body>", best, StringComparison.OrdinalIgnoreCase);
        return end > best ? html[best..end] : html[best..];
    }

    private static IEnumerable<string> ExtractContentImages(string html, DateOnly? articleDate)
    {
        // 正文图片通常在正文段落之后：有 <p> 就只从它之后找，避免抓到页头模板图
        var bodyStart = html.IndexOf("<p", StringComparison.OrdinalIgnoreCase);

        foreach (Match match in ImageTagRegex.Matches(html))
        {
            if (bodyStart >= 0 && match.Index < bodyStart) continue;

            var attributes = ParseAttributes(match.Value);
            if (IsTiny(attributes)) continue;

            // src 可能是占位图（loading.gif 之类），逐个候选源试，别因为第一个不合格就丢掉整张图
            foreach (var candidate in Sources(attributes))
            {
                var source = candidate.Trim();
                if (source.Length == 0) continue;
                if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (LooksLikeNoise(source)) continue;
                if (IsStaleTemplateImage(source, articleDate)) continue;

                yield return source;
                break;
            }
        }
    }

    private static IEnumerable<string> Sources(Dictionary<string, string> attributes)
    {
        foreach (var key in new[] { "src", "data-src", "data-original", "data-echo" })
        {
            if (attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) yield return value;
        }
    }

    private static bool LooksLikeNoise(string source)
    {
        foreach (var marker in NoiseMarkers)
        {
            if (source.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool IsTiny(Dictionary<string, string> attributes)
    {
        foreach (var key in new[] { "width", "height" })
        {
            if (!attributes.TryGetValue(key, out var raw)) continue;
            var digits = new string([.. raw.TakeWhile(char.IsDigit)]);
            if (digits.Length > 0 && int.TryParse(digits, out var value) && value <= 2) return true;
        }

        return false;
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
