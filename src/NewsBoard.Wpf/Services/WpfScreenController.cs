using System.Runtime.InteropServices;
using System.Windows;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Wpf.Services;

/// <summary>
/// 桌面屏幕控制：不做常亮（桌面无此需求），承载窗口显示模式（普通窗口 / 无边框全屏），
/// 并在黑屏时**真正关掉显示器背光**（Win32 广播 <c>WM_SYSCOMMAND / SC_MONITORPOWER</c>）。
/// 安全护栏见 <see cref="MonitorPowerPolicy"/>：只在应用窗口前台或无边框全屏时才关屏。
/// </summary>
public sealed class WpfScreenController : IScreenController
{
    private readonly MonitorPowerPolicy _monitor = new();

    public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

    public Task SetBlackoutAsync(bool black)
    {
        // 前台判定与全屏判定都要在 UI 线程上读窗口状态
        var (isForeground, isFullscreen) = ReadWindowState();
        var action = _monitor.Next(black, isForeground, isFullscreen);

        if (action != MonitorPowerAction.None)
        {
            // 广播可能被挂起的窗口拖住，放到线程池上跑，别卡住播放循环
            var off = action == MonitorPowerAction.Off;
            _ = Task.Run(() => MonitorPower.TrySet(off));
        }

        return Task.CompletedTask;
    }

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

    /// <summary>读当前窗口的（是否前台, 是否无边框全屏）。窗口还没建好/正在关闭时按「不安全」处理。</summary>
    private static (bool IsForeground, bool IsFullscreen) ReadWindowState()
    {
        var app = Application.Current;
        if (app is null)
        {
            return (false, false);
        }

        try
        {
            return app.Dispatcher.CheckAccess()
                ? ReadCore(app)
                : app.Dispatcher.Invoke(() => ReadCore(app));
        }
        catch (Exception)
        {
            // 分发器已在关闭等：按「不安全」处理，绝不因为读窗口状态把异常抛进播放循环
            return (false, false);
        }
    }

    private static (bool IsForeground, bool IsFullscreen) ReadCore(Application app)
    {
        if (app.MainWindow is not { } window)
        {
            return (false, false);
        }

        var fullscreen = window.WindowStyle == WindowStyle.None && window.WindowState == WindowState.Maximized;
        return (window.IsActive, fullscreen);
    }
}

/// <summary>Win32 显示器电源控制（<c>SC_MONITORPOWER</c>：2=关闭，-1=打开）。</summary>
internal static class MonitorPower
{
    private const int WmSysCommand = 0x0112;
    private const int ScMonitorPower = 0xF170;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>关（true）或开（false）显示器。任何失败都只当没做成，不抛异常。</summary>
    public static bool TrySet(bool off)
    {
        try
        {
            var lParam = off ? new IntPtr(2) : new IntPtr(-1);
            SendMessageTimeout(
                HwndBroadcast, WmSysCommand, new IntPtr(ScMonitorPower), lParam, SmtoAbortIfHung, 1000, out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
