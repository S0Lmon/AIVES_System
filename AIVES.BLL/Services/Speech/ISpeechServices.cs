using AIVES.DTO;

namespace AIVES.BLL.Services.Speech;

/// <summary>Text-to-speech that returns a complete WAV file.</summary>
public interface ITextToSpeech
{
    bool IsAvailable(AppLanguage language);

    bool IsAvailable(AppLanguage language, string? preferredVoiceName) => IsAvailable(language);

    bool UsesOnlineService(AppLanguage language, string? preferredVoiceName) => false;

    /// <param name="speed">1 is the voice's normal pace; the admin's speech rate is passed through.</param>
    Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default);

    Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, string? preferredVoiceName, CancellationToken cancellationToken = default) =>
        SynthesizeWavAsync(text, language, speed, cancellationToken);

    async Task<SpeechAudio> SynthesizeAsync(
        string text,
        AppLanguage language,
        double speed,
        string? preferredVoiceName = null,
        bool useCache = true,
        CancellationToken cancellationToken = default) =>
        new(await SynthesizeWavAsync(text, language, speed, preferredVoiceName, cancellationToken), "audio/wav");
}

public sealed record SpeechAudio(byte[] Data, string ContentType);
