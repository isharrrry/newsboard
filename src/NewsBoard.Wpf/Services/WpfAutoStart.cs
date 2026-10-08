using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace NewsBoard.Wpf.Services;

/// <summary>
/// Windows 开机自启（plan §3.4）：在 HKCU\...\CurrentVersion\Run 写入一个值，
/// 只在当前用户生效，不需要管理员权限。
/// </summary>
public sealed class WpfAutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NewsBoard";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName, BuildCommandLine(), RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>把当前进程还原成一条可直接执行的命令行（dotnet run 场景要带上 dll）。</summary>
    private static string BuildCommandLine()
    {
        var processPath = Environment.ProcessPath ?? "";
        var assemblyPath = Environment.ProcessPath is not null
            ? System.Reflection.Assembly.GetEntryAssembly()?.Location
            : null;

        var isDotnetHost = Path.GetFileNameWithoutExtension(processPath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        if (isDotnetHost && !string.IsNullOrEmpty(assemblyPath))
        {
            return $"\"{processPath}\" \"{assemblyPath}\"";
        }

        if (!string.IsNullOrEmpty(processPath))
        {
            return $"\"{processPath}\"";
        }

        var fallback = Process.GetCurrentProcess().MainModule?.FileName ?? "NewsBoard.Wpf.exe";
        return $"\"{fallback}\"";
    }
}
