namespace NewsBoard.Maui;

public partial class MainPage : ContentPage
{
    private bool _started;

    public MainPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 等配置与缓存加载完再显示 Blazor，避免首屏出现空播放列表
        if (!_started)
        {
            _started = true;
            await NewsBoardBoot.WaitAsync(TimeSpan.FromSeconds(10));
        }

        LoadingLabel.IsVisible = false;
        WebView.IsVisible = true;
    }
}
