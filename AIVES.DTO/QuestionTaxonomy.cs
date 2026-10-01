namespace AIVES.DTO;

/// <summary>
/// The Bloom levels a question may carry. Mirrors the <c>BloomLevels</c> table, which is the only
/// place a level becomes an id, so generators and the UI agree on one list.
/// </summary>
public static class BloomLevels
{
    public static readonly IReadOnlyList<string> All =
        ["Remember", "Understand", "Apply", "Analyze", "Evaluate", "Create"];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value) && All.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static string? Normalize(string? value) =>
        IsAllowed(value) ? All.First(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase)) : null;
}

/// <summary>
/// Difficulty is a generation hint only: it shapes the wording of a question and is shown in the
/// review, but the bank stores nothing for it. <see cref="Ordered"/> runs easiest to hardest so a
/// range can be compared and spread numerically.
/// </summary>
public static class QuestionDifficulties
{
    public static readonly IReadOnlyList<string> Ordered = ["Basic", "Intermediate", "Advanced"];

    public static bool IsAllowed(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Ordered.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Position in <see cref="Ordered"/>, or -1 when the value is blank or unknown.</summary>
    public static int IndexOf(string? value) =>
        IsAllowed(value) ? Ordered.ToList().FindIndex(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase)) : -1;

    public static string? Normalize(string? value) =>
        IsAllowed(value) ? Ordered[IndexOf(value)] : null;
}