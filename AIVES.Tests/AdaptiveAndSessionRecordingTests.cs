using AIVES.BLL.Services.Interview;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace AIVES.Tests;

[Trait("Requirement", "FR-08")]
public sealed class AdaptiveQuestionPickerTests
{
    private static ExamPoolQuestion Q(int id, int order) => new(id, order, $"Level {order}", $"Question {id}", $"Expected {id}", BloomOrder: order);

    private static AdaptivePool Pool(IEnumerable<ExamPoolQuestion> questions, IEnumerable<int>? neighbours = null, Dictionary<int, int>? usage = null) =>
        new(questions.ToList(), new Dictionary<string, int>(), (neighbours ?? []).ToHashSet(), usage ?? []);

    [Theory]
    [InlineData(FollowUpReason.Sufficient, false, 0, FollowUpReason.Sufficient, AdaptiveStep.Harder)]
    [InlineData(FollowUpReason.Sufficient, true, 0, FollowUpReason.Sufficient, AdaptiveStep.Easier)]
    [InlineData(FollowUpReason.NoAnswer, false, 0, FollowUpReason.NoAnswer, AdaptiveStep.Easier)]
    [InlineData(FollowUpReason.OffTopic, false, 0, FollowUpReason.OffTopic, AdaptiveStep.Easier)]
    [InlineData(FollowUpReason.Missing, false, 1, FollowUpReason.Sufficient, AdaptiveStep.Same)]
    [InlineData(FollowUpReason.Vague, false, 1, FollowUpReason.LimitReached, AdaptiveStep.Same)]
    [InlineData(FollowUpReason.Missing, false, 2, FollowUpReason.LimitReached, AdaptiveStep.Easier)]
    [InlineData(FollowUpReason.Missing, false, 1, FollowUpReason.NoAnswer, AdaptiveStep.Easier)]
    [InlineData(FollowUpReason.AiUnavailable, false, 0, FollowUpReason.AiUnavailable, AdaptiveStep.Same)]
    [InlineData(FollowUpReason.LimitReached, false, 0, FollowUpReason.LimitReached, AdaptiveStep.Same)]
    public void AnswersAreAssessedFromTheExaminersDecisions(FollowUpReason first, bool timedOut, int followUps, FollowUpReason last, AdaptiveStep expected) =>
        Assert.Equal(expected, AdaptiveQuestionPicker.Assess(first, timedOut, followUps, last));

    [Fact]
    public void AHarderQuestionIsTheNearestLevelUpThatNeighboursDidNotGet()
    {
        var pool = Pool([Q(1, 2), Q(2, 2), Q(3, 3), Q(4, 3), Q(5, 4)], neighbours: [3]);
        var chosen = AdaptiveQuestionPicker.Pick(pool, AdaptiveStep.Harder, currentOrder: 2, plannedOrder: 2, assignedIds: [1, 2], new Random(1));
        Assert.Equal(4, chosen!.Id);
    }

    [Fact]
    public void AnEasierQuestionPrefersTheLeastUsed()
    {
        var pool = Pool([Q(1, 1), Q(2, 1), Q(3, 3), Q(4, 3)], usage: new() { [1] = 5, [2] = 1 });
        var chosen = AdaptiveQuestionPicker.Pick(pool, AdaptiveStep.Easier, currentOrder: 3, plannedOrder: 3, assignedIds: [3, 4], new Random(1));
        Assert.Equal(2, chosen!.Id);
    }

    [Fact]
    public void ThePlannedQuestionIsKeptWhenItAlreadyMovesTheRightWayOrNothingFits()
    {
        var pool = Pool([Q(1, 2), Q(2, 4), Q(3, 3)]);
        Assert.Null(AdaptiveQuestionPicker.Pick(pool, AdaptiveStep.Harder, 2, plannedOrder: 4, [1, 2], new Random(1)));
        Assert.Null(AdaptiveQuestionPicker.Pick(pool, AdaptiveStep.Same, 2, plannedOrder: 2, [1], new Random(1)));
        // Nothing easier than Remember exists, and questions already in the set are never reused.
        Assert.Null(AdaptiveQuestionPicker.Pick(Pool([Q(1, 1), Q(2, 1)]), AdaptiveStep.Easier, 1, 1, [1], new Random(1)));
        Assert.Null(AdaptiveQuestionPicker.Pick(Pool([Q(1, 2), Q(2, 3)]), AdaptiveStep.Harder, 2, 2, [1, 2], new Random(1)));
    }
}

