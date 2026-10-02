namespace AIVES.BLL.Services.Interview;

public sealed class InterviewOptions
{
    public const string SectionName = "Interview";

    /// <summary>
    /// Gemini model for follow-up decisions. A "lite" model keeps the pause after an answer to about
    /// a second; measured on 2026-10-03, gemini-3.5-flash-lite answered in ~1.1 s while
    /// gemini-3.5-flash took 9-16 s and was often overloaded (503).
    /// </summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";

    /// <summary>Longest wait for the AI before carrying on without a follow-up.</summary>
    public int AiTimeoutSeconds { get; set; } = 12;

    /// <summary>Allowance past the answer time limit for network and speech-recognition delay.</summary>
    public int AnswerGraceSeconds { get; set; } = 15;
}
