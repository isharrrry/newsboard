using System.Net;
using System.Text;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// 正文首图兜底：实测中新网/人民网文章页都没有 og:image，只看 meta 会让图文模式永远取不到图。
/// </summary>
public class ImageResolverFallbackTests
{
    private const string ArticleUrl = "https://www.chinanews.com.cn/sh/2026/10-08/10709034.shtml";

    [Fact]
    public async Task 有og_image时优先用og_image()
    {
        var html = """
            <html><head>
            <meta property="og:image" content="https://cdn.example.com/cover.jpg" />
            <link rel="image_src" href="https://cdn.example.com/other.jpg" />
            </head><body><div class="content"><img src="/body.jpg" /></div></body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("https://cdn.example.com/cover.jpg", image);
    }

    [Fact]
    public async Task 没有og时退到twitter_image()
    {
        var html = """
            <html><head><meta name="twitter:image" content="https://cdn.example.com/tw.jpg" /></head>
            <body><div class="content"><img src="/body.jpg" /></div></body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("https://cdn.example.com/tw.jpg", image);
    }

    [Fact]
    public async Task 没有og时退到link_image_src()
    {
        var html = """
            <html><head><link rel="image_src" href="/pic/lead.jpg" /></head>
            <body><div class="content"><img src="/body.jpg" /></div></body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("https://www.chinanews.com.cn/pic/lead.jpg", image);
    }

    [Fact]
    public async Task 都没有meta时取正文容器内首图并补成绝对地址()
    {
        var html = """
            <html><head><title>t</title></head><body>
            <div class="nav"><img src="//image.cns.com.cn/2022homepage/images/toparr.png" /></div>
            <div class="content" id="cont_1_1_2">
              <p>正文</p>
              <img src="/fileftp/2026/10-08/U123P4T47D1DT20261008134035.jpg" />
              <img src="/fileftp/2026/10-08/second.jpg" />
            </div>
            </body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("https://www.chinanews.com.cn/fileftp/2026/10-08/U123P4T47D1DT20261008134035.jpg", image);
    }

    [Fact]
    public async Task 没有正文容器时全页兜底并跳过装饰图()
    {
        var html = """
            <html><body>
            <img src="/2022homepage/logo.png" />
            <img src="/images/spacer.gif" width="1" height="1" />
            <img src="data:image/png;base64,AAAA" />
            <img src="http://politics.people.com.cn/NMediaFile/2026/1008/photo.jpg" />
            </body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("http://politics.people.com.cn/NMediaFile/2026/1008/photo.jpg", image);
    }

    [Fact]
    public async Task 全页只有装饰图时返回null()
    {
        var html = """
            <html><body>
            <img src="/2022homepage/logo.png" />
            <img src="/images/icon_share.png" />
            <img src="/x/blank.gif" />
            </body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Null(image);
    }

    [Fact]
    public async Task data_src懒加载也能取到()
    {
        var html = """
            <html><body><div class="article-content">
            <img src="/images/loading.gif" data-src="https://cdn.example.com/lazy.jpg" />
            </div></body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("https://cdn.example.com/lazy.jpg", image);
    }

    [Fact]
    public async Task 候选防盗链时跳过并取下一个可用图()
    {
        var html = """
            <html><body><div class="content" id="cont_1_1_2">
            <p>正文</p>
            <img src="https://i2.chinanews.com.cn/simg/ypt/2026/261008/blocked_zsite.jpg" />
            <img src="/fileftp/2026/10-08/real.jpg" />
            </div></body></html>
            """;

        var image = await ResolveAsync(html);

        Assert.Equal("https://www.chinanews.com.cn/fileftp/2026/10-08/real.jpg", image);
    }

    [Fact]
    public async Task 抓取失败返回null不抛异常()
    {
        var resolver = new ImageResolver(new HttpClient(new ImageFixtureHandler(null, HttpStatusCode.InternalServerError)));

        Assert.Null(await resolver.ResolveAsync(ArticleUrl));
    }

    private static Task<string?> ResolveAsync(string html)
    {
        var resolver = new ImageResolver(new HttpClient(new ImageFixtureHandler(html, HttpStatusCode.OK)));
        return resolver.ResolveAsync(ArticleUrl);
    }

    /// <summary>文章页返回 HTML；图片地址返回真实图片字节；已知防盗链域名返回 HTML（复刻中新网图床行为）。</summary>
    private sealed class ImageFixtureHandler(string? html, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var isBlockedHost = url.Contains("i2.chinanews.com.cn", StringComparison.OrdinalIgnoreCase);
            var looksLikeImage = !isBlockedHost && url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || (!isBlockedHost && url.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                || (!isBlockedHost && url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

            if (looksLikeImage)
            {
                var bytes = new byte[4096];
                var image = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                image.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(image);
            }

            var response = new HttpResponseMessage(html is null ? status : HttpStatusCode.OK);
            if (html is not null)
            {
                response.Content = new StringContent(html, Encoding.UTF8, "text/html");
            }
            else if (status != HttpStatusCode.OK)
            {
                response.StatusCode = status;
            }

            return Task.FromResult(response);
        }
    }
}
