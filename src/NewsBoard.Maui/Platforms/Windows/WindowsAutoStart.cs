using System.Diagnostics;
using Microsoft.Win32;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>Windows 开机自启：HKCU\...\Run 写一个值（plan §3.4）。</summary>
public sealed class WindowsAutoStart : IAutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NewsBoard";

    public bool IsSupported => true;

    public Task<bool> IsEnabledAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return Task.FromResult(key?.GetValue(ValueName) is string value && value.Length > 0);
    }

    public Task SetEnabledAsync(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null)
        {
            return Task.CompletedTask;
        }

        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                key.SetValue(ValueName, $"\"{exe}\"");
            }
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        return Task.CompletedTask;
    }

    public Task<bool> OpenSystemSettingsAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }
}
