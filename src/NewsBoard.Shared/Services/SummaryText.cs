namespace NewsBoard.Shared.Services;

/// <summary>
/// 摘要文本的本地二次校验：模型经常超字数，两条通道（RSS 提炼 / 联网搜索）都要过这一关。
/// </summary>
public static class SummaryText
{
    /// <summary>
    /// 压平换行、去首尾空白；超过 <paramref name="maxChars"/> 的 1.2 倍时截断到最后一个句号
    /// （尽量不切断句子），没有句号则硬截断。
    /// </summary>
    public static string Normalize(string? text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        var limit = maxChars > 0 ? (int)(maxChars * 1.2) : cleaned.Length;
        if (cleaned.Length <= limit)
        {
            return cleaned;
        }

        var cut = cleaned.LastIndexOf('。', Math.Min(limit, cleaned.Length - 1));
        return cut >= 0 ? cleaned[..(cut + 1)] : cleaned[..limit];
    }
}