[Trait("Requirement", "FR-08")]
public sealed class AdaptiveInterviewTests
{
    private const string Email = "student@fpt.edu.vn";
    private static readonly DateTime Now = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }

    private sealed class Generator(params FollowUpDecision[] decisions) : IFollowUpGenerator
    {
        private readonly Queue<FollowUpDecision> script = new(decisions);
        public bool IsConfigured => true;
        public Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default) => Task.FromResult(script.Dequeue());
    }

    private sealed record Harness(ApplicationDbContext Db, FixedClock Clock, int CandidateId, Dictionary<string, int> Questions);

    /// <summary>A candidate with two Understand questions; the pool also holds one Remember and one Apply question.</summary>
    private static Harness Build(QuestionSelectionStrategy strategy)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var levels = new[] { "Remember", "Understand", "Apply" }.Select((name, index) => new BloomLevel { Name = name, Order = index + 1 }).ToList();
        db.BloomLevels.AddRange(levels);
        db.SaveChanges();
        var questions = new Dictionary<string, Question>
        {
            ["u1"] = new() { Content = "Understand 1?", ExpectedAnswer = "U1", BloomLevelId = levels[1].Id },
            ["u2"] = new() { Content = "Understand 2?", ExpectedAnswer = "U2", BloomLevelId = levels[1].Id },
            ["remember"] = new() { Content = "Remember?", ExpectedAnswer = "R", BloomLevelId = levels[0].Id },
            ["apply"] = new() { Content = "Apply?", ExpectedAnswer = "A", BloomLevelId = levels[2].Id }
        };
        db.Questions.AddRange(questions.Values);
        db.SaveChanges();

        var candidate = new ExamCandidate { Order = 1, Email = Email };
        candidate.Questions.Add(new ExamCandidateQuestion { Order = 1, QuestionId = questions["u1"].Id, Content = "Understand 1?", ExpectedAnswer = "U1", BloomLevelName = "Understand" });
        candidate.Questions.Add(new ExamCandidateQuestion { Order = 2, QuestionId = questions["u2"].Id, Content = "Understand 2?", ExpectedAnswer = "U2", BloomLevelName = "Understand" });
        var exam = new Exam
        {
            Title = "Viva",
            SubjectName = ".NET",
            StartsAtUtc = Now.AddMinutes(-1),
            SlotMinutes = 30,
            MainQuestionCount = 2,
            MaxFollowUpQuestions = 4,
            MaxFollowUpsPerQuestion = 2,
            Language = "vi-VN",
            CreatedById = "owner",
            Strategy = strategy,
            Candidates = { candidate }
        };
        foreach (var question in questions.Values)
            exam.QuestionPool.Add(new ExamQuestionPool { QuestionId = question.Id });
        db.Exams.Add(exam);
        db.SaveChanges();
        return new Harness(db, new FixedClock(Now), candidate.Id, questions.ToDictionary(pair => pair.Key, pair => pair.Value.Id));
    }

    private static InterviewService Service(Harness h, IFollowUpGenerator ai) => new(
        new InterviewRepository(h.Db), ai, TestServices.Glossary(h.Db), TestServices.Settings(h.Db, h.Clock), TestServices.Audit(h.Db, h.Clock),
        Options.Create(new InterviewOptions()), h.Clock, NullLogger<InterviewService>.Instance);

    private static FollowUpDecision Enough => new(false, FollowUpReason.Sufficient, null);

    [Fact]
    public async Task AGoodFirstAnswerBringsAHarderNextQuestion()
    {
        var h = Build(QuestionSelectionStrategy.Adaptive);
        var service = Service(h, new Generator(Enough));
        var state = await service.StartAsync(h.CandidateId, Email);

        state = await service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "Một câu trả lời đầy đủ", AnswerInputMode.Speech));

        Assert.Equal("Apply?", state.CurrentTurn!.QuestionText);
        var second = h.Db.ExamCandidateQuestions.Single(question => question.Order == 2);
        Assert.Equal(h.Questions["apply"], second.QuestionId);
        Assert.Equal("Apply", second.BloomLevelName);
        Assert.Equal("A", second.ExpectedAnswer);
        Assert.Contains(h.Db.AuditEntries, entry => entry.Action == AuditActions.InterviewQuestionAdapted && entry.Details!.Contains("harder"));
    }

    [Fact]
    public async Task NoAnswerBringsAnEasierNextQuestion()
    {
        var h = Build(QuestionSelectionStrategy.Adaptive);
        var service = Service(h, new Generator());
        var state = await service.StartAsync(h.CandidateId, Email);

        state = await service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "", AnswerInputMode.Speech));

        Assert.Equal("Remember?", state.CurrentTurn!.QuestionText);
        Assert.Equal(h.Questions["remember"], h.Db.ExamCandidateQuestions.Single(question => question.Order == 2).QuestionId);
    }

    [Fact]
    public async Task AnAnswerThatNeededAFollowUpKeepsThePlannedQuestion()
    {
        var h = Build(QuestionSelectionStrategy.Adaptive);
        var service = Service(h, new Generator(new FollowUpDecision(true, FollowUpReason.Missing, "Còn gì nữa?"), Enough));
        var state = await service.StartAsync(h.CandidateId, Email);

        state = await service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "Thiếu ý", AnswerInputMode.Speech));
        Assert.Equal(TurnKind.FollowUp, state.CurrentTurn!.Kind);
        state = await service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "Bổ sung đủ", AnswerInputMode.Speech));

        Assert.Equal("Understand 2?", state.CurrentTurn!.QuestionText);
        Assert.DoesNotContain(h.Db.AuditEntries, entry => entry.Action == AuditActions.InterviewQuestionAdapted);
    }

    [Fact]
    public async Task BalancedExamsNeverChangeTheDrawnQuestions()
    {
        var h = Build(QuestionSelectionStrategy.Balanced);
        var service = Service(h, new Generator(Enough));
        var state = await service.StartAsync(h.CandidateId, Email);

        state = await service.AnswerAsync(h.CandidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "Một câu trả lời đầy đủ", AnswerInputMode.Speech));

        Assert.Equal("Understand 2?", state.CurrentTurn!.QuestionText);
        Assert.Equal(h.Questions["u2"], h.Db.ExamCandidateQuestions.Single(question => question.Order == 2).QuestionId);
    }

    [Fact]
    public async Task AQuestionAlreadyAskedIsNeverReplaced()
    {
        var h = Build(QuestionSelectionStrategy.Adaptive);
        var service = Service(h, new Generator());
        await service.StartAsync(h.CandidateId, Email);
        var repository = new InterviewRepository(h.Db);
        var apply = (await repository.GetAdaptivePoolAsync(h.CandidateId))!.Questions.Single(question => question.Id == h.Questions["apply"]);

        Assert.False(await repository.ReplaceQuestionAsync(h.CandidateId, 1, apply));
        Assert.True(await repository.ReplaceQuestionAsync(h.CandidateId, 2, apply));
    }
}

