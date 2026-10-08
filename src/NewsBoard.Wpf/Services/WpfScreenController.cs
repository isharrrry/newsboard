using System.Windows;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Wpf.Services;

/// <summary>
/// 桌面屏幕控制：不做常亮与亮熄循环（plan §1：Windows 不做那套，桌面无此需求），
/// 只承载窗口显示模式（普通窗口 / 无边框全屏）。
/// </summary>
public sealed class WpfScreenController : IScreenController
{
    public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

    public Task SetBlackoutAsync(bool black) => Task.CompletedTask;

    public Task SetFullscreenAsync(bool fullscreen)
    {
        var app = Application.Current;
        if (app is null)
        {
            return Task.CompletedTask;
        }

        return app.Dispatcher.InvokeAsync(() =>
        {
            if (app.MainWindow is MainWindow window)
            {
                window.ApplyDisplayOptions(fullscreen);
            }
        }).Task;
    }
}
