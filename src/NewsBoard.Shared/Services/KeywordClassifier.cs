namespace NewsBoard.Shared.Services;

/// <summary>
/// 共享频道源（整频道 RSS，模板里没有 {q}）抓回来的条目本身不带分类信息 ——
/// 它们的 Keyword 是频道名（实测全是「中新网即时新闻」），画面上看着就像没有分类。
/// 这里按配置的关键字顺序在标题、正文里找第一个命中项，命中哪个就是这条新闻的分类。
/// </summary>
public static class KeywordClassifier
{
    /// <summary>都没有命中时返回 <paramref name="fallback"/>（通常是频道名）。</summary>
    public static string Classify(string? title, string? content, IReadOnlyList<string> keywords, string fallback = "")
    {
        var hit = FirstHit(title, keywords) ?? FirstHit(content, keywords);
        return string.IsNullOrWhiteSpace(hit) ? fallback : hit;
    }

    /// <summary>
    /// 把一个候选词匹配回配置里的关键字（不区分大小写，返回配置里的原样写法）。
    /// 模型归类用它做白名单校验，防止标签里冒出配置之外的自造分类；匹配不到返回 null。
    /// </summary>
    public static string? Match(string? candidate, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var trimmed = candidate.Trim();
        foreach (var keyword in keywords)
        {
            var configured = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(configured)) continue;
            if (string.Equals(configured, trimmed, StringComparison.OrdinalIgnoreCase)) return configured;
        }

        return null;
    }

    private static string? FirstHit(string? text, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text) || keywords.Count == 0) return null;

        foreach (var keyword in keywords)
        {
            var trimmed = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            if (text.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) return trimmed;
        }

        return null;
    }
}
