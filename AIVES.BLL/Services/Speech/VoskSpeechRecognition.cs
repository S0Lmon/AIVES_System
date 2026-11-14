using AIVES.DTO;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Vosk;

namespace AIVES.BLL.Services.Speech;

public interface IVoskSpeechRecognition
{
    bool IsAvailable(AppLanguage language);
    VoskSpeechRecognitionSession CreateSession(
        AppLanguage language,
        Func<SpeechRecognitionUpdate, Task> publish,
        ILogger<VoskSpeechRecognitionSession> logger);
}

public sealed class VoskSpeechRecognition(string modelRoot) : IVoskSpeechRecognition, IDisposable
{
    private readonly ConcurrentDictionary<AppLanguage, Lazy<Model>> models = new();

    public bool IsAvailable(AppLanguage language)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            return false;
        var path = GetModelPath(language);
        return File.Exists(Path.Combine(path, "conf", "model.conf"))
            && File.Exists(Path.Combine(path, "am", "final.mdl"));
    }

    public VoskSpeechRecognitionSession CreateSession(
        AppLanguage language,
        Func<SpeechRecognitionUpdate, Task> publish,
        ILogger<VoskSpeechRecognitionSession> logger)
    {
        if (!IsAvailable(language))
            throw new InvalidOperationException($"The local Vosk speech model for {language} is not installed.");

        var model = models.GetOrAdd(language, selected => new Lazy<Model>(
            () => new Model(GetModelPath(selected)),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return new VoskSpeechRecognitionSession(model, publish, logger);
    }

    private string GetModelPath(AppLanguage language) =>
        Path.Combine(modelRoot, language switch
        {
            AppLanguage.Vi => "vi-VN",
            AppLanguage.En => "en-US",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        });

    public void Dispose()
    {
        foreach (var model in models.Values)
        {
            if (model.IsValueCreated)
                model.Value.Dispose();
        }
    }
}
