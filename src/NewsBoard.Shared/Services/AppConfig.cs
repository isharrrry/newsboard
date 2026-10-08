using System.Text.Json.Serialization;

namespace NewsBoard.Shared.Services;

public enum NewsFetchMode { RssThenSummarize, WebSearch, Auto }

public enum PlaybackMode { TextOnly, TextAndImage }

public enum SpeechContent { SummaryOnly, TitleAndSummary }

public enum TtsUnavailablePolicy { FallbackHold, SkipItem }

public enum BlackoutMode { BrightnessZeroAndBlack, BlackOnly }

public enum WindowEndBehavior { FinishCurrentItem, InterruptImmediately }

public enum ImagePosition { TopImageBottomText, LeftImageRightText }

public enum PlayOrder { ByKeywordOrder, ByPublishTime, Random }

public enum TextAlignOption { Left, Center, Right }

/// <summary>全部 App 内可配置项。JSON 持久化，不含 ApiKey。</summary>
public sealed class AppConfig
{
    // ---- 4.1 API / 采集 ----
    public NewsFetchMode FetchMode { get; set; } = NewsFetchMode.Auto;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-chat";
    public string BaseUrlSearch { get; set; } = "https://api.deepseek.com/anthropic";
    public int SearchMaxUses { get; set; } = 3;
    public List<string> Keywords { get; set; } = ["武汉", "中国", "美国", "台湾", "国际", "AI"];
    public int ItemsPerKeyword { get; set; } = 3;
    public int FetchIntervalMinutes { get; set; } = 5;
    public int SummaryMaxChars { get; set; } = 70;
    public int AiMinIntervalMinutes { get; set; } = 10;
    /// <summary>RSS 源模板。含 <c>{q}</c> 时按关键字各抓一次（搜索型源）；不含则视为共享频道源，只抓一次。</summary>
    public string RssTemplate { get; set; } = "https://www.chinanews.com.cn/rss/scroll-news.xml";

    /// <summary>主源失败时依次尝试的备用 RSS 源（不含 {q} 的共享频道源）。</summary>
    public List<string> RssFallbackTemplates { get; set; } =
    [
        "http://www.people.com.cn/rss/politics.xml",
        "https://www.ithome.com/rss/",
    ];

    public bool OnlyTodayNews { get; set; } = true;

    /// <summary>运行时从 ISecretStore 注入，永不写入 JSON。</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    // ---- 4.2 外观 ----
    public string BackgroundColor { get; set; } = "#000000";
    public string ForegroundColor { get; set; } = "#FFFFFF";
    public string AccentColor { get; set; } = "#00D4AA";
    public int FontSizeSp { get; set; } = 56;
    public string FontWeight { get; set; } = "Bold";
    public double LineHeight { get; set; } = 1.35;
    public int PagePaddingPx { get; set; } = 48;
    public int KeywordLabelFontSizeSp { get; set; } = 28;
    public TextAlignOption TextAlign { get; set; } = TextAlignOption.Center;
    public bool ShowClock { get; set; } = true;
    public bool ShowKeywordLabel { get; set; } = true;
    public bool ShowTitle { get; set; } = false;
    public bool ShowSource { get; set; } = false;

    // ---- 4.3 播放 / 语音 ----
    public PlaybackMode PlaybackMode { get; set; } = PlaybackMode.TextOnly;
    public bool SpeechEnabled { get; set; } = true;
    public SpeechContent SpeechContent { get; set; } = SpeechContent.SummaryOnly;
    public double SpeechRate { get; set; } = 1.0;
    public double SpeechPitch { get; set; } = 1.0;
    public string SpeechLanguage { get; set; } = "zh-CN";
    public int SpeechLeadSeconds { get; set; } = 1;
    public string QuietHours { get; set; } = "";
    public TtsUnavailablePolicy TtsUnavailable { get; set; } = TtsUnavailablePolicy.FallbackHold;
    public int ShowDurationSeconds { get; set; } = 60;
    public int ItemHoldSeconds { get; set; } = 12;
    public int ScrollSpeedPxPerSecond { get; set; } = 60;
    public int BlackoutDurationSeconds { get; set; } = 300;
    public bool BlackoutCycleEnabled { get; set; } = true;
    public BlackoutMode BlackoutMode { get; set; } = BlackoutMode.BrightnessZeroAndBlack;
    public WindowEndBehavior WindowEndBehavior { get; set; } = WindowEndBehavior.FinishCurrentItem;
    public ImagePosition ImagePosition { get; set; } = ImagePosition.TopImageBottomText;
    public int ImageScreenPercent { get; set; } = 40;
    public PlayOrder PlayOrder { get; set; } = PlayOrder.ByKeywordOrder;
    public bool ResumeAcrossRounds { get; set; } = true;

    // ---- 4.4 系统 / 宿主 ----
    public bool KeepScreenOn { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public bool WifiOnlyFetch { get; set; } = false;
    public bool WindowsBorderlessFullscreen { get; set; } = false;
    public bool AndroidShowStatusBar { get; set; } = true;

    /// <summary>拷贝，供设置页编辑后取消/保存使用。关键字列表一并复制。</summary>
    public AppConfig Clone()
    {
        var clone = (AppConfig)MemberwiseClone();
        clone.Keywords = [.. Keywords];
        return clone;
    }

    /// <summary>把配置中的关键字列表规整为去空、去重、去首尾空格。</summary>
    public void NormalizeKeywords()
    {
        Keywords = Keywords
            .Select(k => k?.Trim() ?? "")
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();
    }
}
