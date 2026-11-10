using AIVES.DTO;

namespace AIVES.BLL.Services.Speech;

/// <summary>Text-to-speech that returns a complete WAV file.</summary>
public interface ITextToSpeech
{
    bool IsAvailable(AppLanguage language);

    /// <param name="speed">1 is the voice's normal pace; the admin's speech rate is passed through.</param>
    Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default);
}
