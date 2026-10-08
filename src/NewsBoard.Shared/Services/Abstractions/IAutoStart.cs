namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>
/// 平台开机自启。Windows 写 HKCU Run 键；Android 走 BootReceiver + 前台服务，
/// 且需引导用户在厂商自启动白名单里放行（见 plan §3.4）。
/// </summary>
public interface IAutoStart
{
    /// <summary>平台是否支持开机自启。</summary>
    bool IsSupported { get; }

    /// <summary>当前是否已启用。</summary>
    Task<bool> IsEnabledAsync();

    /// <summary>启用/关闭开机自启。</summary>
    Task SetEnabledAsync(bool enabled);

    /// <summary>跳到系统自启动（或电池白名单）设置页，交给用户手动放行；不支持时返回 false。</summary>
    Task<bool> OpenSystemSettingsAsync();
}
