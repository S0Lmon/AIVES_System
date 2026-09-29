namespace AIVES.WebMVC.Services.Gemini;

public interface IGeminiQuestionGenerator
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(
        string subject,
        string topic,
        string? learningOutcomes,
        string difficulty,
        int count,
        CancellationToken cancellationToken = default);
}
