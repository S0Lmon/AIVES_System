using AIVES.BLL.Services.Interview;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

public sealed class InterviewServiceTests
{
    private const string Email = "student@fpt.edu.vn";
    private static readonly DateTime Now = new(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }

    /// <summary>Plays back scripted decisions; an exception or a delay can stand in for a failing model.</summary>
    private sealed class ScriptedGenerator : IFollowUpGenerator
    {
        public Queue<Func<CancellationToken, Task<FollowUpDecision>>> Script { get; } = new();
        public List<FollowUpRequest> Requests { get; } = [];
        public bool IsConfigured { get; set; } = true;

        public void Probe(FollowUpReason reason, string question) => Script.Enqueue(_ => Task.FromResult(new FollowUpDecision(true, reason, question)));
        public void Enough() => Script.Enqueue(_ => Task.FromResult(new FollowUpDecision(false, FollowUpReason.Sufficient, null)));

        public Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Script.Count > 0 ? Script.Dequeue()(cancellationToken) : throw new InvalidOperationException("No scripted decision left.");
        }
    }

    private sealed record Harness(InterviewService Service, ApplicationDbContext Db, FixedClock Clock, ScriptedGenerator Ai, int CandidateId);

    private static Harness Build(int followUpsPerQuestion = 2, int followUpsTotal = 3, int questions = 2, int aiTimeoutSeconds = 12)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var candidate = new ExamCandidate { Order = 1, Email = Email };
        for (var i = 1; i <= questions; i++)
            candidate.Questions.Add(new ExamCandidateQuestion { Order = i, Content = $"Main question {i}?", ExpectedAnswer = $"Expected {i}", BloomLevelName = "Understand" });
        db.Exams.Add(new Exam
        {
            Title = "Viva",
            SubjectName = ".NET",
            StartsAtUtc = Now.AddMinutes(-1),
            SlotMinutes = 30,
            MainQuestionCount = questions,
            MaxFollowUpQuestions = followUpsTotal,
            MaxFollowUpsPerQuestion = followUpsPerQuestion,
            AnswerTimeLimitSeconds = 60,
            Language = "vi-VN",
            CreatedById = "lecturer",
            Candidates = { candidate }
        });
        db.SaveChanges();
        var clock = new FixedClock(Now);
        var ai = new ScriptedGenerator();
        var service = new InterviewService(new InterviewRepository(db), ai,
            TestServices.Glossary(db), TestServices.Settings(db, clock), TestServices.Audit(db, clock),
            Options.Create(new InterviewOptions { AiTimeoutSeconds = aiTimeoutSeconds, AnswerGraceSeconds = 15 }), clock, NullLogger<InterviewService>.Instance);
        return new Harness(service, db, clock, ai, candidate.Id);
    }

    private static Task<InterviewStateDto> Answer(Harness h, InterviewStateDto state, string text, AnswerInputMode mode = AnswerInputMode.Speech) =>
        h.Service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, text, mode));

    [Fact]
    public async Task OnlyTheListedCandidateCanOpenTheirSlot()
    {
        var h = Build();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Service.GetStateAsync(h.CandidateId, "someone.else@fpt.edu.vn"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => h.Service.GetStateAsync(9999, Email));
        Assert.Equal(InterviewStatus.NotStarted, (await h.Service.GetStateAsync(h.CandidateId, "STUDENT@fpt.edu.vn")).Status);
    }

    [Fact]
    public async Task TheVivaCanOnlyStartDuringTheSlot()
    {
        var h = Build();
        h.Clock.Current = Now.AddMinutes(-5);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.StartAsync(h.CandidateId, Email));
        h.Clock.Current = Now.AddMinutes(40);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.StartAsync(h.CandidateId, Email));
        h.Clock.Current = Now;
        var state = await h.Service.StartAsync(h.CandidateId, Email);
        Assert.Equal(InterviewStatus.InProgress, state.Status);
        Assert.Equal("Main question 1?", state.CurrentTurn!.QuestionText);
        Assert.Equal(60, state.CurrentTurn.TimeLimitSeconds);
        // Starting again resumes the same question instead of creating a second attempt.
        Assert.Equal(state.CurrentTurn.TurnId, (await h.Service.StartAsync(h.CandidateId, Email)).CurrentTurn!.TurnId);
        Assert.Single(h.Db.ExamAttempts);
    }

    [Fact]
    public async Task FollowUpsProbeAnswersWithinThePerQuestionAndPerCandidateLimits()
    {
        var h = Build(followUpsPerQuestion: 1, followUpsTotal: 1);
        h.Ai.Probe(FollowUpReason.Contradiction, "Scoped khác Singleton ở đâu?");

        var state = await h.Service.StartAsync(h.CandidateId, Email);
        state = await Answer(h, state, "Scoped giống Singleton");
        Assert.Equal(TurnKind.FollowUp, state.CurrentTurn!.Kind);
        Assert.Equal("Scoped khác Singleton ở đâu?", state.CurrentTurn.QuestionText);
        Assert.Equal(1, state.CurrentTurn.MainIndex);
        Assert.Equal(1, state.CurrentTurn.FollowUpIndex);

        // One follow-up per question: move on without asking the AI.
        state = await Answer(h, state, "Scoped là một instance cho mỗi request");
        Assert.Equal(TurnKind.Main, state.CurrentTurn!.Kind);
        Assert.Equal(2, state.CurrentTurn.MainIndex);

        // The candidate's single follow-up is used up: the last answer ends the viva without the AI.
        state = await Answer(h, state, "Câu trả lời cho câu 2");
        Assert.Equal(InterviewStatus.Completed, state.Status);
        Assert.Null(state.CurrentTurn);
        Assert.Single(h.Ai.Requests);

        var turns = h.Db.ExamTurns.OrderBy(turn => turn.Order).ToList();
        Assert.Equal([FollowUpReason.Contradiction, FollowUpReason.LimitReached, FollowUpReason.LimitReached], turns.Select(turn => turn.Decision!.Value));
        Assert.NotNull(h.Db.ExamAttempts.Single().CompletedAtUtc);
    }

    [Fact]
    public async Task TheAiSeesTheWholeExchangeAboutTheCurrentQuestion()
    {
        var h = Build();
        h.Ai.Probe(FollowUpReason.Missing, "Còn Singleton?");
        h.Ai.Enough();

        var state = await h.Service.StartAsync(h.CandidateId, Email);
        state = await Answer(h, state, "Transient tạo mới mỗi lần");
        await Answer(h, state, "Singleton dùng chung");

        var second = h.Ai.Requests[1];
        Assert.Equal("Main question 1?", second.MainQuestion);
        Assert.Equal("Expected 1", second.ExpectedAnswer);
        Assert.Equal(AppLanguage.Vi, second.Language);
        Assert.Equal(["Transient tạo mới mỗi lần", "Singleton dùng chung"], second.Exchanges.Select(exchange => exchange.Answer));
        Assert.Equal("Còn Singleton?", second.Exchanges[1].Question);
    }

    [Fact]
    public async Task SilenceAndAiFailuresMoveOnWithoutStallingTheViva()
    {
        var h = Build(questions: 3, aiTimeoutSeconds: 1);
        h.Ai.Script.Enqueue(_ => throw new HttpRequestException("503"));
        h.Ai.Script.Enqueue(async token => { await Task.Delay(TimeSpan.FromSeconds(10), token); return new FollowUpDecision(true, FollowUpReason.Vague, "late"); });

        var state = await h.Service.StartAsync(h.CandidateId, Email);
        state = await Answer(h, state, "   ");                    // nothing said: no AI call
        Assert.Empty(h.Ai.Requests);
        state = await Answer(h, state, "Một câu trả lời");          // AI throws
        Assert.Equal(3, state.CurrentTurn!.MainIndex);
        state = await Answer(h, state, "Câu trả lời cuối");         // AI too slow, cancelled after 1 s
        Assert.Equal(InterviewStatus.Completed, state.Status);

        var decisions = h.Db.ExamTurns.OrderBy(turn => turn.Order).Select(turn => turn.Decision).ToList();
        Assert.Equal([FollowUpReason.NoAnswer, FollowUpReason.AiUnavailable, FollowUpReason.AiUnavailable], decisions);
    }

    [Fact]
    public async Task ARepeatedOrStaleSubmissionChangesNothing()
    {
        var h = Build();
        h.Ai.Enough();
        var first = await h.Service.StartAsync(h.CandidateId, Email);
        var afterFirst = await Answer(h, first, "Câu trả lời");

        // The same turn again (double click, retried request) and a made-up turn id.
        var repeated = await Answer(h, first, "Ghi đè?");
        var stale = await h.Service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(12345, "x", AnswerInputMode.Typed));

        Assert.Equal(afterFirst.CurrentTurn!.TurnId, repeated.CurrentTurn!.TurnId);
        Assert.Equal(afterFirst.CurrentTurn.TurnId, stale.CurrentTurn!.TurnId);
        Assert.Equal("Câu trả lời", h.Db.ExamTurns.Single(turn => turn.Order == 1).Answer);
        Assert.Single(h.Ai.Requests);
    }

    [Fact]
    public async Task LateAnswersAreFlaggedAndTypedAnswersRecorded()
    {
        var h = Build(questions: 1);
        h.Ai.Enough();
        var state = await h.Service.StartAsync(h.CandidateId, Email);
        h.Clock.Current = Now.AddSeconds(60 + 16);   // limit 60 s + 15 s grace exceeded

        await Answer(h, state, "Trễ giờ", AnswerInputMode.Typed);

        var turn = h.Db.ExamTurns.Single();
        Assert.True(turn.TimedOut);
        Assert.Equal(AnswerInputMode.Typed, turn.InputMode);
    }

    [Fact]
    public async Task AnOpenQuestionIsClosedWhenTheSlotEnds()
    {
        var h = Build();
        await h.Service.StartAsync(h.CandidateId, Email);
        h.Clock.Current = Now.AddMinutes(30);

        var state = await h.Service.GetStateAsync(h.CandidateId, Email);

        Assert.Equal(InterviewStatus.Completed, state.Status);
        var turn = h.Db.ExamTurns.Single();
        Assert.True(turn.TimedOut);
        Assert.Equal(FollowUpReason.LimitReached, turn.Decision);
    }

    [Fact]
    public async Task AnAnswerThatWasNeverAdvancedIsRecoveredLater()
    {
        var h = Build();
        var state = await h.Service.StartAsync(h.CandidateId, Email);
        // Simulate a request that stored the answer and then died before deciding.
        var turn = h.Db.ExamTurns.Single();
        turn.Answer = "Đã trả lời";
        turn.AnsweredAtUtc = Now;
        h.Db.SaveChanges();

        // While the AI might still be working, the page just waits...
        var waiting = await h.Service.GetStateAsync(h.CandidateId, Email);
        Assert.Equal(InterviewStatus.InProgress, waiting.Status);
        Assert.Null(waiting.CurrentTurn);

        // ...and once that window has passed, the viva moves on to the next question.
        h.Clock.Current = Now.AddSeconds(30);
        var recovered = await h.Service.GetStateAsync(h.CandidateId, Email);
        Assert.Equal(2, recovered.CurrentTurn!.MainIndex);
        Assert.Empty(h.Ai.Requests);
        Assert.NotEqual(state.CurrentTurn!.TurnId, recovered.CurrentTurn.TurnId);
    }

    [Fact]
    public async Task WithoutAConfiguredModelTheVivaStillRuns()
    {
        var h = Build(questions: 1);
        h.Ai.IsConfigured = false;
        var state = await h.Service.StartAsync(h.CandidateId, Email);

        state = await Answer(h, state, "Câu trả lời");

        Assert.Equal(InterviewStatus.Completed, state.Status);
        Assert.Empty(h.Ai.Requests);
        Assert.Equal(FollowUpReason.AiUnavailable, h.Db.ExamTurns.Single().Decision);
    }
}
