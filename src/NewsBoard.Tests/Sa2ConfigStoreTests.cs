using NewsBoard.Shared.Services;
using NewsBoard.Shared.Services.Abstractions;
using Xunit;

namespace NewsBoard.Tests;

public sealed class Sa2ConfigStoreTests : IDisposable
{
    private const string Key = "sk-sa2-plaintext-check-1234";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nb-sa2-" + Guid.NewGuid().ToString("N"));

    private ConfigStore NewStore(ISecretStore? secrets = null, string? dir = null)
        => new(secrets ?? new MemorySecretStore(), dir ?? _dir);

    private string ConfigFile => Path.Combine(_dir, "config.json");

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
    public async Task LoadAsync_NoFile_UsesDefaultsGroupByGroup()
    {
        var store = NewStore();
        await store.LoadAsync();

        Assert.Equal(NewsFetchMode.RssThenSummarize, store.Current.FetchMode);
        Assert.Equal("https://api.deepseek.com", store.Current.BaseUrl);
        Assert.Equal("https://api.deepseek.com/anthropic", store.Current.BaseUrlSearch);
        Assert.Equal(3, store.Current.SearchMaxUses);
        Assert.Equal(new[] { "武汉", "中国", "美国", "台湾", "国际", "AI" }, store.Current.Keywords);
        Assert.Equal(5, store.Current.FetchIntervalMinutes);
        Assert.Equal(50, store.Current.SummaryMaxChars);
        Assert.Equal(10, store.Current.AiMinIntervalMinutes);
        Assert.True(store.Current.OnlyTodayNews);
        Assert.Null(store.Current.ApiKey);

        Assert.Equal("#000000", store.Current.BackgroundColor);
        Assert.Equal("#FFFFFF", store.Current.ForegroundColor);
        Assert.Equal("#00D4AA", store.Current.AccentColor);
        Assert.Equal(56, store.Current.FontSizeSp);
        Assert.Equal("Bold", store.Current.FontWeight);
        Assert.Equal(1.35, store.Current.LineHeight, 6);
        Assert.Equal(48, store.Current.PagePaddingPx);
        Assert.Equal(28, store.Current.KeywordLabelFontSizeSp);
        Assert.Equal(TextAlignOption.Center, store.Current.TextAlign);
        Assert.True(store.Current.ShowClock);
        Assert.True(store.Current.ShowKeywordLabel);
        Assert.False(store.Current.ShowSource);

        Assert.Equal(PlaybackMode.TextOnly, store.Current.PlaybackMode);
        Assert.True(store.Current.SpeechEnabled);
        Assert.Equal(SpeechContent.SummaryOnly, store.Current.SpeechContent);
        Assert.Equal(1.0, store.Current.SpeechRate, 6);
        Assert.Equal(1.0, store.Current.SpeechPitch, 6);
        Assert.Equal("zh-CN", store.Current.SpeechLanguage);
        Assert.Equal(1, store.Current.SpeechLeadSeconds);
        Assert.Equal("", store.Current.QuietHours);
        Assert.Equal(TtsUnavailablePolicy.FallbackHold, store.Current.TtsUnavailable);
        Assert.Equal(60, store.Current.ShowDurationSeconds);
        Assert.Equal(12, store.Current.ItemHoldSeconds);
        Assert.Equal(60, store.Current.ScrollSpeedPxPerSecond);
        Assert.Equal(300, store.Current.BlackoutDurationSeconds);
        Assert.True(store.Current.BlackoutCycleEnabled);
        Assert.Equal(BlackoutMode.BrightnessZeroAndBlack, store.Current.BlackoutMode);
        Assert.Equal(WindowEndBehavior.FinishCurrentItem, store.Current.WindowEndBehavior);
        Assert.Equal(ImagePosition.TopImageBottomText, store.Current.ImagePosition);
        Assert.Equal(40, store.Current.ImageScreenPercent);
        Assert.Equal(PlayOrder.ByKeywordOrder, store.Current.PlayOrder);
        Assert.True(store.Current.ResumeAcrossRounds);

        Assert.True(store.Current.KeepScreenOn);
        Assert.True(store.Current.AutoStart);
        Assert.False(store.Current.WifiOnlyFetch);
        Assert.False(store.Current.WindowsBorderlessFullscreen);
        Assert.True(store.Current.AndroidShowStatusBar);

        Assert.False(File.Exists(ConfigFile));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsEveryGroup()
    {
        var store = NewStore();
        await store.LoadAsync();

        var config = store.Current.Clone();
        config.FetchMode = NewsFetchMode.WebSearch;
        config.BaseUrl = "https://example.invalid/v1";
        config.Model = "deepseek-flash";
        config.BaseUrlSearch = "https://example.invalid/anthropic";
        config.SearchMaxUses = 7;
        config.Keywords = ["武汉", "AI", "航天"];
        config.ItemsPerKeyword = 4;
        config.FetchIntervalMinutes = 15;
        config.SummaryMaxChars = 60;
        config.AiMinIntervalMinutes = 20;
        config.RssTemplate = "https://example.invalid/rss?q={q}";
        config.OnlyTodayNews = false;
        config.BackgroundColor = "#112233";
        config.ForegroundColor = "#F0F0F0";
        config.AccentColor = "#FF8800";
        config.FontSizeSp = 72;
        config.FontWeight = "Normal";
        config.LineHeight = 1.6;
        config.PagePaddingPx = 64;
        config.KeywordLabelFontSizeSp = 32;
        config.TextAlign = TextAlignOption.Right;
        config.ShowClock = false;
        config.ShowKeywordLabel = false;
        config.ShowSource = true;
        config.PlaybackMode = PlaybackMode.TextAndImage;
        config.SpeechEnabled = false;
        config.SpeechContent = SpeechContent.TitleAndSummary;
        config.SpeechRate = 1.75;
        config.SpeechPitch = 0.8;
        config.SpeechLanguage = "zh-TW";
        config.SpeechLeadSeconds = 3;
        config.QuietHours = "23:00-07:00";
        config.TtsUnavailable = TtsUnavailablePolicy.SkipItem;
        config.ShowDurationSeconds = 90;
        config.ItemHoldSeconds = 20;
        config.ScrollSpeedPxPerSecond = 80;
        config.BlackoutDurationSeconds = 420;
        config.BlackoutCycleEnabled = false;
        config.BlackoutMode = BlackoutMode.BlackOnly;
        config.WindowEndBehavior = WindowEndBehavior.InterruptImmediately;
        config.ImagePosition = ImagePosition.LeftImageRightText;
        config.ImageScreenPercent = 55;
        config.PlayOrder = PlayOrder.Random;
        config.ResumeAcrossRounds = false;
        config.KeepScreenOn = false;
        config.AutoStart = false;
        config.WifiOnlyFetch = true;
        config.WindowsBorderlessFullscreen = true;
        config.AndroidShowStatusBar = false;

        await store.SaveAsync(config);

        var reloaded = NewStore();
        await reloaded.LoadAsync();
        var c = reloaded.Current;

        Assert.Equal(NewsFetchMode.WebSearch, c.FetchMode);
        Assert.Equal("https://example.invalid/v1", c.BaseUrl);
        Assert.Equal("deepseek-flash", c.Model);
        Assert.Equal("https://example.invalid/anthropic", c.BaseUrlSearch);
        Assert.Equal(7, c.SearchMaxUses);
        Assert.Equal(new[] { "武汉", "AI", "航天" }, c.Keywords);
        Assert.Equal(4, c.ItemsPerKeyword);
        Assert.Equal(15, c.FetchIntervalMinutes);
        Assert.Equal(60, c.SummaryMaxChars);
        Assert.Equal(20, c.AiMinIntervalMinutes);
        Assert.Equal("https://example.invalid/rss?q={q}", c.RssTemplate);
        Assert.False(c.OnlyTodayNews);
        Assert.Equal("#112233", c.BackgroundColor);
        Assert.Equal("#F0F0F0", c.ForegroundColor);
        Assert.Equal("#FF8800", c.AccentColor);
        Assert.Equal(72, c.FontSizeSp);
        Assert.Equal("Normal", c.FontWeight);
        Assert.Equal(1.6, c.LineHeight, 6);
        Assert.Equal(64, c.PagePaddingPx);
        Assert.Equal(32, c.KeywordLabelFontSizeSp);
        Assert.Equal(TextAlignOption.Right, c.TextAlign);
        Assert.False(c.ShowClock);
        Assert.False(c.ShowKeywordLabel);
        Assert.True(c.ShowSource);
        Assert.Equal(PlaybackMode.TextAndImage, c.PlaybackMode);
        Assert.False(c.SpeechEnabled);
        Assert.Equal(SpeechContent.TitleAndSummary, c.SpeechContent);
        Assert.Equal(1.75, c.SpeechRate, 6);
        Assert.Equal(0.8, c.SpeechPitch, 6);
        Assert.Equal("zh-TW", c.SpeechLanguage);
        Assert.Equal(3, c.SpeechLeadSeconds);
        Assert.Equal("23:00-07:00", c.QuietHours);
        Assert.Equal(TtsUnavailablePolicy.SkipItem, c.TtsUnavailable);
        Assert.Equal(90, c.ShowDurationSeconds);
        Assert.Equal(20, c.ItemHoldSeconds);
        Assert.Equal(80, c.ScrollSpeedPxPerSecond);
        Assert.Equal(420, c.BlackoutDurationSeconds);
        Assert.False(c.BlackoutCycleEnabled);
        Assert.Equal(BlackoutMode.BlackOnly, c.BlackoutMode);
        Assert.Equal(WindowEndBehavior.InterruptImmediately, c.WindowEndBehavior);
        Assert.Equal(ImagePosition.LeftImageRightText, c.ImagePosition);
        Assert.Equal(55, c.ImageScreenPercent);
        Assert.Equal(PlayOrder.Random, c.PlayOrder);
        Assert.False(c.ResumeAcrossRounds);
        Assert.False(c.KeepScreenOn);
        Assert.False(c.AutoStart);
        Assert.True(c.WifiOnlyFetch);
        Assert.True(c.WindowsBorderlessFullscreen);
        Assert.False(c.AndroidShowStatusBar);

        var json = File.ReadAllText(ConfigFile);
        Assert.Contains("\"WebSearch\"", json);
        Assert.Contains("武汉", json);
        Assert.False(File.Exists(ConfigFile + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_UpdatesCurrent_AndRaisesChanged()
    {
        var store = NewStore();
        await store.LoadAsync();

        AppConfig? notified = null;
        var count = 0;
        store.Changed += c =>
        {
            notified = c;
            count++;
        };

        var edit = store.Current.Clone();
        edit.FontSizeSp = 88;
        await store.SaveAsync(edit);

        Assert.Equal(1, count);
        Assert.Same(store.Current, notified);
        Assert.Equal(88, store.Current.FontSizeSp);
    }

    [Fact]
    public async Task SaveAsync_DoesNotWriteApiKeyPlaintextIntoJson()
    {
        var secrets = new MemorySecretStore();
        var store = NewStore(secrets);
        await store.LoadAsync();
        await store.SetApiKeyAsync(Key);

        await store.SaveAsync(store.Current);

        var json = File.ReadAllText(ConfigFile);
        Assert.DoesNotContain("plaintext-check", json);
        Assert.DoesNotContain("ApiKey", json);
        Assert.DoesNotContain("apiKey", json);
        Assert.Equal(Key, await secrets.GetAsync(ConfigStore.ApiKeySecretName));
        Assert.Equal(Key, store.Current.ApiKey);
    }

    [Fact]
    public async Task SetApiKeyAsync_ThenLoad_InjectsKeyIntoCurrent()
    {
        var secrets = new MemorySecretStore();
        var store = NewStore(secrets);
        Assert.Null(store.Current.ApiKey);
        await store.SetApiKeyAsync("  " + Key + "  ");

        await store.LoadAsync();
        Assert.Equal(Key, store.Current.ApiKey);
        Assert.Equal(Key, await store.GetApiKeyAsync());

        var reloaded = NewStore(secrets, _dir);
        Assert.Null(reloaded.Current.ApiKey);
        await reloaded.LoadAsync();
        Assert.Equal(Key, reloaded.Current.ApiKey);
    }

    [Fact]
    public async Task SetApiKeyAsync_Empty_RemovesKey()
    {
        var secrets = new MemorySecretStore();
        var store = NewStore(secrets);
        await store.SetApiKeyAsync(Key);
        await store.SetApiKeyAsync("");

        Assert.Null(await store.GetApiKeyAsync());
        Assert.Null(store.Current.ApiKey);
    }

    [Fact]
    public async Task LoadAsync_OldJson_MissingFieldsUseDefaultsAndPresentFieldsWin()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(
            ConfigFile,
            """
            {
              "BaseUrl": "https://old.example.com",
              "FontSizeSp": 88,
              "Keywords": ["旧关键字"],
              "ShowSource": true,
              "LineHeight": 1.9
            }
            """);

        var store = NewStore();
        await store.LoadAsync();
        var c = store.Current;

        Assert.Equal("https://old.example.com", c.BaseUrl);
        Assert.Equal(88, c.FontSizeSp);
        Assert.Equal(new[] { "旧关键字" }, c.Keywords);
        Assert.True(c.ShowSource);
        Assert.Equal(1.9, c.LineHeight, 6);

        Assert.Equal(NewsFetchMode.RssThenSummarize, c.FetchMode);
        Assert.Equal("deepseek-chat", c.Model);
        Assert.Equal(3, c.SearchMaxUses);
        Assert.Equal(5, c.FetchIntervalMinutes);
        Assert.Equal(48, c.PagePaddingPx);
        Assert.Equal(PlaybackMode.TextOnly, c.PlaybackMode);
        Assert.Equal(TtsUnavailablePolicy.FallbackHold, c.TtsUnavailable);
        Assert.Equal(300, c.BlackoutDurationSeconds);
        Assert.True(c.AutoStart);
        Assert.True(c.AndroidShowStatusBar);
    }

    [Fact]
    public async Task LoadAsync_KeywordsNullOrEmpty_HandledTolerantly()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(ConfigFile, """{ "Keywords": null, "FontSizeSp": 61 }""");
        var withNull = NewStore();
        await withNull.LoadAsync();
        Assert.Equal(6, withNull.Current.Keywords.Count);
        Assert.Equal(61, withNull.Current.FontSizeSp);

        var dir2 = _dir + "-b";
        Directory.CreateDirectory(dir2);
        await File.WriteAllTextAsync(Path.Combine(dir2, "config.json"), """{ "Keywords": [] }""");
        var withEmpty = NewStore(null, dir2);
        await withEmpty.LoadAsync();
        Assert.Empty(withEmpty.Current.Keywords);

        Directory.Delete(dir2, true);
    }

    [Fact]
    public async Task LoadAsync_BrokenJson_FallsBackToDefaultsWithoutThrowing()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(ConfigFile, """{ "FontSizeSp": 72, """);
        var truncated = NewStore();
        await truncated.LoadAsync();
        Assert.Equal(56, truncated.Current.FontSizeSp);
        Assert.Equal("https://api.deepseek.com", truncated.Current.BaseUrl);

        var dir2 = _dir + "-arr";
        Directory.CreateDirectory(dir2);
        await File.WriteAllTextAsync(Path.Combine(dir2, "config.json"), "[1, 2, 3]");
        var notObject = NewStore(null, dir2);
        await notObject.LoadAsync();
        Assert.Equal(56, notObject.Current.FontSizeSp);

        var dir3 = _dir + "-text";
        Directory.CreateDirectory(dir3);
        await File.WriteAllTextAsync(Path.Combine(dir3, "config.json"), "这不是 JSON");
        var garbage = NewStore(null, dir3);
        await garbage.LoadAsync();
        Assert.Equal(56, garbage.Current.FontSizeSp);
        Assert.NotNull(garbage.Current.Keywords);

        Directory.Delete(dir2, true);
        Directory.Delete(dir3, true);
    }

    [Fact]
    public async Task LoadAsync_WrongFieldType_KeepsDefaultForThatFieldOnly()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(
            ConfigFile,
            """{ "FontSizeSp": "abc", "BaseUrl": 123, "OnlyTodayNews": false, "LineHeight": "高" }""");

        var store = NewStore();
        await store.LoadAsync();
        var c = store.Current;

        Assert.Equal(56, c.FontSizeSp);
        Assert.Equal("https://api.deepseek.com", c.BaseUrl);
        Assert.Equal(1.35, c.LineHeight, 6);
        Assert.False(c.OnlyTodayNews);
    }

