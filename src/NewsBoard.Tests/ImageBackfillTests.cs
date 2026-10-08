using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NewsBoard.Shared.Models;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// 图文模式补图：切到图文模式后，缓存里已有的旧条目同样要拿到配图（此前只有新条目会取图）。
/// </summary>
public class ImageBackfillTests
{
    private const string CachedUrl = "https://example.com/old-article";
    private const string CoverUrl = "https://example.com/img/cover.jpg";

    [Fact]
    public async Task 图文模式下_缓存里缺图的旧条目会在下一轮补上配图()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        await repository.LoadAsync();
        repository.AddOrUpdateRange(
        [
            new NewsItem
            {
                Id = NewsSummarizer.MakeId(CachedUrl),
                Title = "旧条目",
                Summary = "旧条目摘要。",
                Url = CachedUrl,
                Keyword = "测试",
                PublishedAt = DateTimeOffset.Now,
                Importance = 0.9,
            },
        ]);

        var handler = new BackfillHandler();
        var config = new AppConfig
        {
            ApiKey = "test-key",
            FetchMode = NewsFetchMode.WebSearch,
            Keywords = ["测试"],
            ItemsPerKeyword = 1,
            AiMinIntervalMinutes = 0,
            ImportanceThreshold = 0.5,
            PlaybackMode = PlaybackMode.TextAndImage,
        };

        var coordinator = new NewsFetchCoordinator(
            [new WebSearchNewsSource(new HttpClient(handler))],
            new NewsSummarizer(new HttpClient(handler)),
            new ImageResolver(new HttpClient(handler)),
            repository,
            new StubConfigProvider(config));

        await coordinator.FetchAsync();

        var item = Assert.Single(repository.GetPlaylist(config));
        Assert.Equal(CoverUrl, item.ImageUrl);
        Assert.Contains(handler.Requested, url => url.Contains("old-article", StringComparison.Ordinal));
        Assert.Contains(handler.Requested, url => url.EndsWith("cover.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 纯文字模式不补图()
    {
        using var temp = new TempDir();
        var repository = new NewsRepository(temp.Path);
        await repository.LoadAsync();
        repository.AddOrUpdateRange(
        [
            new NewsItem
            {
                Id = NewsSummarizer.MakeId(CachedUrl),
                Title = "旧条目",
                Summary = "旧条目摘要。",
                Url = CachedUrl,
                Keyword = "测试",
                PublishedAt = DateTimeOffset.Now,
            },
        ]);

        var handler = new BackfillHandler();
        var config = new AppConfig
        {
            ApiKey = "test-key",
            FetchMode = NewsFetchMode.WebSearch,
            Keywords = ["测试"],
            ItemsPerKeyword = 1,
            AiMinIntervalMinutes = 0,
            PlaybackMode = PlaybackMode.TextOnly,
        };

        var coordinator = new NewsFetchCoordinator(
            [new WebSearchNewsSource(new HttpClient(handler))],
            new NewsSummarizer(new HttpClient(handler)),
            new ImageResolver(new HttpClient(handler)),
            repository,
            new StubConfigProvider(config));

        await coordinator.FetchAsync();

        Assert.Null(Assert.Single(repository.GetPlaylist(config)).ImageUrl);
        Assert.DoesNotContain(handler.Requested, url => url.EndsWith("cover.jpg", StringComparison.Ordinal));
    }

    /// <summary>联网搜索返回「已缓存的那条」（于是本轮没有新条目），文章页给 og:image，图片地址返回真图片。</summary>
    private sealed class BackfillHandler : HttpMessageHandler
    {
        private readonly List<string> _requested = [];

        public IReadOnlyList<string> Requested => _requested;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            _requested.Add(url);

            if (url.Contains("/v1/messages", StringComparison.Ordinal))
            {
                var payload = $$"""
                    {"content":[{"type":"text","text":"[{\"title\":\"旧条目\",\"summary\":\"旧条目摘要重写。\",\"url\":\"{{CachedUrl}}\",\"importance\":0.9}]"}]}
                    """;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                });
            }

            if (url.EndsWith("cover.jpg", StringComparison.Ordinal))
            {
                var image = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[4096]),
                };
                image.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(image);
            }

            var html = $"""<html><head><meta property="og:image" content="{CoverUrl}" /></head><body>正文</body></html>""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
            });
        }
    }

    private sealed class StubConfigProvider(AppConfig config) : IConfigProvider
    {
        public AppConfig Current { get; } = config;

        public event Action<AppConfig>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "nb-imgbackfill-" + Guid.NewGuid().ToString("N")[..8]);

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
