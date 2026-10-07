using AIVES.DTO;

namespace AIVES.Tests;

/// <summary>Plan §5: the exam status of one candidate derives from the override, the attempt and the clock.</summary>
public sealed class CandidateStatusTests
{
    private static readonly DateTime Start = new(2026, 12, 15, 1, 0, 0, DateTimeKind.Utc);

    private static ExamCandidateDto Candidate(InterviewRecordDto? interview = null, CandidateStatus? statusOverride = null) =>
        new(2, "b@fpt.edu.vn", null, Start, Start.AddMinutes(20), [], CandidateId: 5, interview, statusOverride);

    [Fact]
    public void TheClockDecidesScheduledWaitingAndAbsent()
    {
        var candidate = Candidate();

        Assert.Equal(CandidateStatus.Scheduled, candidate.StatusAt(Start.AddMinutes(-1)));
        Assert.Equal(CandidateStatus.Waiting, candidate.StatusAt(Start));
        Assert.Equal(CandidateStatus.Waiting, candidate.StatusAt(Start.AddMinutes(19)));
        Assert.Equal(CandidateStatus.Absent, candidate.StatusAt(Start.AddMinutes(20)));
    }

    [Fact]
    public void TheAttemptDecidesInProgressAndCompleted()
    {
        var running = new InterviewRecordDto(InterviewStatus.InProgress, Start, null, []);
        var finished = new InterviewRecordDto(InterviewStatus.Completed, Start, Start.AddMinutes(15), []);

        Assert.Equal(CandidateStatus.InProgress, Candidate(running).StatusAt(Start.AddMinutes(5)));
        Assert.Equal(CandidateStatus.Completed, Candidate(finished).StatusAt(Start.AddMinutes(30)));
    }

    [Theory]
    [InlineData(CandidateStatus.NotScheduled)]
    [InlineData(CandidateStatus.Absent)]
    [InlineData(CandidateStatus.Cancelled)]
    [InlineData(CandidateStatus.RequiresReview)]
    public void AStatusOverrideWinsOverEverything(CandidateStatus statusOverride)
    {
        var finished = new InterviewRecordDto(InterviewStatus.Completed, Start, Start.AddMinutes(15), []);

        Assert.Equal(statusOverride, Candidate(finished, statusOverride).StatusAt(Start.AddMinutes(30)));
        Assert.Equal(statusOverride, Candidate(statusOverride: statusOverride).StatusAt(Start.AddMinutes(-60)));
    }
}