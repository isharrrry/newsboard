using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NewsBoard.Shared.Services;
using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// 正文抓取：RSS 只给几十字 description、联网搜索通道拿不到正文，靠它补 RawContent。
/// 全部用例用内嵌 HTML，离线可跑。
/// </summary>
public class ArticleContentFetcherTests
{
    private const string ArticleUrl = "https://www.chinanews.com.cn/sh/2026/10-08/10709034.shtml";

    [Fact]
    public void 中新网风格_content_desc容器命中且剔除脚本与导航()
    {
        var html = """
            <html><head><title>标题</title>
            <script>var tracker = "should-not-appear";</script>
            <style>.a { color: red; }</style>
            </head><body>
            <nav><a href="/">首页</a><a href="/china">国内</a></nav>
            <header><h1>站点页头文字</h1></header>
            <div class="content_desc">
              <p>武汉光谷一家企业发布新一代存储控制器，单盘顺序读取速度达到每秒十四点五吉字节，功耗较上一代下降约三成。</p>
              <p>该公司称新产品已进入量产阶段，首批面向国内数据中心客户交付，年内计划出货十万片，并在两个季度内完成产线扩产。</p>
              <p>责任编辑：张三</p>
            </div>
            <footer>版权所有 未经许可不得转载</footer>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("武汉光谷一家企业发布新一代存储控制器", text);
        Assert.Contains("年内计划出货十万片", text);
        Assert.DoesNotContain("should-not-appear", text);
        Assert.DoesNotContain("首页", text);
        Assert.DoesNotContain("站点页头文字", text);
        Assert.DoesNotContain("责任编辑", text);
        Assert.DoesNotContain("版权所有", text);
    }

    [Fact]
    public void 人民网风格_cont_1_1_2容器命中()
    {
        var html = """
            <html><body>
            <div class="nav"><a href="/">人民网首页</a></div>
            <div id="cont_1_1_2">
              <p>国务院常务会议审议通过关于深入实施人工智能加行动的意见，要求推动人工智能与制造业、服务业深度融合。</p>
              <p>会议还提出要加快算力基础设施建设，支持企业开展行业大模型应用，并在年内出台配套的算力调度与数据流通细则。</p>
            </div>
            <div class="foot">人民日报社概况</div>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("国务院常务会议审议通过", text);
        Assert.Contains("算力调度与数据流通细则", text);
        Assert.DoesNotContain("人民网首页", text);
        Assert.DoesNotContain("人民日报社概况", text);
    }

    [Fact]
    public void IT之家风格_post_content容器命中()
    {
        var html = """
            <html><body>
            <aside><p>广告位招租，联系方式见页面底部，长期有效，欢迎垂询合作事宜与投放价格。</p></aside>
            <div class="post_content">
              <p>AMD 宣布以约八十二亿美元收购李飞飞创办的 World Labs，交易预计在明年上半年完成，仍需通过监管审批。</p>
              <p>World Labs 专注于空间智能，其模型可以从单张图片重建三维场景，团队将并入 AMD 的 AI 事业部继续研发。</p>
            </div>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("收购李飞飞创办的 World Labs", text);
        Assert.Contains("空间智能", text);
        Assert.DoesNotContain("广告位招租", text);
    }

    [Fact]
    public void 没有已知容器时用零散段落启发式并丢掉过短段落()
    {
        var html = """
            <html><body>
            <p>短句</p>
            <p>沪苏浙皖四地联合发布长三角一体化年度报告，数据显示跨省通办事项累计办件量突破两千万件，群众平均少跑一点八次。</p>
            <p>报告还提到，区域内高铁运营里程已达七千三百公里，相邻城市间通勤时间较五年前缩短约四分之一。</p>
            <p>下一步四地将统一数据共享标准，把异地就医结算与公积金互认纳入新一轮清单。</p>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("长三角一体化年度报告", text);
        Assert.Contains("公积金互认纳入新一轮清单", text);
        Assert.DoesNotContain("短句", text);
    }

    [Fact]
    public void 只有导航与页脚时返回null()
    {
        var html = """
            <html><body>
            <nav><a href="/">首页</a><a href="/sports">体育</a><a href="/tech">科技</a></nav>
            <footer><p>关于我们</p><p>联系方式</p><p>版权声明</p></footer>
            </body></html>
            """;

        Assert.Null(ArticleContentFetcher.ExtractMainText(html));
    }

