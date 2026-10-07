using AIVES.DTO;

namespace AIVES.BLL.Services.Speech;

/// <summary>Speech-to-text over 16 kHz mono samples in the range [-1, 1].</summary>
public interface ISpeechToText
{
    bool IsAvailable { get; }

    /// <param name="preview">True for the live preview, which may use a faster model.</param>
    /// <param name="prompt">Words to bias recognition towards: the question and the subject's terms.</param>
    Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, AppLanguage language, string? prompt, bool preview,
        CancellationToken cancellationToken = default);
}

/// <summary>Text-to-speech that returns a complete WAV file.</summary>
public interface ITextToSpeech
{
    bool IsAvailable(AppLanguage language);

    /// <param name="speed">1 is the voice's normal pace; the admin's speech rate is passed through.</param>
    Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default);
}
