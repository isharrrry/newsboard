using System.Text;
using System.Text.Json;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>通道 A：模型联网搜索，直接产出带摘要的条目（不再走提炼）。</summary>
public sealed class WebSearchNewsSource : INewsSource
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    private readonly HttpClient _http;

    public WebSearchNewsSource(HttpClient httpClient) => _http = httpClient;

    public string Name => "模型联网搜索";

    public async Task<IReadOnlyList<RawNewsItem>> FetchAsync(string keyword, AppConfig config, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey)) throw new InvalidOperationException("未配置 API Key");

        var endpoint = $"{config.BaseUrlSearch.TrimEnd('/')}/v1/messages";
        var body = JsonSerializer.Serialize(new
        {
            model = config.Model,
            max_tokens = 2500,
            messages = new[] { new { role = "user", content = BuildPrompt(keyword, config) } },
            tools = new[]
            {
                new
                {
                    type = "web_search_20250305",
                    name = "web_search",
                    max_uses = config.SearchMaxUses,
                },
            },
        });

        string responseBody;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(RequestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        }

        // 响应里同时含 server_tool_use / web_search_tool_result 块，只取 text 块
        var text = AiJson.ExtractTextBlocks(responseBody);
        var array = AiJson.ExtractArray(text);
        if (array is null) return [];

        var now = DateTimeOffset.Now;
        var items = new List<RawNewsItem>();
        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            var url = AiJson.GetString(element, "url")?.Trim();
            var summary = SummaryText.Normalize(AiJson.GetString(element, "summary"), config.SummaryMaxChars);
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(summary)) continue;

            items.Add(new RawNewsItem
            {
                Keyword = keyword,
                Title = AiJson.GetString(element, "title")?.Trim(),
                Url = url,
                Source = "联网搜索",
                PreSummarized = summary,
                PublishedAt = now,
            });
        }

        return items.Take(Math.Max(0, config.ItemsPerKeyword)).ToList();
    }

    private static string BuildPrompt(string keyword, AppConfig config)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"请针对关键字「{keyword}」联网检索当天的新闻，挑选最新 {config.ItemsPerKeyword} 篇。");
        builder.AppendLine("硬性要求：");
        builder.AppendLine("1. 只依据联网检索到的真实结果，严禁编造标题、链接或内容。");
        builder.AppendLine($"2. 每条摘要不超过 {config.SummaryMaxChars} 个汉字；写 2–3 句连贯的话，先交代「谁 + 做了什么」，再补「关键数字或结果」，最后补「影响或后续」，把检索到的事实说完整，不要只写一句概括。");
        builder.AppendLine("3. 用陈述语气直接叙述事实，不要写「本文」「据报道」「摘要」等字样，不要复述标题。");
        builder.AppendLine("4. 只输出一个严格的 JSON 数组，不要任何解释文字或 Markdown 围栏，格式为：");
        builder.AppendLine($"[{{\"title\":\"标题\",\"summary\":\"摘要\",\"url\":\"原文链接\",\"keyword\":\"{keyword}\"}}]");
        builder.AppendLine("5. 检索不到足够结果时少输出几条即可，不要编造。");
        return builder.ToString();
    }
}
