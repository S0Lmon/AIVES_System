using AIVES.BLL.Services.Exams;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIVES.Tests;

public sealed class ExamServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 8, 0, 0, DateTimeKind.Utc);
    private static readonly ExamActor Lecturer = new("lecturer-1", IsAdmin: false);
    private static readonly ExamActor OtherLecturer = new("lecturer-2", IsAdmin: false);
    private static readonly ExamActor Admin = new("admin-1", IsAdmin: true);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }

    private static (ExamService Service, ApplicationDbContext Db, FixedClock Clock, int SubjectId, int TopicId, int OtherSubjectTopicId) Build(int questions = 12)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        db.Database.EnsureCreated(); // seeds the Bloom levels
        var subject = new Subject { Name = "Software Engineering" };
        var topic = new Topic { Name = "Layers", Subject = subject };
        var other = new Subject { Name = "Networks" };
        var otherTopic = new Topic { Name = "TCP", Subject = other };
        db.AddRange(subject, topic, other, otherTopic);
        for (var i = 1; i <= questions; i++)
            db.Questions.Add(new Question { Content = $"Q{i}", ExpectedAnswer = $"A{i}", BloomLevelId = (i - 1) % 4 + 1, Subject = subject, Topic = topic });
        db.Questions.Add(new Question { Content = "Inactive", BloomLevelId = 1, Subject = subject, Topic = topic, IsActive = false });
        db.Questions.Add(new Question { Content = "Other subject", BloomLevelId = 1, Subject = other, Topic = otherTopic });
        db.SaveChanges();
        var clock = new FixedClock(Now);
        var service = new ExamService(new ExamRepository(db), new SubjectRepository(db), new TopicRepository(db),
            TestServices.SubjectAccess(db), TestServices.Audit(db, clock), TestServices.Recordings(db, clock), clock, NullLogger<ExamService>.Instance);
        return (service, db, clock, subject.Id, topic.Id, otherTopic.Id);
    }

    private static ExamInput Input(int subjectId, int? topicId = null, int main = 3, params string[] emails) => new(
        "Midterm viva", subjectId, topicId, Now.AddDays(1), 15, main, 2,
        emails.Length > 0 ? emails : ["a@fpt.edu.vn", "b@fpt.edu.vn", "c@fpt.edu.vn", "d@fpt.edu.vn"]);

    [Fact]
    public async Task CreatingAnExamSchedulesSlotsAndSnapshotsQuestions()
    {
        var (service, db, _, subjectId, topicId, _) = Build();

        var result = await service.CreateAsync(Input(subjectId, topicId), Lecturer);
        var exam = (await service.GetAsync(result.ExamId, Lecturer))!;

        Assert.Equal(0, result.ConsecutiveOverlaps);
        Assert.Equal("Software Engineering", exam.SubjectName);
        Assert.Equal("Layers", exam.TopicName);
        Assert.Equal(4, exam.Candidates.Count);
        Assert.Equal(Now.AddDays(1).AddMinutes(45), exam.Candidates[3].StartsAtUtc);
        Assert.Equal(exam.StartsAtUtc.AddMinutes(60), exam.EndsAtUtc);
        Assert.All(exam.Candidates, candidate =>
        {
            Assert.Equal(3, candidate.Questions.Count);
            Assert.DoesNotContain(candidate.Questions, question => question.Content is "Inactive" or "Other subject");
        });

        // Editing the bank afterwards does not change what the exam recorded.
        var first = exam.Candidates[0].Questions[0];
        var bankQuestion = db.Questions.Single(question => question.Id == first.QuestionId);
        bankQuestion.Content = "Rewritten later";
        db.SaveChanges();
        Assert.Equal(first.Content, (await service.GetAsync(result.ExamId, Lecturer))!.Candidates[0].Questions[0].Content);
    }

    [Fact]
    public async Task CandidateEmailsAreNormalisedAndInvalidOnesAreListed()
    {
        var (service, _, _, subjectId, _, _) = Build();

        Assert.Equal(["a@fpt.edu.vn", "b@gmail.com"], service.ParseCandidateEmails(" A@FPT.edu.vn\r\nb@gmail.com; a@fpt.edu.vn ,"));
        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Input(subjectId, emails: ["ok@fpt.edu.vn", "not-an-email", "x@localhost"]), Lecturer));
        Assert.Contains("not-an-email", error.Message);
        Assert.Contains("x@localhost", error.Message);
    }

    [Fact]
    public async Task InvalidSettingsAreRejected()
    {
        var (service, _, _, subjectId, _, otherTopicId) = Build(questions: 5);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(subjectId) with { Title = " " }, Lecturer));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(subjectId) with { StartsAtUtc = Now.AddMinutes(-1) }, Lecturer));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(subjectId) with { SlotMinutes = 2 }, Lecturer));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(subjectId) with { CandidateEmails = [] }, Lecturer));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(subjectId, topicId: otherTopicId), Lecturer));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(9999), Lecturer));
        var pool = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Input(subjectId, main: 6), Lecturer));
        Assert.Contains("Only 5 active questions", pool.Message);
    }

    [Fact]
    public async Task LecturersOnlySeeAndChangeTheirOwnExamsWhileAdminsSeeAll()
    {
        var (service, _, _, subjectId, _, _) = Build();
        var id = (await service.CreateAsync(Input(subjectId), Lecturer)).ExamId;

        Assert.Null(await service.GetAsync(id, OtherLecturer));
        Assert.Empty(await service.ListAsync(OtherLecturer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(id, Input(subjectId), OtherLecturer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReassignQuestionsAsync(id, OtherLecturer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeleteAsync(id, OtherLecturer));

        Assert.NotNull(await service.GetAsync(id, Admin));
        Assert.Single(await service.ListAsync(Admin));
        Assert.Single(await service.ListAsync(Lecturer));
    }

    [Fact]
    public async Task EditingReplacesCandidatesAndKeepsTheOwner()
    {
        var (service, _, _, subjectId, _, _) = Build();
        var id = (await service.CreateAsync(Input(subjectId), Lecturer)).ExamId;

        await service.UpdateAsync(id, Input(subjectId, main: 2, emails: ["z@fpt.edu.vn", "a@fpt.edu.vn"]) with { Title = "Final viva" }, Admin);
        var exam = (await service.GetAsync(id, Lecturer))!;

        Assert.Equal("Final viva", exam.Title);
        Assert.Equal(Lecturer.UserId, exam.CreatedById);
        Assert.Equal(["z@fpt.edu.vn", "a@fpt.edu.vn"], exam.Candidates.Select(candidate => candidate.Email));
        Assert.All(exam.Candidates, candidate => Assert.Equal(2, candidate.Questions.Count));
    }

    [Fact]
    public async Task StartedExamsAreLockedButAdminsMayStillDeleteThem()
    {
        var (service, _, clock, subjectId, _, _) = Build();
        var id = (await service.CreateAsync(Input(subjectId), Lecturer)).ExamId;
        clock.Current = Now.AddDays(1).AddMinutes(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(id, Input(subjectId), Lecturer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReassignQuestionsAsync(id, Lecturer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(id, Lecturer));

        await service.DeleteAsync(id, Admin);
        Assert.Null(await service.GetAsync(id, Admin));
    }

    [Fact]
    public async Task StudentsSeeOnlyTheirOwnSlot()
    {
        var (service, _, _, subjectId, _, _) = Build();
        await service.CreateAsync(Input(subjectId), Lecturer);

        var mine = Assert.Single(await service.ListForCandidateAsync("C@fpt.edu.vn"));
        Assert.Equal(3, mine.Order);
        Assert.Equal(4, mine.CandidateCount);
        Assert.Equal(Now.AddDays(1).AddMinutes(30), mine.StartsAtUtc);
        Assert.Equal(Now.AddDays(1).AddMinutes(45), mine.EndsAtUtc);
        Assert.Empty(await service.ListForCandidateAsync("nobody@fpt.edu.vn"));
    }

    [Fact]
    public async Task TheScheduleUsesBufferAndBreaksAndStoresEverySlot()
    {
        var (service, _, _, subjectId, topicId, _) = Build();
        var input = Input(subjectId, topicId) with { BufferMinutes = 5, BreakMinutes = 15, BreakEveryCount = 2 };

        var id = (await service.CreateAsync(input, Lecturer)).ExamId;
        var exam = (await service.GetAsync(id, Lecturer))!;

        // 15 min slots with a 5 min buffer, plus a 15 min break after candidate #2.
        Assert.Equal(new[] { 0, 20, 55, 75 }.Select(minutes => input.StartsAtUtc.AddMinutes(minutes)),
            exam.Candidates.Select(candidate => candidate.StartsAtUtc));
        // No end time given: the window follows the schedule, which ends 90 minutes in.
        Assert.Equal(input.StartsAtUtc.AddMinutes(90), exam.EndsAtUtc);
        Assert.Equal(input.StartsAtUtc.AddMinutes(90), exam.ScheduleEndsAtUtc);
        Assert.Equal(5, exam.BufferMinutes);
        Assert.Equal(15, exam.BreakMinutes);
        Assert.Equal(2, exam.BreakEveryCount);
    }

    [Fact]
    public async Task TheScheduleMustFitTheWindowUnlessTheLecturerAllowsTheOverflow()
    {
        var (service, _, _, subjectId, topicId, _) = Build();
        var input = Input(subjectId, topicId) with { BufferMinutes = 5, BreakMinutes = 15, BreakEveryCount = 2 };
        var tooSmall = input with { EndsAtUtc = input.StartsAtUtc.AddMinutes(60) }; // the schedule needs 90

        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(tooSmall, Lecturer));
        Assert.Contains("does not fit the exam window", error.Message);

        var allowed = await service.CreateAsync(tooSmall with { ScheduleOverflowAllowed = true }, Lecturer);
        var exam = (await service.GetAsync(allowed.ExamId, Lecturer))!;
        Assert.Equal(input.StartsAtUtc.AddMinutes(60), exam.EndsAtUtc);          // the window the lecturer chose
        Assert.Equal(input.StartsAtUtc.AddMinutes(90), exam.ScheduleEndsAtUtc);  // where the schedule really ends

        var endsBeforeStart = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(input with { EndsAtUtc = input.StartsAtUtc.AddMinutes(-5) }, Lecturer));
        Assert.Contains("must end after it starts", endsBeforeStart.Message);
    }

    [Fact]
    public async Task BreakSettingsThatContradictEachOtherAreRejected()
    {
        var (service, _, _, subjectId, topicId, _) = Build();

        var lengthWithoutCount = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Input(subjectId, topicId) with { BreakMinutes = 15 }, Lecturer));
        Assert.Contains("after how many candidates", lengthWithoutCount.Message);

        var countWithoutLength = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Input(subjectId, topicId) with { BreakEveryCount = 3 }, Lecturer));
        Assert.Contains("break length in minutes", countWithoutLength.Message);
    }

    [Fact]
    public async Task SlotsCanBeMovedByHandButNotIntoAnotherSlotOrAfterTheStart()
    {
        var (service, _, clock, subjectId, topicId, _) = Build();
        var id = (await service.CreateAsync(Input(subjectId, topicId), Lecturer)).ExamId;
        var start = Now.AddDays(1);

        // #4 moved into #2's slot: rejected with the candidate it clashes with.
        var overlap = await Assert.ThrowsAsync<ArgumentException>(() => service.AdjustSlotAsync(id, 4, start.AddMinutes(15), Lecturer));
        Assert.Contains("overlaps candidate #2", overlap.Message);
        // Before the exam start: rejected as well.
        await Assert.ThrowsAsync<ArgumentException>(() => service.AdjustSlotAsync(id, 4, start.AddMinutes(-5), Lecturer));
        // An unknown candidate is not found.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AdjustSlotAsync(id, 9, start.AddMinutes(60), Lecturer));

        // Into the free gap after the last slot: accepted and stored.
        await service.AdjustSlotAsync(id, 4, start.AddMinutes(60), Lecturer);
        var exam = (await service.GetAsync(id, Lecturer))!;
        Assert.Equal(start.AddMinutes(60), exam.Candidates[3].StartsAtUtc);

        // Once the exam has started, the schedule is locked like the rest of the record.
        clock.Current = start.AddMinutes(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AdjustSlotAsync(id, 3, start.AddMinutes(120), Lecturer));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetScheduleAsync(id, Lecturer));
    }

    [Fact]
    public async Task ResetScheduleRegeneratesEverySlotFromTheSettings()
    {
        var (service, _, _, subjectId, topicId, _) = Build();
        var input = Input(subjectId, topicId) with { BufferMinutes = 5 };
        var id = (await service.CreateAsync(input, Lecturer)).ExamId;
        var start = input.StartsAtUtc;

        await service.AdjustSlotAsync(id, 1, start.AddMinutes(120), Lecturer);
        await service.ResetScheduleAsync(id, Lecturer);

        var exam = (await service.GetAsync(id, Lecturer))!;
        Assert.Equal(new[] { 0, 20, 40, 60 }.Select(minutes => start.AddMinutes(minutes)),
            exam.Candidates.Select(candidate => candidate.StartsAtUtc));
    }

    [Fact]
    public async Task CandidateStatusesCanBeOverriddenAndDerivedFromTheSchedule()
    {
        var (service, _, _, subjectId, topicId, _) = Build();
        var id = (await service.CreateAsync(Input(subjectId, topicId), Lecturer)).ExamId;

        var exam = (await service.GetAsync(id, Lecturer))!;
        Assert.Equal(CandidateStatus.Scheduled, exam.Candidates[1].StatusAt(Now.AddDays(1)));

        // The lecturer marks candidate #2 absent; clearing the override returns to the derived status.
        await service.SetCandidateStatusAsync(id, 2, CandidateStatus.Absent, Lecturer);
        exam = (await service.GetAsync(id, Lecturer))!;
        Assert.Equal(CandidateStatus.Absent, exam.Candidates[1].StatusOverride);
        Assert.Equal(CandidateStatus.Absent, exam.Candidates[1].StatusAt(Now.AddDays(1)));

        await service.SetCandidateStatusAsync(id, 2, null, Lecturer);
        exam = (await service.GetAsync(id, Lecturer))!;
        Assert.Null(exam.Candidates[1].StatusOverride);

        // Derived from the clock: scheduled before the slot, waiting during it, absent afterwards.
        Assert.Equal(CandidateStatus.Scheduled, exam.Candidates[1].StatusAt(Now.AddDays(1)));
        Assert.Equal(CandidateStatus.Waiting, exam.Candidates[1].StatusAt(Now.AddDays(1).AddMinutes(16)));
        Assert.Equal(CandidateStatus.Absent, exam.Candidates[1].StatusAt(Now.AddDays(1).AddMinutes(60)));

        // Only the settable statuses may be written by hand, and only for candidates on the exam.
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetCandidateStatusAsync(id, 2, CandidateStatus.InProgress, Lecturer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetCandidateStatusAsync(id, 9, CandidateStatus.Absent, Lecturer));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetCandidateStatusAsync(999, 1, CandidateStatus.Absent, Lecturer));
    }

    [Fact]
    public async Task SessionDetailsAndQuestionStrategyAreStored()
    {
        var (service, _, _, subjectId, topicId, _) = Build();
        var input = Input(subjectId, topicId) with
        {
            Term = "Fall 2026",
            ExamType = "Final",
            Instructions = "Bring your student ID.",
            Strategy = QuestionSelectionStrategy.Random
        };

        var id = (await service.CreateAsync(input, Lecturer)).ExamId;
        var exam = (await service.GetAsync(id, Lecturer))!;

        Assert.Equal("Fall 2026", exam.Term);
        Assert.Equal("Final", exam.ExamType);
        Assert.Equal("Bring your student ID.", exam.Instructions);
        Assert.Equal(QuestionSelectionStrategy.Random, exam.Strategy);
        Assert.All(exam.Candidates, candidate => Assert.NotEqual(default, candidate.StartsAtUtc));
    }
}
