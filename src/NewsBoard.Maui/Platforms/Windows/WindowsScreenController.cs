using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>Windows 桌面：不做亮熄与常亮（桌面无此需求），全屏交给 MAUI Window 处理。</summary>
public sealed class WindowsScreenController : IScreenController
{
    public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

    public Task SetBlackoutAsync(bool black) => Task.CompletedTask;

    public Task SetFullscreenAsync(bool fullscreen) => Task.CompletedTask;
}
