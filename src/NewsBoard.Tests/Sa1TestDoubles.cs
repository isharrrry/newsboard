using System.Net;
using System.Text;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Tests;

/// <summary>记录请求内容 + 返回预置响应的假 HttpMessageHandler。</summary>
internal sealed record RecordedRequest(string Url, string? Body, IReadOnlyDictionary<string, string> Headers);

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;
    private readonly object _sync = new();
    private readonly List<RecordedRequest> _recorded = [];

    public FakeHttpHandler(Func<RecordedRequest, HttpResponseMessage> responder) => _responder = responder;

    public IReadOnlyList<RecordedRequest> Recorded
    {
        get { lock (_sync) return [.. _recorded]; }
    }

    public int Count
    {
        get { lock (_sync) return _recorded.Count; }
    }

    public int CountOf(string urlFragment)
        => Recorded.Count(r => r.Url.Contains(urlFragment, StringComparison.OrdinalIgnoreCase));

    public HttpClient CreateClient() => new(this, disposeHandler: false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers) headers[header.Key] = string.Join(",", header.Value);

        var recorded = new RecordedRequest(request.RequestUri!.AbsoluteUri, body, headers);
        lock (_sync) _recorded.Add(recorded);

        return _responder(recorded);
    }
}

internal static class FakeHttp
{
    public static HttpResponseMessage Ok(string content, string mediaType = "application/json")
        => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, mediaType) };

    public static HttpResponseMessage Status(HttpStatusCode status)
        => new(status) { Content = new StringContent(string.Empty) };
}

internal sealed class FakeConfigProvider : IConfigProvider
{
    public FakeConfigProvider(AppConfig? config = null) => Current = config ?? new AppConfig();

    public AppConfig Current { get; set; }

    public event Action<AppConfig>? Changed;

    public void Raise(AppConfig config)
    {
        Current = config;
        Changed?.Invoke(config);
    }
}

/// <summary>临时目录，测试结束自动清理。</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "newsboard-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论
        }
    }
}

internal static class Sa1Data
{
    public static AppConfig Config(params string[] keywords) => new()
    {
        Keywords = [.. keywords],
        ItemsPerKeyword = 3,
        SummaryMaxChars = 50,
        AiMinIntervalMinutes = 10,
        ApiKey = "test-key",
    };

    public static RawNewsItem Raw(string url, string title, string? description = null, DateTimeOffset? publishedAt = null, string keyword = "武汉")
        => new()
        {
            Keyword = keyword,
            Title = title,
            Url = url,
            Description = description ?? $"{title} 的正文",
            Source = "示例站",
            PublishedAt = publishedAt,
        };

    /// <summary>构造 RSS 文档；Published 为 null 时不写 pubDate。</summary>
    public static string Rss(params (string Title, string Link, DateTimeOffset? Published)[] items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<rss version=\"2.0\"><channel><title>测试源</title>");
        foreach (var (title, link, published) in items)
        {
            builder.AppendLine("<item>");
            builder.AppendLine($"<title>{title}</title>");
            builder.AppendLine($"<link>{link}</link>");
            builder.AppendLine("<description>&lt;p&gt;正文 &amp;amp; 片段&lt;/p&gt;</description>");
            if (published is not null) builder.AppendLine($"<pubDate>{published.Value.ToString("R")}</pubDate>");
            builder.AppendLine("<source url=\"https://source.example.com\">示例站</source>");
            builder.AppendLine("</item>");
        }

        builder.AppendLine("</channel></rss>");
        return builder.ToString();
    }

    /// <summary>构造 OpenAI 兼容的 chat/completions 响应。</summary>
    public static string ChatResponse(params (string Url, string Summary)[] pairs)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(
            pairs.Select(p => new { url = p.Url, summary = p.Summary }));
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            id = "chatcmpl-1",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = payload } } },
        });
    }

    /// <summary>构造 Anthropic 兼容 + 联网搜索的响应（含工具块）。</summary>
    public static string WebSearchResponse(string jsonArray)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize($"检索结果如下：\n```json\n{jsonArray}\n```");
        return $$$"""
        {
          "id": "msg_1",
          "model": "deepseek-v4-flash",
          "content": [
            {"type": "server_tool_use", "id": "srvtoolu_1", "name": "web_search", "input": {"query": "武汉"}},
            {"type": "web_search_tool_result", "tool_use_id": "srvtoolu_1", "content": [
              {"type": "web_search_result", "title": "结果", "url": "https://news.example.com/1", "encrypted_content": "abc"}
            ]},
            {"type": "text", "text": {{{escaped}}}}
          ],
          "stop_reason": "end_turn"
        }
        """;
    }
}
