namespace AIVES.BLL.Services.Speech;

/// <summary>Who reads questions aloud when VieNeu is not used.</summary>
public enum SpeechProvider
{
    /// <summary>On this server with a sherpa-onnx (Piper) voice.</summary>
    Local = 0,
    /// <summary>Gemini, falling back to the local voice when it fails, is rate limited or is too slow.</summary>
    Gemini = 1
}

/// <summary>
/// Spoken questions for the AI viva. Vietnamese questions are read by a VieNeu-TTS server when
/// <see cref="VieNeuUrl"/> is set, then by Gemini when <see cref="TtsProvider"/> asks for it, and
/// otherwise by an offline sherpa-onnx (Piper) voice whose models are files under
/// <see cref="ModelsPath"/> (scripts/download-speech-models). Candidates answer by browser speech
/// recognition or by typing; no audio is recognised on the server.
/// </summary>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>Who reads questions aloud after VieNeu. Local is ~0.4 s a question; Gemini took 8-9 s on 2026-10-08.</summary>
    public SpeechProvider TtsProvider { get; set; } = SpeechProvider.Local;

    public string GeminiTtsModel { get; set; } = "gemini-3.8-flash-lite-tts";

    /// <summary>One of Gemini's prebuilt voices (Kore, Puck, Charon, …).</summary>
    public string GeminiVoice { get; set; } = "Kore";

    public int GeminiTtsTimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// Base address of a VieNeu-TTS server (its OpenAI-compatible <c>POST /v1/audio/speech</c>), for example
    /// <c>http://localhost:8800</c>. Empty leaves it off. When set, Vietnamese questions are read by VieNeu first;
    /// if it is down or slow the next engine (Gemini, then the local voice) reads them instead.
    /// </summary>
    public string? VieNeuUrl
    {
        get; set;
    }

    public string VieNeuModel { get; set; } = "vieneu-v3-turbo";

    /// <summary>One of the server's preset voices (<c>GET /v1/voices</c>).</summary>
    public string VieNeuVoice { get; set; } = "Mai Anh";

    public string? VieNeuApiKey
    {
        get; set;
    }

    public int VieNeuTimeoutSeconds { get; set; } = 20;

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
