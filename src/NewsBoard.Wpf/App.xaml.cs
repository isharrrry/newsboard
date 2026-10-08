using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NewsBoard.Shared.Services.Abstractions;
using NewsBoard.Wpf.Services;

namespace NewsBoard.Wpf;

public partial class App : Application
{
    /// <summary>宿主 DI 容器。BlazorWebView 与平台实现都从这里取。</summary>
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var collection = new ServiceCollection();
        ConfigureServices(collection);
        Services = collection.BuildServiceProvider();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddWpfBlazorWebView();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IScreenController, WpfScreenController>();
        services.AddSingleton<ISpeechService, WpfSpeechService>();
        services.AddSingleton<ISecretStore, WpfSecretStore>();
        services.AddSingleton<WpfAutoStart>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

        // 待集成（主 Agent 合并 SA1/SA2/SA3 后在此补注册，当前工作树中这些类型尚不存在）：
        //   services.AddSingleton<ConfigStore>();                       // 并注册为 IConfigProvider
        //   services.AddSingleton<INewsRepository, NewsRepository>();
        //   services.AddSingleton<ImageResolver>();
        //   services.AddSingleton<NewsSummarizer>();
        //   services.AddSingleton<INewsSource, RssNewsSource>();
        //   services.AddSingleton<INewsSource, WebSearchNewsSource>();
        //   services.AddSingleton<NewsFetchCoordinator>();
        //   services.AddSingleton<PlaybackEngine>();
        // 另：MainWindow 当前以默认「普通带标题栏窗口」启动；集成后应读
        //   IConfigProvider.Current.WindowsBorderlessFullscreen 并调用 window.ApplyDisplayOptions(...)。
    }
}
