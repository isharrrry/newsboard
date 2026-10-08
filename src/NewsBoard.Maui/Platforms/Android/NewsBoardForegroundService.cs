using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace NewsBoard.Maui;

/// <summary>
/// 常驻前台服务：让看板进程不被厂商系统随手清掉，并在通知栏留一个「点击进入看板」的入口。
/// 不做 StartActivity —— Android 10+ 后台启动 Activity 会被系统拦掉。
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public sealed class NewsBoardForegroundService : Service
{
    private const string ChannelId = "newsboard-resident";
    private const int NotificationId = 1001;

    public static void EnsureRunning(Context context)
    {
        try
        {
            var intent = new Intent(context, typeof(NewsBoardForegroundService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception)
        {
            // 系统限制（如后台启动前台服务被拒）时静默失败，不影响看板主流程
        }
    }

    public static void Stop(Context context)
    {
        try
        {
            context.StopService(new Intent(context, typeof(NewsBoardForegroundService)));
        }
        catch (Exception)
        {
            // 服务已停止
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();
        CreateChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();
        // specialUse 类型常量是 Android 14 才有的，低版本用两参重载
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        }
        else
        {
            StartForeground(NotificationId, notification);
        }

        return StartCommandResult.Sticky;
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "看板常驻", NotificationImportance.Low)
        {
            Description = "保持新闻看板进程常驻，点击通知可进入看板",
        };

        if (GetSystemService(NotificationService) is NotificationManager manager)
        {
            manager.CreateNotificationChannel(channel);
        }
    }

    private Notification BuildNotification()
    {
        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.AddFlags(ActivityFlags.SingleTop);

        var contentIntent = PendingIntent.GetActivity(
            this,
            0,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        // 绑定的链式方法返回可空，逐句调用避免解引用告警
        var builder = new NotificationCompat.Builder(this, ChannelId)!;
        builder.SetContentTitle("新闻看板");
        builder.SetContentText("点击进入看板");
        builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo);
        builder.SetContentIntent(contentIntent);
        builder.SetOngoing(true);
        builder.SetPriority(NotificationCompat.PriorityLow);

        return builder.Build()!;
    }
}
