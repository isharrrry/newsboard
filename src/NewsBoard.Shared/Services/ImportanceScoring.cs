using System.Globalization;
using System.Text.Json;

namespace NewsBoard.Shared.Services;

/// <summary>
/// 重要度评分的本地二次处理（纯函数，无 IO、无平台依赖）。
/// 背景：模型给出的分数经常「不分散」——要么整批都是 0.9，要么整批都在 0.3 附近，
/// 这样门槛过滤后要么全留要么全丢，随机播放在同一档里也没有意义。
/// 这里在「整批退化」时按**秩**把分数在 [<see cref="LowBand"/>, <see cref="HighBand"/>] 上均匀展开，
/// 让同一批里必然出现高低差的分布；不退化时只做 [0,1] 夹取，尊重模型原本的判断。
/// </summary>
public static class ImportanceScoring
{
    /// <summary>展开区间的下界：不把低分压到 0，避免整批被门槛一扫而空。</summary>
    public const double LowBand = 0.15;

    /// <summary>展开区间的上界：不把高分顶到 1，留出「还有更重要的」余地。</summary>
    public const double HighBand = 0.95;

    /// <summary>极差小于它视为「没有拉开差距」。</summary>
    public const double FlatSpread = 0.1;

    /// <summary>落在同一端的比例达到它即视为「堆在一端」。</summary>
    public const double PileUpRatio = 0.8;

    /// <summary>「高分端」的起点。</summary>
    public const double HighPile = 0.85;

    /// <summary>「低分端」的终点。</summary>
    public const double LowPile = 0.15;

    /// <summary>少于这个条数时不做秩展开（避免 1–2 条被强行制造出高分）。</summary>
    public const int MinCountForRebalance = 3;

    /// <summary>把分数夹到 [0,1]；非有限值（NaN/∞）按 0 处理由调用方决定。</summary>
    public static double Clamp(double value)
        => value < 0 ? 0 : value > 1 ? 1 : value;

    /// <summary>
    /// 读取 JSON 元素里的 <paramref name="property"/>（数字，或「0.82」这类数字字符串）。
    /// 缺失、类型不对、非有限值 → null；越界 → 夹到 [0,1]。
    /// </summary>
    public static double? Read(JsonElement element, string property = "importance")
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var fromText) => fromText,
            _ => double.NaN,
        };

        return double.IsFinite(parsed) ? Clamp(parsed) : null;
    }

    /// <summary>
    /// 对一批重要度做均衡。返回列表与入参**按下标一一对应**：
    /// <list type="bullet">
    /// <item>null 原样保留（表示「未评分」，例如旧缓存或模型没给分）；</item>
    /// <item>非 null 先夹到 [0,1]；</item>
    /// <item>整批退化（极差 &lt; <see cref="FlatSpread"/>，或 ≥80% 落在高分端/低分端）且条数 ≥
    /// <see cref="MinCountForRebalance"/> 时，按升序秩在 [<see cref="LowBand"/>, <see cref="HighBand"/>] 均匀展开，
    /// 同分并列取相同分数；</item>
    /// <item>否则只返回夹取后的值。</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<double?> Balance(IReadOnlyList<double?> raw)
    {
        var result = new double?[raw.Count];
        var present = new List<(double Value, int Index)>();
        for (var i = 0; i < raw.Count; i++)
        {
            if (raw[i] is { } value && double.IsFinite(value))
            {
                var clamped = Clamp(value);
                result[i] = clamped;
                present.Add((clamped, i));
            }
            else
            {
                result[i] = null;
            }
        }

        if (present.Count == 0 || present.Count < MinCountForRebalance || !IsDegenerate(present))
        {
            return result;
        }

        // 升序秩 → 分数。模型已经把值区分开时，同分并列共用组内平均秩（同分同酬）；
        // 整批完全相同时（一个 distinct 值，信息量为零）按输入顺序破平，否则展开等于没做。
        var sorted = present.OrderBy(p => p.Value).ThenBy(p => p.Index).ToList();
        var distinguishable = present.Select(p => p.Value).Distinct().Count() > 1;
        var count = sorted.Count;
        var cursor = 0;

        while (cursor < count)
        {
            var end = cursor;
            if (distinguishable)
            {
                while (end + 1 < count && sorted[end + 1].Value == sorted[cursor].Value) end++;
            }

            var rank = (cursor + end) / 2.0;
            var score = Math.Round(LowBand + (HighBand - LowBand) * (rank / (count - 1)), 2);
            for (var k = cursor; k <= end; k++)
            {
                result[sorted[k].Index] = score;
            }

            cursor = end + 1;
        }

        return result;
    }

    /// <summary>一批分数是否「退化」（没拉开差距，或堆在某一端）。</summary>
    private static bool IsDegenerate(List<(double Value, int Index)> present)
    {
        var min = present.Min(p => p.Value);
        var max = present.Max(p => p.Value);
        if (max - min < FlatSpread) return true;

        var high = present.Count(p => p.Value >= HighPile);
        var low = present.Count(p => p.Value <= LowPile);
        var limit = present.Count * PileUpRatio;
        return high >= limit || low >= limit;
    }
}
