using System.Net;
using System.Text;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// SA10：共享频道源的分类归类（<see cref="KeywordClassifier"/>）+ 原文正文补抓
/// （<see cref="ArticleContentFetcher"/> 经 <see cref="NewsFetchCoordinator"/> 接入）的集成测试。
/// </summary>
public class Sa10KeywordAndContentTests
{
    private const string WuhanUrl = "https://example.com/wuhan";
    private const string PlainUrl = "https://example.com/plain";
    private const string AiUrl = "https://example.com/ai";

    private static (NewsFetchCoordinator Coordinator, NewsRepository Repository) Build(
        AppConfig config,
        TempDirectory temp,
        FakeHttpHandler handler,
        ArticleContentFetcher? contentFetcher = null)
    {
        var client = handler.CreateClient();
        var repository = new NewsRepository(temp.Path);
        var coordinator = new NewsFetchCoordinator(
            [new RssNewsSource(client), new WebSearchNewsSource(client)],
            new NewsSummarizer(client),
            new ImageResolver(client),
            repository,
            new FakeConfigProvider(config),
            contentFetcher);

        return (coordinator, repository);
    }

    /// <summary>自定义 description 的 RSS：共享频道源场景需要按描述内容验证归类。</summary>
    private static string RssWithDescriptions(params (string Title, string Link, string Description)[] items)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<rss version=\"2.0\"><channel><title>测试源</title>");
        foreach (var (title, link, description) in items)
        {
            builder.AppendLine("<item>");
            builder.AppendLine($"<title>{title}</title>");
            builder.AppendLine($"<link>{link}</link>");
            builder.AppendLine($"<description>{description}</description>");
            builder.AppendLine($"<pubDate>{DateTimeOffset.UtcNow.AddMinutes(-5).ToString("R")}</pubDate>");
            builder.AppendLine("</item>");
        }

        builder.AppendLine("</channel></rss>");
        return builder.ToString();
    }

    /// <summary>同一条 chat 响应覆盖本轮所有条目（只按 url 取用，多余的会被忽略）。</summary>
    private static FakeHttpHandler ChatAndRss(string rss)
        => new(request => request.Url.Contains("/chat/completions", StringComparison.Ordinal)
            ? FakeHttp.Ok(Sa1Data.ChatResponse((WuhanUrl, "武汉摘要"), (PlainUrl, "普通摘要"), (AiUrl, "AI 摘要")))
            : FakeHttp.Ok(rss, "application/rss+xml"));

    /// <summary>文章页响应：注意 StringContent 的 mediaType 不能带 charset，否则会抛 FormatException。</summary>
    private static HttpResponseMessage Html(string html)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        };
        response.Content.Headers.ContentType!.CharSet = "utf-8";
        return response;
    }

    [Fact]
    public async Task 共享频道源_标题命中关键字归到该关键字_未命中保持频道名()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉", "AI");
        var handler = ChatAndRss(Sa1Data.Rss(
            ("武汉光谷发布新一代存储控制器", WuhanUrl, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1)),
            ("某地举办秋季农产品展销会", PlainUrl, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2))));
        var (coordinator, repository) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(2, result.NewCount);
        var playlist = repository.GetPlaylist(config);
        Assert.Equal("武汉", playlist.Single(i => i.Url == WuhanUrl).Keyword);
        Assert.Equal("测试源", playlist.Single(i => i.Url == PlainUrl).Keyword);
    }

    [Fact]
    public async Task 共享频道源_标题没命中但描述命中时按描述归类()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉", "AI");
        var handler = ChatAndRss(RssWithDescriptions(
            ("某公司发布新一代推理芯片", AiUrl, "该芯片面向 AI 推理场景，单卡显存一百四十四吉字节，已开始向国内客户送样。")));
        var (coordinator, repository) = Build(config, temp, handler);

        var result = await coordinator.FetchAsync();

        Assert.Equal(1, result.NewCount);
        Assert.Equal("AI", Assert.Single(repository.GetPlaylist(config)).Keyword);
    }

    [Fact]
    public async Task 正文补抓生效_短描述条目入库的是原文正文()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        const string articleHtml = """
            <html><body>
            <div class="content_desc">
            <p>武汉新增的三条地铁线路今天开通运营，线路总长四十二公里，共设车站三十三座，串联起四个城区与两个火车站。</p>
            <p>运营方称开通首日安排二十列车上线，高峰期最小行车间隔缩短到四分钟，沿线居民通勤时间可减少约二十分钟。</p>
            </div>
            </body></html>
            """;
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
                return FakeHttp.Ok(Sa1Data.ChatResponse((PlainUrl, "普通摘要")));

            if (request.Url.Contains("example.com/plain", StringComparison.Ordinal))
                return Html(articleHtml);

            return FakeHttp.Ok(Sa1Data.Rss(
                ("本地轨道交通迎来新进展", PlainUrl, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1))),
                "application/rss+xml");
        });
        var (coordinator, repository) = Build(config, temp, handler, new ArticleContentFetcher(handler.CreateClient()));

        var result = await coordinator.FetchAsync();

        Assert.Equal(1, result.NewCount);
        var item = Assert.Single(repository.GetPlaylist(config));
        Assert.NotNull(item.RawContent);
        Assert.Contains("三条地铁线路今天开通运营", item.RawContent);
        Assert.Contains("沿线居民通勤时间可减少约二十分钟", item.RawContent);
        Assert.DoesNotContain("正文 & 片段", item.RawContent);
        // 正文里出现「武汉」→ 归类也按正文重新算过
        Assert.Equal("武汉", item.Keyword);
        Assert.True(handler.CountOf("example.com/plain") >= 1, "应当请求过原文页");
    }

    [Fact]
    public async Task 正文补抓失败_条目照旧入库且不影响同轮其它条目()
    {
        using var temp = new TempDirectory();
        var config = Sa1Data.SharedConfig("武汉");
        const string articleHtml = """
            <html><body><div class="content_desc">
            <p>武汉东湖高新区发布人工智能产业三年行动方案，提出到二〇二八年底建成三个千亿级产业集群，并设立专项基金支持早期项目。</p>
            <p>方案还提到将在光谷建设两个算力中心，向本地中小企业开放普惠算力，同时推动大模型在工业质检与生物医药研发环节落地。</p>
            </div></body></html>
            """;
        var handler = new FakeHttpHandler(request =>
        {
            if (request.Url.Contains("/chat/completions", StringComparison.Ordinal))
                return FakeHttp.Ok(Sa1Data.ChatResponse((WuhanUrl, "武汉摘要"), (PlainUrl, "普通摘要")));

            if (request.Url.Contains("example.com/plain", StringComparison.Ordinal))
                return Html(articleHtml);

            if (request.Url.Contains("example.com/wuhan", StringComparison.Ordinal))
                return FakeHttp.Status(HttpStatusCode.NotFound);

            return FakeHttp.Ok(Sa1Data.Rss(
                ("武汉某路段今日起封闭施工", WuhanUrl, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1)),
                ("某行业发布年度报告", PlainUrl, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-2))),
                "application/rss+xml");
        });
        var (coordinator, repository) = Build(config, temp, handler, new ArticleContentFetcher(handler.CreateClient()));

        var result = await coordinator.FetchAsync();

        Assert.Equal(2, result.NewCount);
        var playlist = repository.GetPlaylist(config);
        Assert.Equal(2, playlist.Count);

        var failed = playlist.Single(i => i.Url == WuhanUrl);
        Assert.True(
            handler.CountOf("example.com/plain") >= 1,
            $"应当对两条原描述都补抓过；实际请求：{string.Join(" | ", handler.Recorded.Select(r => r.Url))}");
        Assert.True(
            handler.CountOf("example.com/wuhan") >= 1,
            $"应当请求过 404 那条；实际请求：{string.Join(" | ", handler.Recorded.Select(r => r.Url))}");
        Assert.False(string.IsNullOrWhiteSpace(failed.RawContent));
        Assert.DoesNotContain("人工智能产业三年行动方案", failed.RawContent);
        Assert.Equal("武汉", failed.Keyword); // 标题命中，照旧归类

        var ok = playlist.Single(i => i.Url == PlainUrl);
        Assert.Contains("人工智能产业三年行动方案", ok.RawContent);
        Assert.Equal("武汉", ok.Keyword); // 正文命中后归类
    }
}
