using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>
/// Android 屏幕控制。黑屏 = 窗口亮度置 0 + 画面上全黑（免权限），
/// 全程不改 KeepScreenOn：屏幕物理上一直亮，系统才不会锁屏（plan §5.1）。
/// </summary>
public sealed class AndroidScreenController : IScreenController
{
    private readonly object _gate = new();
    private float? _savedBrightness;

    public Task SetKeepScreenOnAsync(bool on) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        DeviceDisplay.Current.KeepScreenOn = on;
    });

    public Task SetBlackoutAsync(bool black) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (Platform.CurrentActivity?.Window is not { } window)
        {
            return;
        }

        lock (_gate)
        {
            var attributes = window.Attributes;
            if (attributes is null)
            {
                return;
            }

            if (black)
            {
                _savedBrightness ??= attributes.ScreenBrightness;
                attributes.ScreenBrightness = 0f;
            }
            else
            {
                attributes.ScreenBrightness = _savedBrightness ?? WindowManagerLayoutParams.BrightnessOverrideNone;
                _savedBrightness = null;
            }

            window.Attributes = attributes;
        }
    });

    public Task SetFullscreenAsync(bool fullscreen) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (Platform.CurrentActivity?.Window is not { } window)
        {
            return;
        }

        if (WindowCompat.GetInsetsController(window, window.DecorView) is not { } controller)
        {
            return;
        }

        if (fullscreen)
        {
            controller.Hide(WindowInsetsCompat.Type.StatusBars());
            controller.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
        }
        else
        {
            controller.Show(WindowInsetsCompat.Type.StatusBars());
        }
    });
}
