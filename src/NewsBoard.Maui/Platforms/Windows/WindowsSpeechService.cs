using System.Speech.Synthesis;
using System.Text;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>
/// Windows 系统 TTS（System.Speech）。SpeakAsync 返回的 Task 在朗读自然结束时完成 ——
/// 它是播放引擎的切条时钟。SpeechSynthesizer 非线程安全，用信号量串行化。
/// </summary>
public sealed class WindowsSpeechService : ISpeechService, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public Task<bool> IsAvailableAsync(string language) => Task.FromResult(FindVoice(language) is not null);

    public async Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var voice = FindVoice(language);
            if (voice is not null)
            {
                _synth.SelectVoice(voice.Name);
            }

            _synth.Rate = MapRate(rate);

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<SpeakCompletedEventArgs> handler = (_, e) =>
            {
                if (e.Cancelled)
                {
                    completion.TrySetCanceled();
                }
                else if (e.Error is not null)
                {
                    completion.TrySetException(e.Error);
                }
                else
                {
                    completion.TrySetResult();
                }
            };

            _synth.SpeakCompleted += handler;
            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    _synth.SpeakAsyncCancelAll();
                }
                catch (Exception)
                {
                }
            });

            try
            {
                try
                {
                    _synth.SpeakSsmlAsync(BuildSsml(text, language, pitch));
                }
                catch (Exception)
                {
                    // 个别语音包不接受 SSML prosody：退化为纯文本朗读
                    _synth.SpeakAsync(text);
                }

                await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                _synth.SpeakCompleted -= handler;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        try
        {
            _synth.SpeakAsyncCancelAll();
        }
        catch (Exception)
        {
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _synth.SpeakAsyncCancelAll();
        }
        catch (Exception)
        {
        }

        _synth.Dispose();
        _gate.Dispose();
    }

    private VoiceInfo? FindVoice(string language)
    {
        var enabled = _synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => v.VoiceInfo)
            .ToList();

        if (enabled.Count == 0)
        {
            return null;
        }

        var exact = enabled.FirstOrDefault(v => string.Equals(v.Culture.Name, language, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var prefix = language.Split('-')[0];
        return enabled.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName.Equals(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static int MapRate(double rate) => Math.Clamp((int)Math.Round((rate - 1.0) * 10.0), -10, 10);

    private static string BuildSsml(string text, string language, double pitch)
    {
        var percent = (int)Math.Round((pitch - 1.0) * 100.0);
        var sign = percent >= 0 ? "+" : "";

        var builder = new StringBuilder();
        builder.Append("<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"")
               .Append(language)
               .Append("\">");
        builder.Append("<prosody pitch=\"").Append(sign).Append(percent).Append("%\">");
        builder.Append(EscapeXml(text));
        builder.Append("</prosody></speak>");
        return builder.ToString();
    }

    private static string EscapeXml(string text)
        => text.Replace("&", "&amp;")
               .Replace("<", "&lt;")
               .Replace(">", "&gt;")
               .Replace("\"", "&quot;")
               .Replace("'", "&apos;");
}
