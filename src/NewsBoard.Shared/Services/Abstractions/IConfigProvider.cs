namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>配置只读访问 + 变更通知。由 ConfigStore 实现。</summary>
public interface IConfigProvider
{
    AppConfig Current { get; }

    event Action<AppConfig>? Changed;
}
