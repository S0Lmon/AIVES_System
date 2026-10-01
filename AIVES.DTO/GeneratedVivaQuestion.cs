namespace AIVES.DTO;

public sealed class GeneratedVivaQuestion
{
    public string Content { get; set; } = string.Empty;
    public string ExpectedAnswer { get; set; } = string.Empty;
    public string BloomLevel { get; set; } = string.Empty;

    /// <summary>
    /// Assigned by the model when the request did not pin a difficulty. Review only: the bank has
    /// no difficulty column, so this guides the lecturer rather than being stored.
    /// </summary>
    public string Difficulty { get; set; } = string.Empty;

    public List<string> FollowUpQuestions { get; set; } = [];
}