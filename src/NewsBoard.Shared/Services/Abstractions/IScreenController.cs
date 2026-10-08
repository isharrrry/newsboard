namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>平台屏幕控制：常亮、黑屏（亮度 0 + 全黑画面）、全屏模式。</summary>
public interface IScreenController
{
    /// <summary>保持屏幕常亮（Android 用 KeepScreenOn；桌面为空实现）。</summary>
    Task SetKeepScreenOnAsync(bool on);

    /// <summary>进入/退出黑屏。实现方负责亮度归零与恢复。</summary>
    Task SetBlackoutAsync(bool black);

    /// <summary>沉浸式全屏 / 显示系统状态栏（Android），或无边框全屏（Windows）。</summary>
    Task SetFullscreenAsync(bool fullscreen);
}
