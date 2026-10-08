using System.Runtime.InteropServices;
using Microsoft.Maui.Controls;
using Microsoft.UI.Xaml;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using WinRT.Interop;

namespace NewsBoard.Maui;

/// <summary>
/// Windows 桌面：不做常亮与亮度（桌面无此需求），黑屏时**真正关掉显示器背光**
/// （Win32 广播 <c>WM_SYSCOMMAND / SC_MONITORPOWER</c>）。
/// 安全护栏见 <see cref="MonitorPowerPolicy"/>：只在应用窗口是前台时才关屏 —— 用户切走去做别的事，
/// 看板即使进入黑屏阶段也只显示黑遮罩，不会把屏幕黑掉。
/// </summary>
public sealed class WindowsScreenController : IScreenController
{
    private readonly MonitorPowerPolicy _monitor = new();

    public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

    public Task SetBlackoutAsync(bool black)
    {
        var hwnd = TryGetWindowHandle();
        var isForeground = hwnd != IntPtr.Zero && MonitorPower.IsForeground(hwnd);

        // MAUI 的 Windows 头目前不做无边框全屏（窗口由系统标题栏托管），故全屏条件恒为 false
        var action = _monitor.Next(black, isForeground, borderlessFullscreen: false);

        if (action != MonitorPowerAction.None)
        {
            var off = action == MonitorPowerAction.Off;
            _ = Task.Run(() => MonitorPower.TrySet(off));
        }

        return Task.CompletedTask;
    }

    public Task SetFullscreenAsync(bool fullscreen) => Task.CompletedTask;

    private static IntPtr TryGetWindowHandle()
    {
        try
        {
            var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window platform)
            {
                return WindowNative.GetWindowHandle(platform);
            }
        }
        catch (Exception)
        {
            // 窗口还没建好：按「不安全」处理（不关屏）
        }

        return IntPtr.Zero;
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>本应用窗口是否就是前台窗口。</summary>
    public static bool IsForeground(IntPtr ownWindow)
        => ownWindow != IntPtr.Zero && GetForegroundWindow() == ownWindow;

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
