using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Wpf;

public partial class MainWindow : Window
{
    private const int WmKeyDown = 0x0100;

    private bool _borderlessFullscreen;
    private bool _applied;
    private readonly ConfigStore? _configStore;

    public MainWindow()
    {
        InitializeComponent();

        // BlazorWebView 必须在启动前拿到组件层的服务容器
        Blazor.Services = App.Services;

        // 默认普通带标题栏窗口（plan §4.4：Windows 无边框全屏默认 false）；配置已加载时按配置启动
        ApplyDisplayOptions(ReadConfiguredFullscreen());

        // 键盘焦点一旦落进 WebView2 的子窗口，WPF 的 PreviewKeyDown 与线程消息钩子都收不到按键，
        // 无边框全屏下用户会被困住 —— 必须走 WebView2 自己的消息通道。
        // Blazor.WebView 要到窗口加载后才存在，所以挂到 Loaded。
        Loaded += (_, _) => HookWebViewAccelerators();
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;

        // 设置页里改「无边框全屏」后立即生效
        _configStore = App.Services.GetService<ConfigStore>();
        if (_configStore is not null)
        {
            _configStore.Changed += OnConfigChanged;
            Closed += (_, _) => _configStore.Changed -= OnConfigChanged;
        }

        Closed += (_, _) => ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
    }

    private static bool ReadConfiguredFullscreen()
    {
        try
        {
            return App.Services.GetService<IConfigProvider>()?.Current.WindowsBorderlessFullscreen ?? false;
        }
        catch
        {
            return false;
        }
    }

    private void OnConfigChanged(AppConfig config) => ApplyDisplayOptions(config.WindowsBorderlessFullscreen);

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

        if (_applied && _borderlessFullscreen == borderlessFullscreen)
        {
            return;
        }

        _applied = true;
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
        if (HandleShortcut(e.Key))
        {
            e.Handled = true;
        }
    }

    /// <summary>焦点在 WebView2 里时，按键只会到达线程消息队列，靠这里兜住。</summary>
    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WmKeyDown)
        {
            return;
        }

        if (HandleShortcut(KeyInterop.KeyFromVirtualKey((int)msg.wParam)))
        {
            handled = true;
        }
    }

    private bool HandleShortcut(Key key)
    {
        switch (key)
        {
            case Key.Escape when _borderlessFullscreen:
                ToggleFullscreen(false);
                return true;
            case Key.F11:
                ToggleFullscreen(!_borderlessFullscreen);
                return true;
            default:
                return false;
        }
    }

    private void HookWebViewAccelerators()
    {
        var webView = Blazor.WebView;
        if (webView is null)
        {
            return;
        }

        webView.CoreWebView2InitializationCompleted += (_, _) => AttachCoreHandler();
        AttachCoreHandler();
    }

    private void AttachCoreHandler()
    {
        var core = Blazor.WebView?.CoreWebView2;
        if (core is null)
        {
            return;
        }

        core.WebMessageReceived -= OnWebMessageReceived;
        core.WebMessageReceived += OnWebMessageReceived;
    }

    /// <summary>接收 index.html 转发的 F11/Esc（焦点在 WebView2 内容里时的唯一可靠通道）。</summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        const string prefix = "hostKey:";
        if (message is null || !message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        var key = message[prefix.Length..] switch
        {
            "F11" => Key.F11,
            "Escape" => Key.Escape,
            _ => Key.None,
        };

        if (key != Key.None)
        {
            HandleShortcut(key);
        }
    }

    /// <summary>
    /// 用户主动切换：先应用，再写回配置。保存触发的 Changed 会以同一个值回调，
    /// 被 ApplyDisplayOptions 的同值判断吸收，不会抖动。
    /// </summary>
    private void ToggleFullscreen(bool borderlessFullscreen)
    {
        ApplyDisplayOptions(borderlessFullscreen);

        var config = _configStore?.Current;
        if (_configStore is null || config is null || config.WindowsBorderlessFullscreen == borderlessFullscreen)
        {
            return;
        }

        config.WindowsBorderlessFullscreen = borderlessFullscreen;
        _ = _configStore.SaveAsync(config);
    }
}
