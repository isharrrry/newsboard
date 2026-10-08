namespace NewsBoard.Shared.Services;

/// <summary>显示器电源动作。</summary>
public enum MonitorPowerAction
{
    /// <summary>什么都不用发（幂等命中，或安全护栏不允许）。</summary>
    None,

    /// <summary>真的关显示器（背光熄灭）。</summary>
    Off,

    /// <summary>把显示器打开。</summary>
    On
}

/// <summary>
/// 「要不要真的关显示器」的判定 + 幂等状态（纯逻辑，无平台依赖，可单测）。
///
/// 安全护栏：只有在**本应用窗口是前台窗口**或**窗口已是无边框全屏**时才真的关屏；
/// 否则只保留画面黑遮罩（用户在别的程序里干活时，屏幕不会被黑掉）。
/// </summary>
public sealed class MonitorPowerPolicy
{
    private readonly object _gate = new();
    private bool _off;

    /// <summary>当前是否处于「已请求关屏」状态。</summary>
    public bool IsOff
    {
        get
        {
            lock (_gate)
            {
                return _off;
            }
        }
    }

    /// <summary>安全护栏：前台窗口 或 无边框全屏，二者之一成立才允许关屏。</summary>
    public static bool CanCutMonitor(bool appIsForeground, bool borderlessFullscreen)
        => appIsForeground || borderlessFullscreen;

    /// <summary>
    /// 本次要执行的显示器动作。幂等：重复进入黑屏不会重复关屏，没关过时也不会白发开屏指令。
    /// </summary>
    public MonitorPowerAction Next(bool black, bool appIsForeground, bool borderlessFullscreen)
    {
        lock (_gate)
        {
            if (black)
            {
                if (_off || !CanCutMonitor(appIsForeground, borderlessFullscreen))
                {
                    return MonitorPowerAction.None;
                }

                _off = true;
                return MonitorPowerAction.Off;
            }

            if (!_off)
            {
                return MonitorPowerAction.None;
            }

            _off = false;
            return MonitorPowerAction.On;
        }
    }
}
