namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>平台安全存储（Android SecureStorage / Windows DPAPI）。API Key 只走这里。</summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}
