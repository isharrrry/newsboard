using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsBoard.Shared.Services;

/// <summary>
/// 抓文章原文页并提取正文纯文本：RSS 只给几十字 description、联网搜索通道拿不到正文，
/// 都需要它来补 <c>NewsItem.RawContent</c>。HTML 处理只用正则/字符串，不引第三方解析库。
/// </summary>
public sealed class ArticleContentFetcher
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>正文容器标记（class / id 片段），命中后只看该容器。</summary>
    private static readonly string[] ContentMarkers =
    [
        "content_desc", "cont_1_1_2", "article-content", "article_content",
        "post_content", "news_content", "detail-content", "TRS_Editor",
        // 实测（2026-10-08）：人民网政治频道的正文容器是 class="rm_txt_con cf"，上面这些都不命中
        "rm_txt_con",
    ];

    /// <summary>站点尾部噪声行特征，整行丢弃。</summary>
    private static readonly string[] NoiseLines =
    [
        "责任编辑", "上一篇", "下一篇", "扫一扫", "返回顶部", "分享到", "点击查看更多",
    ];

    /// <summary>尾部裁剪用的噪声特征（比 <see cref="NoiseLines"/> 宽：页脚、站内导航、推荐位）。</summary>
    private static readonly string[] TailNoiseWords =
    [
        "版权", "版权所有", "all rights reserved", "copyright", "京icp", "京网文", "备案",
        "许可证", "举报", "关于我们", "关于人民网", "报社招聘", "招聘英才", "广告服务", "广告声明",
        "合作加盟", "服务邮箱", "联系我们", "联系方式", "软媒", "返利", "优惠券",
        "相关阅读", "相关新闻", "相关推荐", "热门推荐", "推荐阅读", "猜你喜欢",
        "分享", "评论", "顶部", "编辑：", "来源：",
    ];

    /// <summary>尾部裁剪：短行（无句号）也算噪声。</summary>
    /// <summary>尾部裁剪：短行（无句号）也算噪声。实测中新网的推荐位标题能长到 31 字，所以放到 45。</summary>
    private const int ShortTailLineChars = 45;

    /// <summary>站点常见的正文结束标记，出现在尾部时它后面全部丢弃。</summary>
    private static readonly string[] ArticleEndMarkers = ["(完)", "（完）"];

    /// <summary>正文最少字数：低于它认为没提到正文。</summary>
    private const int MinTextChars = 60;

    /// <summary>全文档启发式：单段至少这么长才算正文段落。</summary>
    private const int MinParagraphChars = 8;

    /// <summary>全文档启发式：收集到的正文总量至少这么长才采用。</summary>
    private const int MinFallbackChars = 120;

    private static readonly Regex CommentRegex = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ScriptStyleRegex = new(
        @"<(script|style|noscript)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LayoutRegex = new(
        @"<(nav|header|footer|aside)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ParagraphRegex = new(
        @"<p\b[^>]*>(.*?)</p\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex BlockSplitRegex = new(
        @"<(?:br|/p|/div|/section|/article|/li|/h[1-6])\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"[ \t\r\n\u3000\u00A0]+", RegexOptions.Compiled);

    private static readonly Regex TagNameRegex = new(@"^<\s*([A-Za-z][A-Za-z0-9]*)", RegexOptions.Compiled);

    private static readonly Regex CharsetRegex = new(
        @"charset\s*=\s*[""']?\s*([A-Za-z0-9_\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public ArticleContentFetcher(HttpClient httpClient) => _http = httpClient;

    /// <summary>
    /// 抓取并提取正文；失败或无正文返回 null，不向上抛异常（含超时、非 2xx、空响应、解析失败；
    /// 调用方取消同样返回 null，与 <see cref="ImageResolver"/>「外部取消向上传播」的约定不同）。
    /// </summary>
    public async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");

            using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            var html = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
            return ExtractMainText(html);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>从 HTML 提取正文纯文本（纯函数，便于单测）；提取不到返回 null。</summary>
    public static string? ExtractMainText(string html, int maxChars = 4000)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var cleaned = RemoveNoise(html);

        var markerIndex = FindContentMarker(cleaned);
        if (markerIndex >= 0)
        {
            var region = SliceContainer(cleaned, TagStartBefore(cleaned, markerIndex));
            var paragraphs = CollectParagraphs(region, filterShort: false);
            if (paragraphs.Count == 0)
            {
                // 有些站点正文不用 <p>（直接 <div>/<br> 分段），容器命中时退化为整块取文本
                paragraphs = CollectBlockText(region);
            }

            return Finish(TrimTailNoise(paragraphs), maxChars, MinTextChars);
        }

        // 没有已知容器：全文档收 <p>，短段丢弃，总量够长才敢用
        return Finish(TrimTailNoise(CollectParagraphs(cleaned, filterShort: true)), maxChars, MinFallbackChars);
    }

    /// <summary>先摘掉一定能确定是噪声的块：注释、脚本样式、导航页头页脚侧栏。</summary>
    private static string RemoveNoise(string html)
    {
        var cleaned = CommentRegex.Replace(html, string.Empty);
        cleaned = ScriptStyleRegex.Replace(cleaned, string.Empty);
        cleaned = LayoutRegex.Replace(cleaned, string.Empty);
        return cleaned;
    }

    /// <summary>返回最早出现的正文容器标记下标；都没有返回 -1。</summary>
    private static int FindContentMarker(string html)
    {
        var best = -1;
        foreach (var marker in ContentMarkers)
        {
            var index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best)) best = index;
        }

        return best;
    }

    private static int TagStartBefore(string html, int index)
    {
        var start = html.LastIndexOf('<', Math.Max(0, index));
        return start < 0 ? 0 : start;
    }

    /// <summary>从容器起始标签切到配平的结束标签（只按同名标签计数，避免嵌套 <div> 被提前截断）。</summary>
    private static string SliceContainer(string html, int tagStart)
    {
        if (tagStart < 0 || tagStart >= html.Length) return html;

        var nameMatch = TagNameRegex.Match(html, tagStart);
        if (!nameMatch.Success || nameMatch.Index != tagStart) return html[tagStart..];

        var name = nameMatch.Groups[1].Value;
        var tagRegex = new Regex($@"<(/?){Regex.Escape(name)}\b[^>]*>", RegexOptions.IgnoreCase);
        var depth = 0;
        var position = tagStart;

        while (position < html.Length)
        {
            var match = tagRegex.Match(html, position);
            if (!match.Success) return html[tagStart..];

            if (match.Groups[1].Value.Length > 0)
            {
                depth--;
                if (depth == 0) return html[tagStart..(match.Index + match.Length)];
            }
            else
            {
                depth++;
            }

            position = match.Index + match.Length;
        }

        return html[tagStart..];
    }

    private static List<string> CollectParagraphs(string html, bool filterShort)
    {
        var lines = new List<string>();
        foreach (Match match in ParagraphRegex.Matches(html))
        {
            var text = Clean(match.Groups[1].Value);
            if (text.Length == 0) continue;
            if (filterShort && text.Length < MinParagraphChars) continue;
            if (IsHardNoiseLine(text)) continue;

            lines.Add(text);
        }

        return lines;
    }

    /// <summary>容器内没有 &lt;p&gt; 时的兜底：按 br/块级结束标签切块，再逐块清文本。</summary>
    private static List<string> CollectBlockText(string html)
    {
        var lines = new List<string>();
        foreach (var raw in BlockSplitRegex.Replace(html, "\n").Split('\n'))
        {
            var text = Clean(raw);
            if (text.Length < MinParagraphChars) continue;
            if (IsHardNoiseLine(text)) continue;

            lines.Add(text);
        }

        return lines;
    }

    /// <summary>去标签 → 解实体 → 压缩空白（含全角空格与 &amp;nbsp;）。</summary>
    private static string Clean(string fragment)
        => WhitespaceRegex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(fragment, string.Empty)), " ").Trim();

    /// <summary>硬噪声：行里出现「责任编辑 / 上一篇 / …」这类一定不是正文的词，直接整行丢弃。</summary>
    private static bool IsHardNoiseLine(string line)
    {
        foreach (var marker in NoiseLines)
        {
            if (line.Contains(marker, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>尾部噪声：命中特征词，或「无中文句号的短行」，或「以 ASCII 为主的短行」（英文版权/论文标题）。</summary>
    private static bool IsTailNoiseLine(string line)
    {
        foreach (var word in TailNoiseWords)
        {
            if (line.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (line.Length <= ShortTailLineChars && !line.Contains('。')) return true;
        return line.Length <= 200 && !line.Contains('。') && AsciiRatio(line) > 0.6;
    }

    /// <summary>去掉尾部连续噪声行（正文中间的「分享」之类不误杀，只有尾部才裁）。</summary>
    private static List<string> TrimTailNoise(List<string> lines)
    {
        // 中新网/人民网这类站点用「(完)」标记正文结束，它后面是推荐位与版权，整段丢掉
        var lastEnd = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (ArticleEndMarkers.Any(marker => lines[i].Contains(marker, StringComparison.Ordinal))) lastEnd = i;
        }

        if (lastEnd >= 0 && lastEnd < lines.Count - 1)
        {
            lines.RemoveRange(lastEnd + 1, lines.Count - lastEnd - 1);
        }

        var end = lines.Count;
        while (end > 0 && IsTailNoiseLine(lines[end - 1])) end--;
        if (end == lines.Count) return lines;

        lines.RemoveRange(end, lines.Count - end);
        return lines;
    }

    private static double AsciiRatio(string text)
    {
        if (text.Length == 0) return 0;
        var ascii = 0;
        foreach (var ch in text)
        {
            if (ch < 128) ascii++;
        }

        return (double)ascii / text.Length;
    }

    private static string? Finish(List<string> lines, int maxChars, int requiredLength)
    {
        var text = string.Join(' ', lines).Trim();
        if (text.Length < requiredLength) return null;
        if (maxChars > 0 && text.Length > maxChars) text = text[..maxChars];
        return text;
    }

    /// <summary>按响应头 charset 或 HTML 里的 &lt;meta charset&gt; 解码；都拿不到按 UTF-8。
    /// GB2312/GBK 需要 CodePagesEncodingProvider（本仓库不引第三方包、Shared 零依赖），
    /// 拿不到该编码时按 UTF-8 兜底（中文可能乱码）；目标站点实测均为 UTF-8。</summary>
    private static string Decode(byte[] bytes, string? charset)
    {
        var name = string.IsNullOrWhiteSpace(charset) ? DetectCharsetFromHtml(bytes) : charset;
        if (!string.IsNullOrWhiteSpace(name))
        {
            try
            {
                return Encoding.GetEncoding(name.Trim().Trim('"', '\'')).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // 系统里没有这个编码：落到 UTF-8
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? DetectCharsetFromHtml(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));
        var match = CharsetRegex.Match(head);
        return match.Success ? match.Groups[1].Value : null;
    }
}
