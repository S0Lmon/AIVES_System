namespace AIVES.WebMVC.Services.Gemini;

public sealed class GeneratedVivaQuestion
{
    public string Content { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string BloomLevel { get; set; } = string.Empty;
    public List<string> FollowUpQuestions { get; set; } = [];
}
