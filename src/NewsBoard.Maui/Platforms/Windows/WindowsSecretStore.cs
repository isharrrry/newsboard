using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>
/// Windows 密钥存储：DPAPI（CurrentUser）加密后落盘，磁盘上不出现明文。
/// 与 WPF 宿主共用同一份文件（%APPDATA%\NewsBoard\secret.dat），两端密钥互通。
/// </summary>
public sealed class WindowsSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NewsBoard.SecretStore.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public WindowsSecretStore(string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NewsBoard");
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "secret.dat");
    }

    public async Task<string?> GetAsync(string key)
    {
        var store = await ReadAsync().ConfigureAwait(false);
        return store.TryGetValue(key, out var value) ? value : null;
    }

    public async Task SetAsync(string key, string value)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await ReadUnlockedAsync().ConfigureAwait(false);
            store[key] = value;
            WriteUnlocked(store);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string key)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var store = await ReadUnlockedAsync().ConfigureAwait(false);
            if (store.Remove(key))
            {
                WriteUnlocked(store);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ReadUnlockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadUnlockedAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_filePath).ConfigureAwait(false);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            // 文件损坏或换了用户账户导致无法解密：视为空存储，不阻断启动
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void WriteUnlocked(Dictionary<string, string> store)
    {
        var json = JsonSerializer.Serialize(store);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

        var tempPath = _filePath + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
