using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Shared.Services;

/// <summary>
/// 配置持久化：AppConfig 以 JSON 落平台 AppData，API Key 只走 ISecretStore（永不写入 JSON）。
/// 读取宽容：旧 JSON 缺字段用默认值补齐，字段类型不对或整个文件损坏都不抛异常。
/// </summary>
public sealed class ConfigStore : IConfigProvider
{
    /// <summary>API Key 在 ISecretStore 中的键名。</summary>
    public const string ApiKeySecretName = "deepseek-api-key";

    private const string FileName = "config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ISecretStore _secretStore;
    private readonly object _gate = new();

    public ConfigStore(ISecretStore secretStore, string? directory = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        StorageDirectory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewsBoard")
            : directory;
        EnsureDirectory();
        Current = new AppConfig();
    }

    /// <summary>config.json 所在目录，构造时已确保存在。</summary>
    public string StorageDirectory { get; }

    public AppConfig Current { get; private set; }

    public event Action<AppConfig>? Changed;

    /// <summary>读盘并注入 API Key。文件缺失/损坏时得到全默认值，不抛异常。</summary>
    public async Task LoadAsync()
    {
        AppConfig loaded;
        lock (_gate)
        {
            loaded = ReadFromDisk();
        }

        loaded.ApiKey = await _secretStore.GetAsync(ApiKeySecretName).ConfigureAwait(false);

        lock (_gate)
        {
            Current = loaded;
        }
    }

    /// <summary>原子写盘并更新 Current。ApiKey 非空时同时写入安全存储。</summary>
    public async Task SaveAsync(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var apiKey = config.ApiKey;
        AppConfig snapshot;
        lock (_gate)
        {
            snapshot = config.Clone();
            snapshot.NormalizeKeywords();
            snapshot.RssFallbackTemplates = NormalizeTemplates(snapshot.RssFallbackTemplates);
            snapshot.ApiKey = string.IsNullOrWhiteSpace(apiKey) ? Current.ApiKey : apiKey.Trim();
            WriteToDisk(snapshot);
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await _secretStore.SetAsync(ApiKeySecretName, apiKey.Trim()).ConfigureAwait(false);
        }

        lock (_gate)
        {
            Current = snapshot;
        }

        Changed?.Invoke(snapshot);
    }

    public Task<string?> GetApiKeyAsync() => _secretStore.GetAsync(ApiKeySecretName);

    /// <summary>写入 API Key（空串 = 清除）。同步更新 <see cref="Current"/> 并触发 <see cref="Changed"/>。</summary>
    public async Task SetApiKeyAsync(string value)
    {
        var key = value?.Trim() ?? "";
        if (key.Length == 0)
        {
            await _secretStore.RemoveAsync(ApiKeySecretName).ConfigureAwait(false);
            lock (_gate)
            {
                Current.ApiKey = null;
            }
        }
        else
        {
            await _secretStore.SetAsync(ApiKeySecretName, key).ConfigureAwait(false);
            lock (_gate)
            {
                Current.ApiKey = key;
            }
        }

        Changed?.Invoke(Current);
    }

    private string ConfigPath => Path.Combine(StorageDirectory, FileName);

    private void EnsureDirectory()
    {
        try
        {
            Directory.CreateDirectory(StorageDirectory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteToDisk(AppConfig config)
    {
        EnsureDirectory();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    private AppConfig ReadFromDisk()
    {
        string json;
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            json = File.ReadAllText(ConfigPath);
        }
        catch (IOException)
        {
            return new AppConfig();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppConfig();
        }

        return ParseTolerant(json);
    }

    /// <summary>先建默认实例，再按属性名逐个覆盖；任何单字段失败都保留默认值。</summary>
    private static AppConfig ParseTolerant(string json)
    {
        var config = new AppConfig();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return config;
        }

        if (root is not JsonObject obj)
        {
            return config;
        }

        foreach (var property in typeof(AppConfig).GetProperties())
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var node = FindIgnoreCase(obj, property.Name);
            if (node is null)
            {
                continue;
            }

            try
            {
                var value = node.Deserialize(property.PropertyType, JsonOptions);
                if (value is null)
                {
                    continue;
                }

                property.SetValue(config, value);
            }
            catch (JsonException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (ArgumentException)
            {
            }
        }

        config.NormalizeKeywords();
        config.RssFallbackTemplates = NormalizeTemplates(config.RssFallbackTemplates);
        return config;
    }

    /// <summary>
    /// 备用 RSS 源规整：去空、去首尾空格、去重，并换用新列表，避免与调用方（或 Current）共享引用。
    /// </summary>
    private static List<string> NormalizeTemplates(List<string>? source)
        => (source ?? [])
            .Select(t => t?.Trim() ?? "")
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

    private static JsonNode? FindIgnoreCase(JsonObject obj, string name)
    {
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
