namespace AIVES.BLL.Services.Ai;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base address of the local Ollama server.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model for every AI feature unless <see cref="InterviewModel"/> or <see cref="GradingModel"/> overrides it.</summary>
    public string Model { get; set; } = "phi3:mini";

    /// <summary>Lets Ollama act as a fallback when Gemini is unavailable.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Use Ollama first for every AI feature (questions, rubrics, viva follow-ups, grading), with
    /// Gemini only as the fallback. Off: Gemini first, Ollama when Gemini has no key or fails.
    /// </summary>
    public bool PreferLocal
    {
        get; set;
    }

    /// <summary>
    /// Optional model for viva follow-ups. These are asked while the candidate waits
    /// (<c>Interview:AiTimeoutSeconds</c>), so a smaller, faster model can be chosen here. Empty uses <see cref="Model"/>.
    /// </summary>
    public string? InterviewModel
    {
        get; set;
    }

    /// <summary>Optional model for grading suggestions (not live, so a large model is fine). Empty uses <see cref="Model"/>.</summary>
    public string? GradingModel
    {
        get; set;
    }

    /// <summary>Upper bound on the prompt budget spent on retrieved material.</summary>
    public int MaxContextCharacters { get; set; } = 6000;

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Context window (num_ctx) for every call. One value for all features on purpose: Ollama reloads the
    /// model whenever num_ctx changes, which costs tens of seconds for a large model.
    /// </summary>
    public int ContextTokens { get; set; } = 8192;

    /// <summary>How long Ollama keeps the model loaded after a call, so the next viva answer does not wait for a reload.</summary>
    public string KeepAlive { get; set; } = "30m";

    public string InterviewModelName => string.IsNullOrWhiteSpace(InterviewModel) ? Model : InterviewModel.Trim();

    public string GradingModelName => string.IsNullOrWhiteSpace(GradingModel) ? Model : GradingModel.Trim();

    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}
