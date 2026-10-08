using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>
/// Android 开机自启：意愿存 SharedPreferences，实际拉起由 BootReceiver + 前台服务完成。
/// 厂商自启动白名单只能引导用户手动放行（plan §3.4）。
/// </summary>
public sealed class AndroidAutoStart : IAutoStart
{
    public bool IsSupported => true;

    public Task<bool> IsEnabledAsync()
        => Task.FromResult(AutoStartHelper.IsAutoStartEnabled(Android.App.Application.Context));

    public Task SetEnabledAsync(bool enabled)
    {
        var context = Android.App.Application.Context;
        AutoStartHelper.SetAutoStartEnabled(context, enabled);

        if (enabled)
        {
            RequestNotificationPermissionIfNeeded();
            NewsBoardForegroundService.EnsureRunning(context);
        }
        else
        {
            NewsBoardForegroundService.Stop(context);
        }

        return Task.CompletedTask;
    }

    public Task<bool> OpenSystemSettingsAsync()
        => Task.FromResult(AutoStartHelper.OpenAutoStartSettings(Android.App.Application.Context));

    private static void RequestNotificationPermissionIfNeeded()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return;
        }

        if (Platform.CurrentActivity is not { } activity)
        {
            return;
        }

        if (activity.CheckSelfPermission("android.permission.POST_NOTIFICATIONS") == Android.Content.PM.Permission.Granted)
        {
            return;
        }

        AndroidX.Core.App.ActivityCompat.RequestPermissions(
            activity,
            ["android.permission.POST_NOTIFICATIONS"],
            1002);
    }
}
