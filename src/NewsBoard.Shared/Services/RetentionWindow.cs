namespace NewsBoard.Shared.Services;

/// <summary>
/// 保留窗口按「自然日」分桶：保留今天在内的最近 N 个自然日（本地时区）。
/// 例如 N=3、今天是 10-08 ⇒ 只保留 10-08 / 10-07 / 10-06 的条目，10-05 及更早已过期。
/// 与「滑动窗口（now − N×24h）」的区别：早上 6 点与晚上 23 点看到的窗口边界是同一批自然日，
/// 不会因为当前时刻而把第三天的内容切掉一半。
/// </summary>
public static class RetentionWindow
{
    /// <summary>窗口里最早保留的那一天的本地零点。</summary>
    public static DateTimeOffset EarliestKeptInstant(DateTimeOffset now, int days, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var earliestDay = localNow.Date.AddDays(-(Math.Max(1, days) - 1));

        return new DateTimeOffset(earliestDay, zone.GetUtcOffset(earliestDay));
    }

    /// <summary>传给 <c>INewsRepository.PruneExpired</c> 的保留跨度：now − 窗口起点。</summary>
    public static TimeSpan RetentionSpan(DateTimeOffset now, int days, TimeZoneInfo? timeZone = null)
        => now - EarliestKeptInstant(now, days, timeZone);
}
