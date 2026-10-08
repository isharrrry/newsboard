using System.Reflection;
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

#pragma warning disable BL0006 // 测试要直接读渲染树；这是不引第三方包（bUnit）时唯一能验证"页面真的渲染出了什么"的办法

namespace NewsBoard.Tests;

/// <summary>
/// SA7：设置页新增配置项的回归 —— 摘要语言 / 时钟字号与字体 / 重要度门槛与保留天数 /
/// 「显示新闻图片（图文模式）」与播放模式的双向等价，以及"不得使用 .card 类名"的排版约束。
/// </summary>
public sealed class Sa7SettingsOptionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nb-sa7-page-" + Guid.NewGuid().ToString("N"));

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
    public async Task 设置页_摘要语言下拉存在且绑定SummaryLanguage()
    {
        var (html, _) = await RenderAsync();

        Assert.Contains("id=\"summaryLanguage\"", html);
        Assert.Contains("简体中文", html);
        Assert.Contains("繁體中文", html);
        Assert.Contains("English", html);
        // @bind 生效的证据：select 元素自己带上了配置里那一项（默认简体中文）。
        // 用 [^<]* 把匹配限在同一元素内 —— 否则 option 里的 value="SimplifiedChinese" 也会让断言侥幸通过。
        Assert.Matches("id=\"summaryLanguage\"[^<]*value=\"SimplifiedChinese\"", html);
        Assert.Contains("模型提炼与呈现都用这个语言", html);
    }

    [Fact]
    public async Task 设置页_时钟字号与时钟字体两项都在()
    {
        var (html, _) = await RenderAsync();

        Assert.Contains("id=\"clockFontSize\"", html);
        Assert.Contains("min=\"8\"", html);
        Assert.Contains("max=\"200\"", html);
        Assert.Matches("id=\"clockFontSize\"[^<]*value=\"32\"", html); // 默认 ClockFontSizeSp

        Assert.Contains("id=\"clockFontFamily\"", html);
        Assert.Contains("留空=跟随系统，例如 Microsoft YaHei", html);
    }

    [Fact]
    public async Task 设置页_重要度门槛与保留天数都在()
    {
        var (html, _) = await RenderAsync();

        Assert.Contains("id=\"importanceThreshold\"", html);
        Assert.Contains("step=\"0.05\"", html);
        Assert.Matches("id=\"importanceThreshold\"[^<]*value=\"0\\.7\"", html);
        Assert.Contains("只播放模型评分 ≥ 门槛的条目", html);

        Assert.Contains("id=\"retentionDays\"", html);
        Assert.Matches("id=\"retentionDays\"[^<]*value=\"3\"", html);
        Assert.Contains("缓存只保留最近 N 天", html);
    }

    [Fact]
    public async Task 设置页_显示图片复选框与播放模式双向等价()
    {
        var (html, component) = await RenderAsync();
        Assert.Contains("id=\"showImages\"", html);
        Assert.Contains("显示新闻图片（图文模式）", html);

        var type = typeof(Settings);
        var showImages = type.GetProperty("ShowImages", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("设置页缺少私有派生属性 ShowImages");
        var editField = type.GetField("_edit", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("设置页缺少 _edit 字段");
        var edit = (AppConfig)editField.GetValue(component)!;

        // getter 方向：PlaybackMode → ShowImages
        edit.PlaybackMode = PlaybackMode.TextAndImage;
        Assert.True((bool)showImages.GetValue(component)!);
        edit.PlaybackMode = PlaybackMode.TextOnly;
        Assert.False((bool)showImages.GetValue(component)!);

        // setter 方向：ShowImages → PlaybackMode
        showImages.SetValue(component, true);
        Assert.Equal(PlaybackMode.TextAndImage, edit.PlaybackMode);
        showImages.SetValue(component, false);
        Assert.Equal(PlaybackMode.TextOnly, edit.PlaybackMode);

        // 两处同源：把 PlaybackMode 改成图文后让**同一个组件实例**重新渲染，
        // 「播放 / 语音」里的播放模式下拉必须跟着变成图文
        // （[^<]* 保证匹配的是 select 元素自身，而不是它下面那个 value="TextAndImage" 的 option）
        var mutated = await RenderWithMutationAsync(e => e.PlaybackMode = PlaybackMode.TextAndImage);
        Assert.Contains("id=\"playbackMode\"", mutated);
        Assert.Matches("id=\"playbackMode\"[^<]*value=\"TextAndImage\"", mutated);
        // 同时：外观组那个复选框必须自己勾上（渲染层也联动，不只是字段相等）
        Assert.Matches("id=\"showImages\"[^<]*checked", mutated);

        // 反向：纯文字时复选框不能是勾选态
        var plain = await RenderWithMutationAsync(e => e.PlaybackMode = PlaybackMode.TextOnly);
        Assert.Matches("id=\"playbackMode\"[^<]*value=\"TextOnly\"", plain);
        Assert.DoesNotMatch("id=\"showImages\"[^<]*checked", plain);
    }

    [Fact]
    public async Task 设置页_不得使用会与播放页撞名的_card类名()
    {
        var (html, _) = await RenderAsync();

        Assert.Contains("class=\"settings-card\"", html);
        Assert.DoesNotContain("class=\"card\"", html);
    }

    private async Task<(string Html, Settings Component)> RenderAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretStore>(new MemorySecretStore());
        services.AddSingleton(sp => new ConfigStore(sp.GetRequiredService<ISecretStore>(), _dir));
        services.AddSingleton<ISpeechService>(new FakeSpeech());
        services.AddSingleton<IScreenController>(new FakeScreen());

        var provider = services.BuildServiceProvider();
        using var renderer = new FrameCollectingRenderer(provider, NullLoggerFactory.Instance);
        var component = await renderer.RenderComponentAsync<Settings>();
        var html = renderer.LastHtml ?? "";

        // 留一份渲染结果便于人工核对（路径见测试结论）
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "sa7-settings-render.html"), html);
        }
        catch (IOException)
        {
        }

        return (html, component);
    }

    /// <summary>改组件内存副本里的配置后，让同一个组件实例重新渲染，返回重新渲染后的文本。</summary>
    private async Task<string> RenderWithMutationAsync(Action<AppConfig> mutate)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretStore>(new MemorySecretStore());
        services.AddSingleton(sp => new ConfigStore(sp.GetRequiredService<ISecretStore>(), _dir));
        services.AddSingleton<ISpeechService>(new FakeSpeech());
        services.AddSingleton<IScreenController>(new FakeScreen());

        var provider = services.BuildServiceProvider();
        using var renderer = new FrameCollectingRenderer(provider, NullLoggerFactory.Instance);
        var component = await renderer.RenderComponentAsync<Settings>();

        var editField = typeof(Settings).GetField("_edit", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("设置页缺少 _edit 字段");
        mutate((AppConfig)editField.GetValue(component)!);

        var html = await renderer.ReRenderAsync();
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "sa7-settings-render-mutated.html"), html);
        }
        catch (IOException)
        {
        }

        return html;
    }

    /// <summary>
    /// 极简渲染器：把渲染树拼成一段文本（元素/属性/文本），并保留最后一个组件实例，
    /// 便于对派生属性做双向断言。不依赖 ASP.NET Core 共享框架的 HtmlRenderer（测试项目拿不到）。
    /// </summary>
    private sealed class FrameCollectingRenderer : Renderer
    {
        public FrameCollectingRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
            : base(services, loggerFactory)
        {
        }

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public string? LastHtml { get; private set; }

        private int _componentId;

        public Task<TComponent> RenderComponentAsync<TComponent>()
            where TComponent : IComponent
            => Dispatcher.InvokeAsync(() => RenderCoreAsync<TComponent>());

        /// <summary>让同一个组件实例重新渲染一次（用于验证"改配置后界面跟着变"）。</summary>
        public Task<string> ReRenderAsync()
            => Dispatcher.InvokeAsync(async () =>
            {
                await RenderRootComponentAsync(_componentId);
                return Collect(_componentId);
            });

        private async Task<TComponent> RenderCoreAsync<TComponent>()
            where TComponent : IComponent
        {
            var component = InstantiateComponent(typeof(TComponent));
            _componentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(_componentId);

            LastHtml = Collect(_componentId);
            return (TComponent)component;
        }

        private string Collect(int componentId)
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            var builder = new StringBuilder();
            for (var i = 0; i < frames.Count; i++)
            {
                WriteFrame(builder, frames.Array[i]);
            }

            LastHtml = builder.ToString();
            return LastHtml;
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
}
