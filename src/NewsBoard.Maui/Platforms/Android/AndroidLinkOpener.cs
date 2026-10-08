using Android.App;
using Android.Content;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>Android：用系统浏览器（ACTION_VIEW）打开原文链接。</summary>
public sealed class AndroidLinkOpener : ILinkOpener
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
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(uri.AbsoluteUri));
            intent.AddFlags(ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }
}
