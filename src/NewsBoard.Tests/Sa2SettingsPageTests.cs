using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NewsBoard.Shared.Components.Pages;
using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

#pragma warning disable BL0006 // 测试要直接读渲染树；这是不引第三方包（bUnit）时唯一能验证"页面不白屏"的办法

namespace NewsBoard.Tests;

/// <summary>设置页渲染测试：重点是宿主没注册 IAutoStart 时页面不能抛异常/白屏。</summary>
public sealed class Sa2SettingsPageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nb-sa2-page-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Settings_RendersWithoutIAutoStart_DoesNotThrowAndDisablesSwitch()
    {
        var html = await RenderSettingsAsync(autoStart: null);

        Assert.Contains("RSS 备用源", html);
        Assert.Contains("宿主未注册 IAutoStart", html);
        Assert.Contains("宿主未提供，不支持", html);
        Assert.Contains("disabled", html);
    }

    /// <summary>
    /// 设置页排版回归：必须用 settings-card 分组容器，且不能复用播放页的 .card
    /// （app.css 里 .card 是 display:flex 的新闻卡片，撞名会让所有行挤成一行）。
    /// 「取消」必须是回看板的链接，点了要能离开本页。
    /// </summary>
    [Fact]
    public async Task Settings_UsesGroupedCardLayout_AndCancelIsBackLink()
    {
        var html = await RenderSettingsAsync(autoStart: null);

        Assert.Contains("settings-page", html);
        Assert.Contains("class=\"settings-card\"", html);
        Assert.Contains("class=\"settings-actions\"", html);
        Assert.Contains("class=\"btn-secondary\"", html);
        Assert.Contains("href=\"/\"", html);
        Assert.DoesNotContain("class=\"card\"", html);
    }

    [Fact]
    public async Task Settings_RendersCurrentFallbackTemplatesIntoTextarea()
    {
        var html = await RenderSettingsAsync(autoStart: null);
        Assert.Contains("http://www.people.com.cn/rss/politics.xml", html);
        Assert.Contains("https://www.ithome.com/rss/", html);
        Assert.Contains("当前 2 条", html);
    }

    [Fact]
    public async Task Settings_SupportedAutoStart_ShowsActualSystemState()
    {
        var autoStart = new FakeAutoStart { IsSupported = true, Enabled = true };
        var html = await RenderSettingsAsync(autoStart);

        Assert.True(autoStart.IsEnabledCallCount > 0);
        Assert.Contains("系统已启用", html);
        Assert.DoesNotContain("宿主未注册 IAutoStart", html);
    }

    [Fact]
    public async Task Settings_UnsupportedAutoStart_ShowsUnsupportedNotice()
    {
        var autoStart = new FakeAutoStart { IsSupported = false, Enabled = false };
        var html = await RenderSettingsAsync(autoStart);

        Assert.Contains("当前平台不支持开机自启", html);
        Assert.Contains("本平台不支持", html);
    }

    [Fact]
    public async Task Settings_BrokenAutoStart_DoesNotThrow()
    {
        var html = await RenderSettingsAsync(new ThrowingAutoStart());

        Assert.Contains("能力检测失败", html);
        Assert.Contains("本平台不支持", html);
    }

    private async Task<string> RenderSettingsAsync(IAutoStart? autoStart)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretStore>(new MemorySecretStore());
        services.AddSingleton(sp => new ConfigStore(sp.GetRequiredService<ISecretStore>(), _dir));
        services.AddSingleton<ISpeechService>(new FakeSpeech());
        services.AddSingleton<IScreenController>(new FakeScreen());
        if (autoStart is not null)
        {
            services.AddSingleton(autoStart);
        }

        var provider = services.BuildServiceProvider();
        using var renderer = new FrameCollectingRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.RenderAsync<Settings>();
    }

    /// <summary>
    /// 极简渲染器：把渲染树拼成一段文本（元素/属性/文本），用于断言页面真的渲染出了什么、
    /// 以及宿主服务缺失时生命周期不抛异常。不依赖 ASP.NET Core 共享框架的 HtmlRenderer（测试项目拿不到）。
    /// </summary>
    private sealed class FrameCollectingRenderer : Renderer
    {
        public FrameCollectingRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
            : base(services, loggerFactory)
        {
        }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public Task<string> RenderAsync<TComponent>()
            where TComponent : IComponent
            => Dispatcher.InvokeAsync(() => RenderCoreAsync<TComponent>());

        private async Task<string> RenderCoreAsync<TComponent>()
            where TComponent : IComponent
        {
            var component = InstantiateComponent(typeof(TComponent));
            var componentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(componentId);

            var frames = GetCurrentRenderTreeFrames(componentId);
            var builder = new StringBuilder();
            for (var i = 0; i < frames.Count; i++)
            {
                WriteFrame(builder, frames.Array[i]);
            }

            return builder.ToString();
        }

        private static void WriteFrame(StringBuilder builder, RenderTreeFrame frame)
        {
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Element:
                    builder.Append('<').Append(frame.ElementName);
                    break;
                case RenderTreeFrameType.Component:
                    builder.Append('<').Append(frame.ComponentType.Name);
                    break;
                case RenderTreeFrameType.Attribute:
                    builder.Append(' ').Append(frame.AttributeName).Append("=\"")
                        .Append(frame.AttributeValue).Append('"');
                    break;
                case RenderTreeFrameType.Text:
                    builder.Append(frame.TextContent);
                    break;
                case RenderTreeFrameType.Markup:
                    builder.Append(frame.MarkupContent);
                    break;
            }
        }

        protected override void HandleException(Exception exception) => throw exception;

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key)
            => Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSpeech : ISpeechService
    {
        public Task<bool> IsAvailableAsync(string language) => Task.FromResult(true);

        public Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class FakeScreen : IScreenController
    {
        public Task SetKeepScreenOnAsync(bool on) => Task.CompletedTask;

        public Task SetBlackoutAsync(bool black) => Task.CompletedTask;

        public Task SetFullscreenAsync(bool fullscreen) => Task.CompletedTask;
    }

    private sealed class FakeAutoStart : IAutoStart
    {
        public bool IsSupported { get; set; }

        public bool Enabled { get; set; }

        public int IsEnabledCallCount { get; private set; }

        public Task<bool> IsEnabledAsync()
        {
            IsEnabledCallCount++;
            return Task.FromResult(Enabled);
        }

        public Task SetEnabledAsync(bool enabled)
        {
            Enabled = enabled;
            return Task.CompletedTask;
        }

        public Task<bool> OpenSystemSettingsAsync() => Task.FromResult(false);
    }

    private sealed class ThrowingAutoStart : IAutoStart
    {
        public bool IsSupported => throw new InvalidOperationException("平台探测炸了");

        public Task<bool> IsEnabledAsync() => throw new InvalidOperationException("平台探测炸了");

        public Task SetEnabledAsync(bool enabled) => Task.CompletedTask;

        public Task<bool> OpenSystemSettingsAsync() => Task.FromResult(false);
    }
}
