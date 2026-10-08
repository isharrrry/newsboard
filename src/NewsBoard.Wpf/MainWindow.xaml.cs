using System.Windows;
using System.Windows.Input;

namespace NewsBoard.Wpf;

public partial class MainWindow : Window
{
    private bool _borderlessFullscreen;

    public MainWindow()
    {
        InitializeComponent();

        // BlazorWebView 必须在启动前拿到组件层的服务容器
        Blazor.Services = App.Services;

        // 默认普通带标题栏窗口（plan §4.4：Windows 无边框全屏默认 false）
        ApplyDisplayOptions(false);
    }

    /// <summary>
    /// 应用宿主显示模式：true = 无边框全屏，false = 普通带标题栏窗口。
    /// 无边框全屏下按 Esc 或 F11 退出（无标题栏时仍需一个退出手段）。
    /// </summary>
    public void ApplyDisplayOptions(bool borderlessFullscreen)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ApplyDisplayOptions(borderlessFullscreen));
            return;
        }

        _borderlessFullscreen = borderlessFullscreen;

        if (borderlessFullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _borderlessFullscreen:
                ApplyDisplayOptions(false);
                e.Handled = true;
                break;
            case Key.F11:
                ApplyDisplayOptions(!_borderlessFullscreen);
                e.Handled = true;
                break;
        }
    }
}
