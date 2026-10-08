namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>用系统默认浏览器打开外部链接（原文地址）。</summary>
public interface ILinkOpener
{
    /// <summary>打开链接；返回是否成功交给系统处理。</summary>
    Task<bool> OpenAsync(string url);
}