[Trait("Requirement", "FR-10")]
public sealed class SessionRecordingTests
{
    private const string Email = "student@fpt.edu.vn";
    private static readonly DateTime Now = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }

    private sealed class Enough : IFollowUpGenerator
    {
        public bool IsConfigured => true;
        public Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FollowUpDecision(false, FollowUpReason.Sufficient, null));
    }

    private static (ApplicationDbContext Db, FixedClock Clock, int CandidateId) Build(bool recordAudio = true, bool recordVideo = false)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var candidate = new ExamCandidate { Order = 1, Email = Email };
        candidate.Questions.Add(new ExamCandidateQuestion { Order = 1, Content = "What is SQL?", ExpectedAnswer = "A query language", BloomLevelName = "Remember" });
        db.Exams.Add(new Exam
        {
            Title = "Viva",
            SubjectName = "Databases",
            StartsAtUtc = Now.AddMinutes(-1),
            SlotMinutes = 30,
            MainQuestionCount = 1,
            Language = "vi-VN",
            CreatedById = "owner",
            RecordAudio = recordAudio,
            RecordVideo = recordVideo,
            Candidates = { candidate }
        });
        db.SaveChanges();
        return (db, new FixedClock(Now), candidate.Id);
    }

    private static InterviewService Interview(ApplicationDbContext db, FixedClock clock) => new(
        new InterviewRepository(db), new Enough(), TestServices.Glossary(db), TestServices.Settings(db, clock), TestServices.Audit(db, clock),
        Options.Create(new InterviewOptions()), clock, NullLogger<InterviewService>.Instance);

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task TheWholeVivaIsStoredEncryptedInPiecesAndPlaysAsOneFile()
    {
        var (db, clock, candidateId) = Build();
        var state = await Interview(db, clock).StartAsync(candidateId, Email, recordingConsent: true);
        var store = new MemoryRecordingStore();
        var recordings = TestServices.Recordings(db, clock, store);
        const long segment = 1_762_761_600_000;

        await recordings.SaveSessionPartAsync(candidateId, Email, segment, 0, Bytes("header+first;"), "audio/webm;codecs=opus");
        // A retried upload of the same piece is stored once.
        await recordings.SaveSessionPartAsync(candidateId, Email, segment, 0, Bytes("header+first;"), "audio/webm");
        await Interview(db, clock).AnswerAsync(candidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "SQL", AnswerInputMode.Speech));
        // The final piece arrives just after the viva has finished.
        clock.Current = Now.AddMinutes(2);
        await recordings.SaveSessionPartAsync(candidateId, Email, segment, 1, Bytes("second"), "audio/webm");

        Assert.Equal(2, store.Files.Count);
        Assert.All(store.Files.Values, data => Assert.DoesNotContain("second", Encoding.UTF8.GetString(data)));
        Assert.Empty(await recordings.ListSessionAsync(candidateId, new ExamActor("someone-else", false)));
        Assert.Null(await recordings.OpenSessionAsync(candidateId, segment, new ExamActor("someone-else", false)));

        var owner = new ExamActor("owner", false, "owner@fpt.edu.vn");
        var segments = await recordings.ListSessionAsync(candidateId, owner);
        Assert.Equal(2, Assert.Single(segments).Parts);
        var opened = await recordings.OpenSessionAsync(candidateId, segment, owner);
        Assert.Equal("header+first;second", Encoding.UTF8.GetString(opened!.Data));
        Assert.Equal("audio/webm", opened.ContentType);
        Assert.Contains(db.AuditEntries, entry => entry.Action == AuditActions.RecordingViewed && entry.Details!.Contains("Whole-viva"));
        Assert.Contains(db.AuditEntries, entry => entry.Action == AuditActions.RecordingStored && entry.Details!.Contains("Whole-viva"));

        // Past the retention period every piece and its file are deleted.
        clock.Current = Now.AddDays(400);
        Assert.Equal(2, await recordings.PurgeExpiredAsync());
        Assert.Empty(store.Files);
        Assert.Empty(db.SessionRecordingParts);
    }

    [Fact]
    public async Task PiecesAreRefusedWithoutConsentForTheWrongFormatOrLongAfterTheSlot()
    {
        var (db, clock, candidateId) = Build();
        var recordings = TestServices.Recordings(db, clock);
        // Not started yet, so nobody agreed to the recording.
        await Assert.ThrowsAsync<InvalidOperationException>(() => recordings.SaveSessionPartAsync(candidateId, Email, 1, 0, Bytes("x"), "audio/webm"));

        await Interview(db, clock).StartAsync(candidateId, Email, recordingConsent: true);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => recordings.SaveSessionPartAsync(candidateId, "other@fpt.edu.vn", 1, 0, Bytes("x"), "audio/webm"));
        // Audio-only exams do not accept the camera.
        await Assert.ThrowsAsync<ArgumentException>(() => recordings.SaveSessionPartAsync(candidateId, Email, 1, 0, Bytes("x"), "video/webm"));
        await Assert.ThrowsAsync<ArgumentException>(() => recordings.SaveSessionPartAsync(candidateId, Email, 1, -1, Bytes("x"), "audio/webm"));

        clock.Current = Now.AddHours(2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recordings.SaveSessionPartAsync(candidateId, Email, 1, 0, Bytes("x"), "audio/webm"));
        Assert.Empty(db.SessionRecordingParts);
    }

    [Fact]
    public async Task EachReloadStartsASegmentOfItsOwn()
    {
        var (db, clock, candidateId) = Build(recordVideo: true);
        await Interview(db, clock).StartAsync(candidateId, Email, recordingConsent: true);
        var recordings = TestServices.Recordings(db, clock);

        await recordings.SaveSessionPartAsync(candidateId, Email, 100, 0, Bytes("a"), "video/webm");
        await recordings.SaveSessionPartAsync(candidateId, Email, 200, 0, Bytes("b"), "video/webm");

        var segments = await recordings.ListSessionAsync(candidateId, new ExamActor("admin", true));
        Assert.Equal([100L, 200L], segments.Select(segment => segment.Segment));
        Assert.All(segments, segment => Assert.True(segment.HasVideo));
        Assert.Equal("b", Encoding.UTF8.GetString((await recordings.OpenSessionAsync(candidateId, 200, new ExamActor("admin", true)))!.Data));
    }
}
