namespace NewsBoard.Shared.Services;

/// <summary>
/// 应用数据目录（配置 + 新闻缓存）的唯一解析点。
/// 环境变量 <see cref="OverrideVariableName"/> 非空时优先使用它：自动化验证、探针脚本可以借此把数据写进隔离目录，
/// 不再直接覆盖真实用户目录（曾因此把用户配置里 6 个关键字覆盖成 2 个，见 AGENTS.md）。
/// </summary>
public static class NewsBoardDataDirectory
{
    /// <summary>数据目录覆盖变量名。</summary>
    public const string OverrideVariableName = "NEWSBOARD_DATA_DIR";

    /// <summary>解析数据目录：环境变量优先，否则平台本地 AppData 下的 NewsBoard。</summary>
    public static string Resolve()
    {
        var overridden = Environment.GetEnvironmentVariable(OverrideVariableName);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NewsBoard");
    }
}
