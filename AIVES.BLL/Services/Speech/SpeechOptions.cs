namespace AIVES.BLL.Services.Speech;

/// <summary>Where a piece of speech work runs.</summary>
public enum SpeechProvider
{
    /// <summary>On this server: Whisper.net for text, sherpa-onnx for voice.</summary>
    Local = 0,
    /// <summary>Gemini, falling back to the local engine when it fails, is rate limited or is too slow.</summary>
    Gemini = 1,
    /// <summary>
    /// Questions only: VieNeu-TTS in its own container (<see cref="SpeechOptions.VieNeuUrl"/>), falling back
    /// to the local voice in the same cases as Gemini.
    /// </summary>
    VieNeu = 2
}

/// <summary>
/// Speech for the AI viva. Whisper.net turns answers into text and sherpa-onnx (Piper voices) reads
/// questions aloud, both offline; models are files under <see cref="ModelsPath"/> that
/// scripts/download-speech-models fetches. With <see cref="Provider"/> set to Gemini, the live preview
/// stays on Whisper and the kept transcript of each answer comes from Gemini's transcription model.
/// A missing model only disables that half, and the viva falls back to typing.
/// </summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Who writes the transcript that is kept. The preview while speaking is always local.</summary>
    public SpeechProvider Provider { get; set; } = SpeechProvider.Local;

    /// <summary>Who reads questions aloud. Local is ~0.4 s a question; Gemini took 8-9 s on 2026-10-08.</summary>
    public SpeechProvider TtsProvider { get; set; } = SpeechProvider.Local;

    /// <summary>
    /// Gemini model for answer transcripts. On 2026-10-08 it got Vietnamese with English terms right
    /// ("ASP.NET Core") where Whisper small did not, in ~5 s a request; the free tier allows 3 a minute.
    /// </summary>
    public string GeminiTranscribeModel { get; set; } = "gemini-3.5-transcribe";

    public string GeminiTtsModel { get; set; } = "gemini-3.8-flash-lite-tts";

    /// <summary>One of Gemini's prebuilt voices (Kore, Puck, Charon, …).</summary>
    public string GeminiVoice { get; set; } = "Kore";

    /// <summary>Longest wait for Gemini before the local result is used instead.</summary>
    public int GeminiTimeoutSeconds { get; set; } = 10;

    public int GeminiTtsTimeoutSeconds { get; set; } = 12;

    /// <summary>Base address of the VieNeu-TTS API, e.g. http://vieneu:8000/. Empty switches it off.</summary>
    public string? VieNeuUrl
    {
        get; set;
    }

    /// <summary>A preset from the server's GET /v1/voices (e.g. "Ngọc Huyền"); empty is the server's default.</summary>
    public string? VieNeuVoice
    {
        get; set;
    }

    /// <summary>Sent as a bearer token when the VieNeu container was started with VIENEU_API_KEY.</summary>
    public string? VieNeuApiKey
    {
        get; set;
    }

    /// <summary>
    /// Longest wait while a candidate is waiting for a question. The answer clock already runs, so a
    /// slow voice costs answer time; past this the local voice reads the question instead. On the
    /// GTX 1070 server a one-sentence follow-up (~3 s of audio) took ~5 s, a long main question ~12 s.
    /// </summary>
    public int VieNeuTimeoutSeconds { get; set; } = 8;

    /// <summary>Longest wait when a question is prepared ahead of the viva, when nobody is waiting.</summary>
    public int VieNeuPrepareTimeoutSeconds { get; set; } = 120;

    /// <summary>How far ahead of a candidate's slot their main questions are read into the cache.</summary>
    public int PrepareMinutesAhead { get; set; } = 120;

    /// <summary>One of 48000, 24000, 16000 or 8000; 24 kHz keeps speech clear at half the size of 48 kHz.</summary>
    public int VieNeuSampleRate { get; set; } = 24_000;

    /// <summary>VieNeu's voices are Vietnamese; by default English exams keep the English Piper voice.</summary>
    public bool VieNeuReadsEnglish
    {
        get; set;
    }

    /// <summary>Folder holding the models; relative paths are under the app's content root.</summary>
    public string ModelsPath { get; set; } = "App_Data/speech-models";

    /// <summary>
    /// Whisper model for the text that is kept. Measured on 2026-10-08 with Vietnamese speech:
    /// ggml-small on a GTX 1070 (Vulkan) handled 4.5 s of audio in ~0.8 s, on CPU in ~5 s;
    /// ggml-base was ~0.4 s on GPU but got most Vietnamese words wrong.
    /// </summary>
    public string WhisperModel { get; set; } = "whisper/ggml-small.bin";

    /// <summary>Optional faster model for the live preview while the candidate is speaking.</summary>
    public string? WhisperPreviewModel
    {
        get; set;
    }

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
