using Android.App;
using Android.Content;

namespace NewsBoard.Maui;

/// <summary>
/// 开机自启入口（plan §3.4）。Android 10+ 禁止后台直接 StartActivity，
/// 因此这里只拉起前台服务 + 常驻通知，由用户点通知进入看板。
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, "android.intent.action.QUICKBOOT_POWERON" })]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null)
        {
            return;
        }

        if (!AutoStartHelper.IsAutoStartEnabled(context))
        {
            return;
        }

        NewsBoardForegroundService.EnsureRunning(context);
    }
}
