namespace AIVES.BLL.Services.Ai;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base address of the local Ollama server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "phi3:mini";

    /// <summary>Lets Ollama act as a fallback when Gemini is unavailable.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Upper bound on the prompt budget spent on retrieved material.</summary>
    public int MaxContextCharacters { get; set; } = 6000;

    public int TimeoutSeconds { get; set; } = 180;
}