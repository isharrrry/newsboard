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

/// <summary>摘要/提炼的输出语言（呈现与朗读文本都跟随它）。</summary>
public enum SummaryLanguageOption { SimplifiedChinese, TraditionalChinese, English }

/// <summary>语言选项 → 提示词里给模型的语言约束。</summary>
public static class SummaryLanguageText
{
    public static string DisplayName(this SummaryLanguageOption option) => option switch
    {
        SummaryLanguageOption.TraditionalChinese => "繁體中文",
        SummaryLanguageOption.English => "English",
        _ => "简体中文",
    };

    /// <summary>写进提示词的硬性语言要求。</summary>
    public static string PromptRule(this SummaryLanguageOption option) => option switch
    {
        SummaryLanguageOption.TraditionalChinese
            => "全部用繁体中文书写，不要出现简体字（专有名词按当地习惯译名）。",
        SummaryLanguageOption.English
            => "Write everything in English, do not output any Chinese characters.",
        _ => "全部用简体中文书写：禁止出现繁体字，禁止中英夹杂（人名、机构名、产品型号等专有名词可保留原文）。",
    };
}


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

    /// <summary>摘要与提炼的输出语言（默认简体中文）。</summary>
    public SummaryLanguageOption SummaryLanguage { get; set; } = SummaryLanguageOption.SimplifiedChinese;

    /// <summary>重要度门槛：模型评出的 importance 低于它就不进入播放列表。</summary>
    public double ImportanceThreshold { get; set; } = 0.7;

    /// <summary>缓存保留天数，超过则从列表清理（默认 3 天）。</summary>
    public int RetentionDays { get; set; } = 3;

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

    /// <summary>时钟字号（sp），默认 32；不再由关键字标签字号推导。</summary>
    public int ClockFontSizeSp { get; set; } = 32;

    /// <summary>时钟字体族，空 = 跟随系统默认。</summary>
    public string ClockFontFamily { get; set; } = "";

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
    /// <summary>播放顺序：默认随机 —— 只播重要度达标的条目，顺序打散避免每天都是同几条第位。</summary>
    public PlayOrder PlayOrder { get; set; } = PlayOrder.Random;
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