    [Fact]
    public async Task SaveAsync_EmptyApiKey_KeepsPreviouslyLoadedKeyInMemory()
    {
        var secrets = new MemorySecretStore();
        var store = NewStore(secrets);
        await store.LoadAsync();
        await store.SetApiKeyAsync(Key);

        var edit = store.Current.Clone();
        edit.ApiKey = null;
        edit.FontSizeSp = 40;
        await store.SaveAsync(edit);

        Assert.Equal(Key, store.Current.ApiKey);
        Assert.Equal(40, store.Current.FontSizeSp);
        Assert.Equal(Key, await secrets.GetAsync(ConfigStore.ApiKeySecretName));
    }

    [Fact]
    public void DefaultStorageDirectory_IsLocalAppDataNewsBoard()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewsBoard");

        var store = new ConfigStore(new MemorySecretStore());
        Assert.Equal(expected, store.StorageDirectory);
        Assert.True(Directory.Exists(expected));
    }

    [Fact]
    public async Task SaveAsync_NormalizesKeywords()
    {
        var store = NewStore();
        await store.LoadAsync();

        var edit = store.Current.Clone();
        edit.Keywords = ["  武汉 ", "", "AI", "武汉", "  "];
        await store.SaveAsync(edit);

        Assert.Equal(new[] { "武汉", "AI" }, store.Current.Keywords);

        var reloaded = NewStore();
        await reloaded.LoadAsync();
        Assert.Equal(new[] { "武汉", "AI" }, reloaded.Current.Keywords);
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
}
