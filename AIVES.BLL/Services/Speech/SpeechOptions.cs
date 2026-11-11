namespace AIVES.BLL.Services.Speech;

/// <summary>Who reads questions aloud.</summary>
public enum SpeechProvider
{
    /// <summary>On this server with a sherpa-onnx (Piper) voice.</summary>
    Local = 0,
    /// <summary>Gemini, falling back to the local voice when it fails, is rate limited or is too slow.</summary>
    Gemini = 1,
    /// <summary>
    /// VieNeu-TTS in its own container (<see cref="SpeechOptions.VieNeuUrl"/>), falling back to the
    /// local Piper voice in the same cases as Gemini.
    /// </summary>
    VieNeu = 2
}

/// <summary>
/// Spoken questions for the AI viva. With <see cref="TtsProvider"/> set to VieNeu (and <see cref="VieNeuUrl"/>
/// set) Vietnamese questions are read by a VieNeu-TTS server, with Gemini by Gemini's TTS model, and
/// otherwise, or when the remote voice fails, by an offline sherpa-onnx (Piper) voice whose models are
/// files under <see cref="ModelsPath"/> (scripts/download-speech-models). Candidates answer by browser
/// speech recognition or by typing; no audio is recognised on the server.
/// </summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Who reads questions aloud. Local is ~0.4 s a question; Gemini took 8-9 s on 2026-10-08.</summary>
    public SpeechProvider TtsProvider { get; set; } = SpeechProvider.Local;

    public string GeminiTtsModel { get; set; } = "gemini-3.5-flash-lite-tts";

    /// <summary>One of Gemini's prebuilt voices (Kore, Puck, Charon, …).</summary>
    public string GeminiVoice { get; set; } = "Kore";

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

    /// <summary>Piper voice folders (each with an .onnx model, tokens.txt and espeak-ng-data).</summary>
    public string VietnameseVoice { get; set; } = "tts/vits-piper-vi_VN-vais1000-medium";

    public string EnglishVoice { get; set; } = "tts/vits-piper-en_US-amy-low";
}

internal static class SpeechPaths
{
    public static string Root(SpeechOptions options, Microsoft.Extensions.Hosting.IHostEnvironment environment) =>
        Path.IsPathRooted(options.ModelsPath) ? options.ModelsPath : Path.Combine(environment.ContentRootPath, options.ModelsPath);
}
