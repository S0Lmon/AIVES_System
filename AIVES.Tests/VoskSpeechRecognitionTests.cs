using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIVES.Tests;

public sealed class VoskSpeechRecognitionTests
{
    [Fact]
    public void MissingModelsAreReportedUnavailable()
    {
        using var recognition = new VoskSpeechRecognition(
            Path.Combine(Path.GetTempPath(), "aives-missing-vosk-models-" + Guid.NewGuid().ToString("N")));

        Assert.False(recognition.IsAvailable(AppLanguage.Vi));
        Assert.False(recognition.IsAvailable(AppLanguage.En));
    }

    [Fact]
    public async Task InstalledModelsLoadAndAcceptStreamingPcm()
    {
        var modelRoot = Path.Combine(FindRepositoryRoot(), "AIVES.WebRazor", "App_Data", "speech-models");
        using var recognition = new VoskSpeechRecognition(modelRoot);
        if (!recognition.IsAvailable(AppLanguage.Vi) || !recognition.IsAvailable(AppLanguage.En))
            return;

        foreach (var language in new[] { AppLanguage.Vi, AppLanguage.En })
        {
            var updates = new List<SpeechRecognitionUpdate>();
            await using var session = recognition.CreateSession(
                language,
                update =>
                {
                    updates.Add(update);
                    return Task.CompletedTask;
                },
                NullLogger<VoskSpeechRecognitionSession>.Instance);
            session.WriteAudio(new byte[32000]);
            await session.StopAsync();
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "AIVES.WebRazor")))
                return directory.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
