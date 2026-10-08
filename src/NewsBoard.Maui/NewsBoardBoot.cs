using System.Diagnostics;
using NewsBoard.Shared.Services;

namespace NewsBoard.Maui;

/// <summary>
/// 启动期初始化：加载配置（含从安全存储取回 API Key）与新闻缓存。
/// 不加载则播放列表为空，所以 BlazorWebView 显示前先等这一步。
/// </summary>
public static class NewsBoardBoot
{
    private static readonly TaskCompletionSource<bool> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>启动初始化（幂等，重复调用只跑一次）。</summary>
    public static void Start(IServiceProvider services)
    {
        if (Gate.Task.IsCompleted)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await services.InitializeNewsBoardAsync();
                Gate.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NewsBoard] 初始化失败：{ex}");
                Gate.TrySetResult(false);
            }
        });
    }

    /// <summary>等初始化结束（最多等 timeout；超时或失败都放行，页面自己会再重试）。</summary>
    public static async Task WaitAsync(TimeSpan timeout)
    {
        try
        {
            await Gate.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("[NewsBoard] 启动初始化超时，先显示看板");
        }
    }
}
