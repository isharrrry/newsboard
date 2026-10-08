using Microsoft.Extensions.DependencyInjection;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>
/// Shared 侧业务服务的统一装配入口。宿主只需先注册平台实现
/// （<see cref="ISecretStore"/>、<see cref="IScreenController"/>、<see cref="ISpeechService"/>、<see cref="IAutoStart"/>），
/// 再调用 <see cref="AddNewsBoardCore"/>。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNewsBoardCore(this IServiceCollection services)
    {
        // 三个联网实现各自用内部 CTS 计时（15s / 90s / 3s），HttpClient 超时必须让开。
        services.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

        services.AddSingleton<IClock, SystemClock>();

        services.AddSingleton(sp => new NewsRepository());
        services.AddSingleton<INewsRepository>(sp => sp.GetRequiredService<NewsRepository>());

        services.AddSingleton(sp => new ConfigStore(sp.GetRequiredService<ISecretStore>()));
        services.AddSingleton<IConfigProvider>(sp => sp.GetRequiredService<ConfigStore>());

        services.AddSingleton<NewsSummarizer>();
        services.AddSingleton<ImageResolver>();

        services.AddSingleton<INewsSource, RssNewsSource>();
        services.AddSingleton<INewsSource, WebSearchNewsSource>();

        services.AddSingleton<NewsFetchCoordinator>();
        services.AddSingleton<NewsFetchScheduler>();
        services.AddSingleton<PlaybackEngine>();

        return services;
    }

    /// <summary>
    /// 启动期一次性加载：配置（含从 <see cref="ISecretStore"/> 取回 API Key）与新闻缓存，
    /// 然后拉起定时抓取调度（启动即抓一次，之后按「抓取间隔」循环）。
    /// 不加载则播放列表为空。
    /// </summary>
    public static async Task InitializeNewsBoardAsync(this IServiceProvider services)
    {
        await services.GetRequiredService<ConfigStore>().LoadAsync();
        await services.GetRequiredService<NewsRepository>().LoadAsync();
        await services.GetRequiredService<NewsFetchScheduler>().StartAsync();
    }
}
