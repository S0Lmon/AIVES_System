using AIVES.BLL.Services.Operations;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIVES.Tests;

/// <summary>FR-11: the append-only audit log behind /Grading/Audit/{id} and /Admin/Audit.</summary>
[Trait("Requirement", "FR-11")]
public sealed class AuditTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 8, 30, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }

    private sealed class BrokenRepository : IAuditRepository
    {
        public Task AddAsync(AuditEntryInput entry, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is down");
        public Task<IReadOnlyList<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuditEntryDto>>([]);
    }

    private static (AuditService Audit, ApplicationDbContext Db, FixedClock Clock) Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var clock = new FixedClock(Now);
        return (TestServices.Audit(db, clock), db, clock);
    }

    [Fact]
    public async Task EveryEntryKeepsWhoWhatWhenAndWhichExam()
    {
        var (audit, db, _) = Build();

        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeSaved, "lecturer-1", "gv@fpt.edu.vn", ExamId: 4, CandidateId: 9, Details: "Q1: 6 → 8/10 (AI 6)"));

        var entry = Assert.Single(db.AuditEntries);
        Assert.Equal(Now, entry.AtUtc);
        Assert.Equal(AuditActions.GradeSaved, entry.Action);
        Assert.Equal("lecturer-1", entry.ActorId);
        Assert.Equal("gv@fpt.edu.vn", entry.ActorEmail);
        Assert.Equal(4, entry.ExamId);
        Assert.Equal(9, entry.CandidateId);
        Assert.Equal("Q1: 6 → 8/10 (AI 6)", entry.Details);
    }

    [Fact]
    public async Task VeryLongDetailsAreCutRatherThanLost()
    {
        var (audit, db, _) = Build();
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamUpdated, "lecturer-1", "gv@fpt.edu.vn", Details: new string('x', 5000)));
        Assert.Equal(4000, Assert.Single(db.AuditEntries).Details!.Length);
    }

    [Fact]
    public async Task AFailedAuditWriteDoesNotUndoTheAction()
    {
        var audit = new AuditService(new BrokenRepository(), TimeProvider.System, NullLogger<AuditService>.Instance);
        // Must not throw: the grade, exam or recording action that called it has already happened.
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeFinalized, "lecturer-1", "gv@fpt.edu.vn", ExamId: 1));
    }

    [Fact]
    public async Task TheLogIsReadNewestFirstAndFiltersByExamCandidateActionAndActor()
    {
        var (audit, _, clock) = Build();
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamCreated, "gv1", "gv1@fpt.edu.vn", ExamId: 1));
        clock.Current = Now.AddMinutes(1);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.InterviewStarted, null, "sv1@fpt.edu.vn", ExamId: 1, CandidateId: 10));
        clock.Current = Now.AddMinutes(2);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeSaved, "gv1", "gv1@fpt.edu.vn", ExamId: 1, CandidateId: 10));
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeFinalized, "gv1", "gv1@fpt.edu.vn", ExamId: 1, CandidateId: 11));
        await audit.WriteAsync(new AuditEntryInput(AuditActions.ExamCreated, "gv2", "gv2@fpt.edu.vn", ExamId: 2));
        await audit.WriteAsync(new AuditEntryInput(AuditActions.SettingsChanged, "ad", "admin@fpt.edu.vn"));

        var all = await audit.QueryAsync(new AuditQuery());
        Assert.Equal(6, all.Count);
        Assert.Equal(AuditActions.SettingsChanged, all[0].Action);
        Assert.True(all.Zip(all.Skip(1)).All(pair => pair.First.Id > pair.Second.Id));

        Assert.Equal(4, (await audit.QueryAsync(new AuditQuery(ExamId: 1))).Count);
        Assert.Equal([AuditActions.GradeSaved, AuditActions.InterviewStarted],
            (await audit.QueryAsync(new AuditQuery(ExamId: 1, CandidateId: 10))).Select(entry => entry.Action));
        // "grade" matches every grade.* action, as the admin page's action filter does.
        Assert.Equal(2, (await audit.QueryAsync(new AuditQuery(Action: "grade"))).Count);
        Assert.All(await audit.QueryAsync(new AuditQuery(Actor: "gv2")), entry => Assert.Equal("gv2@fpt.edu.vn", entry.ActorEmail));
        Assert.Equal(2, (await audit.QueryAsync(new AuditQuery(Take: 2))).Count);
    }
}