    [Fact]
    public void 超长正文按maxChars截断()
    {
        var paragraph = string.Concat(Enumerable.Repeat("这是一段用于验证截断行为的长正文，句子需要足够长才能超过上限。", 8));
        var html = $"""<html><body><div class="post_content"><p>{paragraph}</p></div></body></html>""";

        var text = ArticleContentFetcher.ExtractMainText(html, maxChars: 100);

        Assert.NotNull(text);
        Assert.Equal(100, text!.Length);
        Assert.StartsWith("这是一段用于验证截断行为的长正文", text);
    }

    [Fact]
    public void 空串与纯脚本返回null()
    {
        Assert.Null(ArticleContentFetcher.ExtractMainText(""));
        Assert.Null(ArticleContentFetcher.ExtractMainText("   "));
        Assert.Null(ArticleContentFetcher.ExtractMainText("<html><body><script>var a = 1;</script></body></html>"));
        Assert.Null(ArticleContentFetcher.ExtractMainText(null!));
    }

    [Fact]
    public void 实体解码与空白压缩生效()
    {
        var nbsp = "&nbsp;&nbsp;";
        var html = $"""
            <html><body><div class="post_content">
            <p>第一段{nbsp}带&nbsp;间隔&#39;引号&#39;以及全角{'\u3000'}空白，用来验证实体解码与空白压缩是否都生效，长度也要超过六十个字才算有效正文，所以这里继续补足一些文字。</p>
            </div></body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.StartsWith("第一段 带 间隔'引号'以及全角 空白", text);
        Assert.DoesNotContain('\u3000', text);
        Assert.DoesNotContain('\u00A0', text);
    }

    [Fact]
    public void 容器内没有p标签时退化为整块文本()
    {
        var html = """
            <html><body>
            <div class="content_desc">
              第一块文字交代事件发生的时间地点与主要参与方，信息来自现场通报与公开材料，其中包含若干具体数字。<br>
              第二块文字补充后续处置安排，相关部门表示将在下一阶段公布详细进展，并回应社会关切的问题。
            </div>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("第一块文字交代事件发生的时间地点", text);
        Assert.Contains("相关部门表示将在下一阶段公布详细进展", text);
    }

    [Fact]
    public void 人民网风格_rm_txt_con容器命中且页脚不进正文()
    {
        var html = """
            <html><body>
            <div class="layout rm_txt cf">
              <div class="rm_txt_con cf">
                <p>生态环境是关系党的使命宗旨的重大政治问题，也是关系民生的重大社会问题，必须站在永续发展的高度来认识。</p>
                <p>今天是世界环境日，各地围绕生态保护开展主题活动，并公布了新一轮的监测数据与治理安排。</p>
                <p>责任编辑：常红</p>
              </div>
            </div>
            <div class="foot">
              <p>人民日报社概况 | 关于人民网 | 报社招聘 | 广告服务 | 合作加盟</p>
              <p>互联网新闻信息服务许可证10120170001 | 京ICP备00000000号</p>
            </div>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("生态环境是关系党的使命宗旨", text);
        Assert.Contains("新一轮的监测数据与治理安排", text);
        Assert.DoesNotContain("责任编辑", text);
        Assert.DoesNotContain("关于人民网", text);
        Assert.DoesNotContain("京ICP备", text);
    }

    [Fact]
    public void 尾部噪声行被裁掉而正文保留()
    {
        var html = """
            <html><body>
            <p>长三角铁路国庆假期安全发送旅客逾两千七百七十万人次，日均超过三百九十五万人次，创下同期新高，其中假期首日与返程日客流最为集中。</p>
            <p>铁路部门加开夜间高铁与重联动车组，重点补充沪宁、沪杭与合宁方向的运力，返程高峰持续到假期最后一天，并延长了车站的进出站引导时间。</p>
            <p>分享</p>
            <p>顶部</p>
            <p>版权所有 © 2026 某某网</p>
            <p>Copyright RuanMei.com, All Rights Reserved.</p>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("长三角铁路国庆假期安全发送旅客", text);
        Assert.Contains("返程高峰持续到假期最后一天", text);
        Assert.DoesNotContain("版权所有", text);
        Assert.DoesNotContain("All Rights Reserved", text);
        Assert.DoesNotContain("顶部", text);
        Assert.DoesNotContain("分享", text);
    }

