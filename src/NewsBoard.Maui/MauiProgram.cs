using Microsoft.Extensions.Logging;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { });

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        // 平台实现先注册，再交给 Shared 的统一装配入口（含 HttpClient / 配置 / 缓存 / 采集 / 提炼）
#if ANDROID
        builder.Services.AddSingleton<IScreenController, AndroidScreenController>();
        builder.Services.AddSingleton<ISpeechService, AndroidSpeechService>();
        builder.Services.AddSingleton<ISecretStore, MauiSecretStore>();
        builder.Services.AddSingleton<IAutoStart, AndroidAutoStart>();
        builder.Services.AddSingleton<ILinkOpener, AndroidLinkOpener>();
#elif WINDOWS
        builder.Services.AddSingleton<IScreenController, WindowsScreenController>();
        builder.Services.AddSingleton<ISpeechService, WindowsSpeechService>();
        builder.Services.AddSingleton<ISecretStore, WindowsSecretStore>();
        builder.Services.AddSingleton<IAutoStart, WindowsAutoStart>();
        builder.Services.AddSingleton<ILinkOpener, WindowsLinkOpener>();
#endif

        builder.Services.AddNewsBoardCore();

        var app = builder.Build();

        // 启动期初始化：加载配置（含从 ISecretStore 取回 API Key）与新闻缓存，并拉起定时抓取调度。
        // 不 await（MAUI 的 CreateMauiApp 是同步的），失败只记日志，不让 App 起不来。
        _ = InitializeAsync(app.Services);

        return app;
    }

    private static async Task InitializeAsync(IServiceProvider services)
    {
        try
        {
            await services.InitializeNewsBoardAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NewsBoard] 启动初始化失败：{ex}");
        }
    }
}
