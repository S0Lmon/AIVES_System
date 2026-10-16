using System;
using System.IO;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;
using AIVES.DTO;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Speech;

public sealed class SystemSpeechToText : ISpeechToText
{
    private readonly ILogger<SystemSpeechToText> logger;

    public SystemSpeechToText(ILogger<SystemSpeechToText> logger)
    {
        this.logger = logger;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public async Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, AppLanguage language, string? prompt, bool preview,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return string.Empty;
        if (!Audio.HasSpeech(samples.Span))
            return string.Empty;

        using var wav = new MemoryStream(Audio.ToWav(samples.Span, Audio.SampleRate));
        wav.Position = 0;

        using var recognizer = new SpeechRecognitionEngine(language.ToCultureCode());
        var resultTask = new TaskCompletionSource<string>();
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.SpeechRecognized += (_, e) =>
        {
            if (e.Result?.Text is string text && !string.IsNullOrEmpty(text))
                resultTask.TrySetResult(text);
        };
        recognizer.RecognizeCompleted += (_, _) =>
        {
            if (!resultTask.Task.IsCompleted)
                resultTask.TrySetResult(string.Empty);
        };

        try
        {
            recognizer.SetInputToWaveStream(wav);
            recognizer.RecognizeAsync(RecognizeMode.Single);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            var completed = await Task.WhenAny(resultTask.Task, timeoutTask);
            recognizer.RecognizeAsyncCancel();
            if (completed == resultTask.Task)
                return WhisperSpeechToText.Clean(await resultTask.Task);
            return string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "System.Speech recognition failed");
            return string.Empty;
        }
    }
}
