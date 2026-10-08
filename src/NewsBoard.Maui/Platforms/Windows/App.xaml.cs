using Microsoft.Maui;
using Microsoft.UI.Xaml;

namespace NewsBoard.Maui.WinUI;

/// <summary>WinUI 3 引导入口（MAUI 单项目的 Windows 头）。</summary>
public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
