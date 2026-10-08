using Android.Content;

namespace NewsBoard.Maui;

/// <summary>
/// 开机自启相关的平台状态与引导（plan §3.4）。
/// 开关状态存平台 SharedPreferences，默认 true（与 §4.4 默认值一致）。
/// </summary>
public static class AutoStartHelper
{
    private const string PreferencesName = "newsboard";
    private const string AutoStartKey = "auto_start";

    // 厂商自启动管理页（小米 / 华为 / OPPO / vivo / 三星 / 乐视），逐个尝试
    private static readonly string[][] VendorAutoStartPages =
    [
        ["com.miui.securitycenter", "com.miui.permcenter.autostart.AutoStartManagementActivity"],
        ["com.huawei.systemmanager", "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity"],
        ["com.huawei.systemmanager", "com.huawei.systemmanager.appcontrol.activity.StartupAppControlActivity"],
        ["com.coloros.safecenter", "com.coloros.safecenter.permission.startup.StartupAppListActivity"],
        ["com.oppo.safe", "com.oppo.safe.permission.startup.StartupAppListActivity"],
        ["com.vivo.permissionmanager", "com.vivo.permissionmanager.activity.BgStartUpManagerActivity"],
        ["com.samsung.android.lool", "com.samsung.android.sm.ui.battery.BatteryActivity"],
        ["com.letv.android.letvsafe", "com.letv.android.letvsafe.AutobootManageActivity"],
    ];

    public static bool IsAutoStartEnabled(Context context)
        => GetPreferences(context)?.GetBoolean(AutoStartKey, true) ?? true;

    /// <summary>供设置页在保存「开机自启」开关时调用（集成时接线）。</summary>
    public static void SetAutoStartEnabled(Context context, bool enabled)
        => GetPreferences(context)?.Edit()?.PutBoolean(AutoStartKey, enabled)?.Apply();

    /// <summary>跳到厂商自启动白名单设置页；都不存在时退回应用详情页。</summary>
    public static bool OpenAutoStartSettings(Context context)
    {
        foreach (var page in VendorAutoStartPages)
        {
            try
            {
                var intent = new Intent();
                intent.SetComponent(new ComponentName(page[0], page[1]));
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
                return true;
            }
            catch (Exception)
            {
                // 该厂商页面不存在，继续试下一个
            }
        }

        try
        {
            var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings);
            intent.SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ISharedPreferences? GetPreferences(Context context)
        => context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
}
