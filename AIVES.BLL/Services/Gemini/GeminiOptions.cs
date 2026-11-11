namespace AIVES.BLL.Services.Gemini;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-3.5-flash-lite";
}
