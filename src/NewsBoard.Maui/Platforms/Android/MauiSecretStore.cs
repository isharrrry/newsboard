using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>Android Keystore 加密的安全存储。API Key 只走这里，永不落 JSON。</summary>
public sealed class MauiSecretStore : ISecretStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception)
        {
            // 密钥库被清除/损坏时按「未配置」处理
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            SecureStorage.Default.Remove(key);
            return;
        }

        await SecureStorage.Default.SetAsync(key, value);
    }

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}
