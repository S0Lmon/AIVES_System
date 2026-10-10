using AIVES.DTO;
using System.Text.RegularExpressions;

namespace AIVES.BLL.Services.Grading;

/// <summary>
/// Secondary signals from the recorded turns: response time, answer length, speaking pace and filler
/// words. They are context for the lecturer, not part of the score.
/// </summary>
public static partial class AnswerSignalCalculator
{
    // Hesitation sounds as speech recognition writes them, Vietnamese and English.
    private static readonly HashSet<string> Fillers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ờ", "ờm", "ừm", "ưm", "ơ", "hừm", "um", "uh", "uhm", "hmm", "erm"
    };

    public static AnswerSignals Compute(IReadOnlyList<InterviewTurnRecordDto> turns)
    {
        var asked = turns.Where(turn => turn.AnsweredAtUtc is not null).ToList();
        if (asked.Count == 0)
            return AnswerSignals.Empty with
            {
                FollowUps = turns.Count(turn => turn.Kind == TurnKind.FollowUp)
            };

        var answered = asked.Where(turn => !string.IsNullOrWhiteSpace(turn.Answer)).ToList();
        var delays = answered.Where(turn => turn.ResponseDelayMs is > 0).Select(turn => turn.ResponseDelayMs!.Value / 1000.0).ToList();
        var durations = answered.Select(turn => (turn.AnsweredAtUtc!.Value - turn.AskedAtUtc).TotalSeconds).Where(seconds => seconds > 0).ToList();

        var spoken = answered.Where(turn => turn.InputMode == AnswerInputMode.Speech && turn.SpeakingMs is > 1000).ToList();
        var spokenWords = spoken.Sum(turn => Words(turn.Answer!).Count);
        var spokenMinutes = spoken.Sum(turn => turn.SpeakingMs!.Value) / 60000.0;

        var allWords = answered.SelectMany(turn => Words(turn.RawAnswer ?? turn.Answer!)).ToList();
        var fillers = allWords.Count(Fillers.Contains);
        var latencies = asked.Where(turn => turn.DecisionLatencyMs is > 0).Select(turn => (double)turn.DecisionLatencyMs!.Value).ToList();

        return new AnswerSignals(
            answered.Count,
            asked.Count - answered.Count,
            asked.Count(turn => turn.TimedOut),
            answered.Count(turn => turn.InputMode == AnswerInputMode.Typed),
            turns.Count(turn => turn.Kind == TurnKind.FollowUp),
            delays.Count == 0 ? null : Math.Round(delays.Average(), 1),
            durations.Count == 0 ? null : Math.Round(durations.Average(), 1),
            spokenMinutes > 0 ? Math.Round(spokenWords / spokenMinutes) : null,
            fillers,
            allWords.Count == 0 ? null : Math.Round((double)fillers / allWords.Count, 3),
            latencies.Count == 0 ? null : Math.Round(latencies.Average()));
    }

    public static IReadOnlyList<string> Words(string text) =>
        WordPattern().Matches(text).Select(match => match.Value).ToList();

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}

/// <summary>Student codes as printed on grade sheets.</summary>
public static partial class StudentCodes
{
    /// <summary>
    /// FPT student emails end the local part with the student code, e.g. "anhnd<b>SE123456</b>@fpt.edu.vn".
    /// Returns that code in upper case, or null for any other address.
    /// </summary>
    public static string? FromEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0)
            return null;
        var match = CodePattern().Match(email[..at]);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"[a-zA-Z]{2}\d{5,6}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
