using Microsoft.Extensions.Logging;
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
        builder.Logging.AddDebug();
#endif

        // 采集与 AI 调用共用一个 HttpClient（超时按最长的一次联网搜索请求给足）
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(120) });

        builder.Services.AddSingleton<IScreenController, AndroidScreenController>();
        builder.Services.AddSingleton<ISpeechService, AndroidSpeechService>();
        builder.Services.AddSingleton<ISecretStore, MauiSecretStore>();

        // 待集成（这些类型由 Shared 侧并行开发，集成时在此补注册）：
        //   IConfigProvider / ConfigStore
        //   INewsRepository / NewsRepository
        //   INewsSource / RssNewsSource / WebSearchNewsSource
        //   NewsSummarizer / ImageResolver / NewsFetchCoordinator / PlaybackEngine

        return builder.Build();
    }
}
