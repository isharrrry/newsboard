namespace NewsBoard.Shared.Services.Abstractions;

/// <summary>系统 TTS。SpeakAsync 的 Task 完成 = 朗读结束（该条播放结束的时钟）。</summary>
public interface ISpeechService
{
    /// <summary>探测指定语言的语音包是否可用。</summary>
    Task<bool> IsAvailableAsync(string language);

    /// <summary>朗读文本；返回的 Task 在朗读自然结束时完成。取消令牌触发即停止朗读。</summary>
    Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default);

    /// <summary>立即停止当前朗读。</summary>
    Task StopAsync();
}
