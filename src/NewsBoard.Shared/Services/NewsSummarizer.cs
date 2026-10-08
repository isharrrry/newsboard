using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewsBoard.Shared.Models;

namespace NewsBoard.Shared.Services;

/// <summary>通道 B 的提炼：同一关键字的全部原文批量一次请求，失败重试 1 次后退化为标题。</summary>
public sealed class NewsSummarizer
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private const int MaxAttempts = 2;

    private readonly HttpClient _http;

    public NewsSummarizer(HttpClient httpClient) => _http = httpClient;

    /// <summary>条目 Id：URL 的 SHA256 十六进制前 16 位。</summary>
    public static string MakeId(string url)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    public async Task<IReadOnlyList<NewsItem>> SummarizeAsync(
        string keyword,
        IReadOnlyList<RawNewsItem> raw,
        AppConfig config,
        CancellationToken cancellationToken = default)
    {
        var candidates = raw.Where(r => !string.IsNullOrWhiteSpace(r.Url)).ToList();
        if (candidates.Count == 0) return [];
        if (string.IsNullOrWhiteSpace(config.ApiKey)) throw new InvalidOperationException("未配置 API Key");

        var now = DateTimeOffset.Now;
        var summaries = await TrySummarizeAsync(keyword, candidates, config, cancellationToken).ConfigureAwait(false);

        var items = new List<NewsItem>();
        foreach (var candidate in candidates)
        {
            var url = candidate.Url!.Trim();
            string summary;
            if (summaries is not null)
            {
                // 模型没给这条（或给空）就跳过，不退化为标题
                summary = summaries.TryGetValue(url, out var text) ? SummaryText.Normalize(text, config.SummaryMaxChars) : string.Empty;
            }
            else
            {
                // AI 全部失败：退化为原标题截断
                summary = Truncate(CleanInline(candidate.Title), config.SummaryMaxChars);
            }

            if (summary.Length == 0) continue;

            items.Add(new NewsItem
            {
                Id = MakeId(url),
                Title = candidate.Title ?? string.Empty,
                Summary = summary,
                Url = url,
                Keyword = candidate.Keyword,
                Source = candidate.Source,
                PublishedAt = candidate.PublishedAt ?? now,
                FetchedAt = now,
            });
        }

        return items;
    }

    private static string Truncate(string text, int maxChars)
        => maxChars > 0 && text.Length > maxChars ? text[..maxChars] : text;

    private static string CleanInline(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>返回 url → summary；失败（含重试后）返回 null。</summary>
    private async Task<Dictionary<string, string>?> TrySummarizeAsync(
        string keyword,
        IReadOnlyList<RawNewsItem> candidates,
        AppConfig config,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{config.BaseUrl.TrimEnd('/')}/chat/completions";
        var prompt = BuildPrompt(keyword, candidates, config);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(RequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.ApiKey}");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model = config.Model,
                        messages = new[] { new { role = "user", content = prompt } },
                    }),
                    Encoding.UTF8,
                    "application/json");

                using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;

                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var parsed = Parse(body);
                if (parsed is not null) return parsed;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // 网络/解析失败：进入下一次尝试
            }
        }

        return null;
    }

    /// <summary>返回 url → summary（可能为空字典）；响应结构不可用时返回 null 表示失败。</summary>
    private static Dictionary<string, string>? Parse(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string? content;
        try
        {
            using var document = JsonDocument.Parse(body);
            content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        var array = AiJson.ExtractArray(content);
        if (array is null) return null;

        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var url = AiJson.GetString(element, "url")?.Trim();
            var summary = AiJson.GetString(element, "summary");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(summary)) continue;
            result[url] = summary;
        }

        return result;
    }

    private static string BuildPrompt(string keyword, IReadOnlyList<RawNewsItem> candidates, AppConfig config)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"你是新闻摘要编辑。下面是关键字「{keyword}」的 {candidates.Count} 篇新闻原文片段，请为每一篇写一段摘要。");
        builder.AppendLine("硬性要求：");
        builder.AppendLine("1. 只依据给出的原文，严禁编造数字、时间、人名、地点。");
        builder.AppendLine($"2. 每条摘要不超过 {config.SummaryMaxChars} 个汉字（写到 {config.SummaryMaxChars - 15}–{config.SummaryMaxChars} 字最好）；写 1–2 句连贯的话：先用一句交代「谁 + 做了什么」，再补一句关键的「数字或结果」，把原文里最重要的事实说清楚，不要复述标题，也不要写成标题的扩写。");
        builder.AppendLine("3. 用陈述语气直接叙述事实，不要写「本文」「据报道」「摘要」等字样，不要复述标题。");
        builder.AppendLine("4. 只输出一个严格的 JSON 数组，不要任何解释文字或 Markdown 围栏，元素形如 {\"url\":\"原文链接\",\"summary\":\"摘要\"}。");
        builder.AppendLine("5. url 必须与下面给出的原文链接完全一致。");
        builder.AppendLine("6. 原文信息不足以写摘要时，summary 留空字符串，不要猜测。");
        builder.AppendLine();
        builder.AppendLine("原文：");

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            builder.AppendLine($"{i + 1}. url: {candidate.Url}");
            builder.AppendLine($"   标题: {candidate.Title}");
            builder.AppendLine($"   内容: {candidate.Description}");
        }

        return builder.ToString();
    }
}

/// <summary>解析模型返回的 JSON 数组，容忍 ```json 围栏与前后解释文字。</summary>
internal static class AiJson
{
    /// <summary>取第一个 '[' 到最后一个 ']' 之间的内容并解析；失败返回 null。</summary>
    public static JsonElement? ExtractArray(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return null;

        try
        {
            // 复制一份，避免调用方释放 JsonDocument
            return JsonDocument.Parse(text[start..(end + 1)]).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>取 Anthropic 风格响应里所有 type=="text" 块拼接后的文本。</summary>
    public static string? ExtractTextBlocks(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                return null;

            var builder = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (!string.Equals(GetString(block, "type"), "text", StringComparison.Ordinal)) continue;
                var text = GetString(block, "text");
                if (!string.IsNullOrEmpty(text)) builder.AppendLine(text);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
