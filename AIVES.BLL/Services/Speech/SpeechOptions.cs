namespace AIVES.BLL.Services.Speech;

/// <summary>
/// Offline speech for the AI viva: Whisper.net turns answers into text and sherpa-onnx (Piper voices)
/// reads questions aloud. Models are files under <see cref="ModelsPath"/>; scripts/download-speech-models
/// fetches them. A missing model only disables that half, and the viva falls back to typing.
/// </summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Folder holding the models; relative paths are under the app's content root.</summary>
    public string ModelsPath { get; set; } = "App_Data/speech-models";

    /// <summary>
    /// Whisper model for the text that is kept. Measured on 2026-10-08 with Vietnamese speech:
    /// ggml-small on a GTX 1070 (Vulkan) handled 4.5 s of audio in ~0.8 s, on CPU in ~5 s;
    /// ggml-base was ~0.4 s on GPU but got most Vietnamese words wrong.
    /// </summary>
    public string WhisperModel { get; set; } = "whisper/ggml-small.bin";

    /// <summary>Optional faster model for the live preview while the candidate is speaking.</summary>
    public string? WhisperPreviewModel { get; set; }

    /// <summary>Use the GPU (Vulkan) when there is one; Whisper.net falls back to the CPU otherwise.</summary>
    public bool UseGpu { get; set; } = true;

    public int Threads { get; set; } = 8;

    /// <summary>Transcriptions allowed at once; more candidates queue rather than overload the GPU.</summary>
    public int MaxConcurrentTranscriptions { get; set; } = 2;

    /// <summary>Piper voice folders (each with an .onnx model, tokens.txt and espeak-ng-data).</summary>
    public string VietnameseVoice { get; set; } = "tts/vits-piper-vi_VN-vais1000-medium";

    public string EnglishVoice { get; set; } = "tts/vits-piper-en_US-amy-low";

    /// <summary>New audio needed before the live preview is refreshed.</summary>
    public double PreviewEverySeconds { get; set; } = 1.5;

    /// <summary>
    /// Once the unconfirmed tail is this long it is transcribed for good at a quiet point, so the
    /// final transcription after the candidate stops only covers the last few seconds.
    /// </summary>
    public double CommitAfterSeconds { get; set; } = 8;

    /// <summary>Upper bound for one committed window when nobody pauses.</summary>
    public double MaxWindowSeconds { get; set; } = 15;
}
