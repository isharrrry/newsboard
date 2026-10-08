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
        builder.Services.AddSingleton<IScreenController, AndroidScreenController>();
        builder.Services.AddSingleton<ISpeechService, AndroidSpeechService>();
        builder.Services.AddSingleton<ISecretStore, MauiSecretStore>();
        builder.Services.AddSingleton<IAutoStart, AndroidAutoStart>();

        builder.Services.AddNewsBoardCore();

        return builder.Build();
    }
}
