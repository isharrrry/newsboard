using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// SA5：MAUI 宿主页（src/NewsBoard.Maui/wwwroot/index.html）的启动契约。
///
/// MAUI 的 BlazorWebView 会自己启动 Blazor 并注册 XAML 里声明的根组件
/// （MainPage.xaml 的 Selector="#app"），所以宿主页里的脚本**不能抢在框架之前**启动：
/// 抢跑会用「没有根组件」的方式把 Blazor 启动起来，框架随后带根组件的启动被拒
/// （"Blazor has already started."），结果进程活着、WebView 在、#app 一片空白
/// （2026-10-08 Android 真机实测）。宿主页只能观察框架是否渲染，渲染不出来才兜底。
/// </summary>
public sealed class Sa5MauiHostPageTests
{
    [Fact]
    public void MAUI宿主页_不抢在框架之前启动Blazor()
    {
        var html = StripLineComments(File.ReadAllText(MauiIndexHtmlPath()));

        Assert.Contains("autostart=\"false\"", html);

        var watchIndex = html.IndexOf("setInterval", StringComparison.Ordinal);
        var startIndex = html.IndexOf("Blazor.start()", StringComparison.Ordinal);

        Assert.True(watchIndex >= 0, "宿主页应当用定时观察等待框架启动");
        Assert.True(startIndex > watchIndex, "Blazor.start() 必须在定时器回调里（延迟兜底），不能立即执行");
    }

    [Fact]
    public void MAUI宿主页_保留可见的启动提示()
    {
        var html = File.ReadAllText(MauiIndexHtmlPath());

        Assert.Contains("id=\"boot-hint\"", html);
        Assert.Contains("正在启动", html);
    }

    private static string StripLineComments(string html) =>
        string.Join(
            "\n",
            html.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MauiIndexHtmlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NewsBoard.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "NewsBoard.Maui", "wwwroot", "index.html");
    }
}