    [Fact]
    public void 正文结束标记之后的推荐位被裁掉()
    {
        var html = """
            <html><body>
            <div class="content_desc">
            <p>中新网上海十月八日电 记者八日从上海机场集团了解到，今年国庆假期浦东与虹桥两大机场共保障航班起降一万四千架次，旅客吞吐量同比增长百分之六。</p>
            <p>机场方面表示，返程高峰集中在假期最后两天，两大机场加开值机柜台并延长地铁与公交的接驳运营时间，确保旅客顺利返程。(完)</p>
            <p>60岁当“北漂”，演了一辈子老太太，她是最让人想念的“牛大妈”</p>
            <p>某地秋季车展现场人气旺，多款新车集中发布并公布预售价格</p>
            </div>
            </body></html>
            """;

        var text = ArticleContentFetcher.ExtractMainText(html);

        Assert.NotNull(text);
        Assert.Contains("确保旅客顺利返程。(完)", text);
        Assert.DoesNotContain("牛大妈", text);
        Assert.DoesNotContain("秋季车展", text);
    }

    [Fact]
    public async Task FetchAsync_成功路径返回正文()
    {
        var html = """
            <html><body><div class="post_content">
            <p>国家移民管理局消息，国庆假期全国边检机关保障一千五百四十六点二万人次出入境，日均二百二十点九万人次，较去年同期增长百分之六点七。</p>
            </div></body></html>
            """;
        var handler = new ArticleFixtureHandler(Encoding.UTF8.GetBytes(html), HttpStatusCode.OK, "text/html; charset=utf-8");
        var fetcher = new ArticleContentFetcher(new HttpClient(handler));

        var text = await fetcher.FetchAsync(ArticleUrl, CancellationToken.None);

        Assert.NotNull(text);
        Assert.Contains("国家移民管理局消息", text);
        Assert.True(handler.UserAgentSent, "应当带上浏览器 UA");
    }

    [Fact]
    public async Task FetchAsync_失败与空响应一律返回null不抛异常()
    {
        var server = new ArticleFixtureHandler(Encoding.UTF8.GetBytes("<html><body>x</body></html>"), HttpStatusCode.InternalServerError, "text/html");
        var empty = new ArticleFixtureHandler([], HttpStatusCode.OK, "text/html");
        var throwing = new ArticleThrowingHandler();

        Assert.Null(await new ArticleContentFetcher(new HttpClient(server)).FetchAsync(ArticleUrl, CancellationToken.None));
        Assert.Null(await new ArticleContentFetcher(new HttpClient(empty)).FetchAsync(ArticleUrl, CancellationToken.None));
        Assert.Null(await new ArticleContentFetcher(new HttpClient(throwing)).FetchAsync(ArticleUrl, CancellationToken.None));
        Assert.Null(await new ArticleContentFetcher(new HttpClient(server)).FetchAsync("", CancellationToken.None));
        Assert.Null(await new ArticleContentFetcher(new HttpClient(server)).FetchAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task FetchAsync_声明gb2312时也不抛异常()
    {
        var html = """
            <html><body><div class="content_desc">
            <p>这条正文用来验证编码兜底：当响应头声明了系统里可能不存在的字符集时，抓取过程不允许抛异常，最差也要按 UTF-8 兜底把内容解出来，并且正文长度必须超过阈值才判定为有效正文，所以这里继续补足一些文字以确保长度足够。</p>
            </div></body></html>
            """;
        var handler = new ArticleFixtureHandler(Encoding.UTF8.GetBytes(html), HttpStatusCode.OK, "text/html; charset=gb2312");
        var fetcher = new ArticleContentFetcher(new HttpClient(handler));

        var text = await fetcher.FetchAsync(ArticleUrl, CancellationToken.None);

        Assert.NotNull(text);
    }

    private sealed class ArticleFixtureHandler(byte[] body, HttpStatusCode status, string? contentType) : HttpMessageHandler
    {
        public bool UserAgentSent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UserAgentSent = request.Headers.UserAgent.Count > 0;

            var response = new HttpResponseMessage(status);
            if (body.Length > 0)
            {
                response.Content = new ByteArrayContent(body);
                if (contentType is not null)
                {
                    response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                }
            }

            return Task.FromResult(response);
        }
    }

    private sealed class ArticleThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("模拟网络故障");
    }
}
