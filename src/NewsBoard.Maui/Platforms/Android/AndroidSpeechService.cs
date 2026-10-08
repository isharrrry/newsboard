using Android.OS;
using Android.Speech.Tts;
using Java.Util;
using Microsoft.Maui.ApplicationModel;
using NewsBoard.Shared.Services.Abstractions;

namespace NewsBoard.Maui;

/// <summary>
/// 原生 Android TTS 封装。SpeakAsync 返回的 Task 在朗读自然结束时完成，
/// 这就是「朗读结束 = 该条播放结束」的时钟（plan D5 / §5.2）。
/// </summary>
public sealed class AndroidSpeechService : ISpeechService, IDisposable
{
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly object _gate = new();
    private TextToSpeech? _engine;
    private bool _initFailed;
    private TaskCompletionSource<bool>? _activeUtterance;
    private int _utteranceCounter;

    public async Task<bool> IsAvailableAsync(string language)
    {
        var engine = await GetEngineAsync().ConfigureAwait(false);
        if (engine is null)
        {
            return false;
        }

        var result = engine.IsLanguageAvailable(ToLocale(language));
        return result is not (LanguageAvailableResult.MissingData or LanguageAvailableResult.NotSupported);
    }

    public async Task SpeakAsync(string text, double rate, double pitch, string language, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var engine = await GetEngineAsync().ConfigureAwait(false);
        if (engine is null)
        {
            throw new InvalidOperationException("系统语音引擎不可用");
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var utteranceId = $"nb-{Interlocked.Increment(ref _utteranceCounter)}";

        lock (_gate)
        {
            _activeUtterance?.TrySetResult(false);
            _activeUtterance = completion;
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                engine.Stop();
            }
            catch (Exception)
            {
                // 停止失败不影响取消语义
            }

            completion.TrySetCanceled(cancellationToken);
        });

        engine.SetSpeechRate((float)Math.Clamp(rate, 0.1, 3.0));
        engine.SetPitch((float)Math.Clamp(pitch, 0.1, 3.0));
        engine.SetLanguage(ToLocale(language));

        var started = engine.Speak(text, QueueMode.Flush, null, utteranceId);
        if (started is not OperationResult.Success)
        {
            ClearActive(completion);
            throw new InvalidOperationException($"语音合成启动失败：{started}");
        }

        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            ClearActive(completion);
        }
    }

    public async Task StopAsync()
    {
        TaskCompletionSource<bool>? pending;
        lock (_gate)
        {
            pending = _activeUtterance;
            _activeUtterance = null;
        }

        if (_engine is { } engine)
        {
            try
            {
                engine.Stop();
            }
            catch (Exception)
            {
                // 引擎可能已释放
            }
        }

        pending?.TrySetResult(false);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void ClearActive(TaskCompletionSource<bool> completion)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeUtterance, completion))
            {
                _activeUtterance = null;
            }
        }
    }

    private void CompleteUtterance(string? utteranceId, bool success)
    {
        TaskCompletionSource<bool>? pending;
        lock (_gate)
        {
            pending = _activeUtterance;
            _activeUtterance = null;
        }

        pending?.TrySetResult(success);
    }

    private async Task<TextToSpeech?> GetEngineAsync()
    {
        if (_engine is { } ready)
        {
            return ready;
        }

        if (_initFailed)
        {
            return null;
        }

        await _initGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_engine is { } existing)
            {
                return existing;
            }

            if (_initFailed)
            {
                return null;
            }

            var initSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TextToSpeech? created = null;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                created = new TextToSpeech(Android.App.Application.Context, new OnInitListener(ok => initSignal.TrySetResult(ok)));
                created.SetOnUtteranceProgressListener(new UtteranceListener(this));
            }).ConfigureAwait(false);

            var completed = await Task.WhenAny(initSignal.Task, Task.Delay(InitTimeout)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, initSignal.Task) || !initSignal.Task.Result)
            {
                _initFailed = true;
                created?.Shutdown();
                created?.Dispose();
                return null;
            }

            _engine = created;
            return _engine;
        }
        finally
        {
            _initGate.Release();
        }
    }

    private static Locale ToLocale(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return Locale.Default!;
        }

        var normalized = language.Replace('_', '-');
        var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 2 => new Locale(parts[0], parts[1]),
            1 => new Locale(parts[0]),
            _ => Locale.Default!,
        };
    }

    public void Dispose()
    {
        var engine = _engine;
        _engine = null;

        if (engine is not null)
        {
            try
            {
                engine.Stop();
                engine.Shutdown();
            }
            catch (Exception)
            {
                // 释放期间的异常忽略
            }

            engine.Dispose();
        }

        _initGate.Dispose();
    }

    private sealed class OnInitListener : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        private readonly Action<bool> _onInit;

        public OnInitListener(Action<bool> onInit) => _onInit = onInit;

        public void OnInit(OperationResult status) => _onInit(status == OperationResult.Success);
    }

    private sealed class UtteranceListener : UtteranceProgressListener
    {
        private readonly AndroidSpeechService _owner;

        public UtteranceListener(AndroidSpeechService owner) => _owner = owner;

        public override void OnStart(string? utteranceId)
        {
        }

        public override void OnDone(string? utteranceId) => _owner.CompleteUtterance(utteranceId, true);

        public override void OnError(string? utteranceId) => _owner.CompleteUtterance(utteranceId, false);
    }
}
