using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NewsBoard.Shared.Services;
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

        // 先出窗口（默认普通带标题栏），配置加载完成后再按配置应用显示模式
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        // 配置与新闻缓存必须加载，否则播放列表为空；但不能阻塞 UI 线程（否则白屏）
        _ = InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddWpfBlazorWebView();

        // 平台实现先注册（AddNewsBoardCore 依赖这些接口）
        services.AddSingleton<ISecretStore, WpfSecretStore>();
        services.AddSingleton<IScreenController, WpfScreenController>();
        services.AddSingleton<ISpeechService, WpfSpeechService>();
        services.AddSingleton<IAutoStart, WpfAutoStart>();
        services.AddSingleton<ILinkOpener, WpfLinkOpener>();

        services.AddNewsBoardCore();
    }

    private static async Task InitializeAsync()
    {
        try
        {
            await Services.InitializeNewsBoardAsync();

            var config = Services.GetRequiredService<IConfigProvider>().Current;
            await Services.GetRequiredService<IScreenController>()
                .SetFullscreenAsync(config.WindowsBorderlessFullscreen);
        }
        catch (Exception ex)
        {
            // 启动期失败不应让看板起不来：保住窗口，把原因写到调试输出
            Debug.WriteLine($"[NewsBoard] 启动初始化失败：{ex}");
        }
    }
}
