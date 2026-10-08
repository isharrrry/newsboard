using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int NotificationPermissionRequestCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        ApplyHostPreferences();
        RequestNotificationPermissionIfNeeded();

        // 看板是常驻应用：前台服务让进程在后台不被随手清掉，同时提供通知入口
        if (AutoStartHelper.IsAutoStartEnabled(this))
        {
            NewsBoardForegroundService.EnsureRunning(this);
        }
    }

    /// <summary>按配置决定默认显示系统状态栏还是沉浸式全屏（plan §4.4）。</summary>
    private void ApplyHostPreferences()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null)
        {
            return;
        }

        var showStatusBar = services.GetService<IConfigProvider>()?.Current.AndroidShowStatusBar ?? true;
        if (services.GetService<IScreenController>() is { } screen)
        {
            _ = screen.SetFullscreenAsync(!showStatusBar);
        }
    }

    private void RequestNotificationPermissionIfNeeded()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        if (CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == Permission.Granted)
        {
            return;
        }

        ActivityCompat.RequestPermissions(this, ["android.permission.POST_NOTIFICATIONS"], NotificationPermissionRequestCode);
    }
}
