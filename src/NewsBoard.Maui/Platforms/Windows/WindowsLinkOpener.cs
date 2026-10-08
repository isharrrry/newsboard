using System.Diagnostics;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>Windows：用系统默认浏览器打开原文链接。</summary>
public sealed class WindowsLinkOpener : ILinkOpener
{
    public Task<bool> OpenAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(false);
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }
}
