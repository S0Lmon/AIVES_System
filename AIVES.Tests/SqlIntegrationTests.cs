using AIVES.BLL.Services;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIVES.Tests;

public sealed class SqlIntegrationTests
{
    [SqlFact]
    public async Task ConcurrentOtpConsumptionAndFailuresAreAtomic()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AIVES_TEST_SQL_CONNECTION"))
        {
            InitialCatalog = "AIVES_LayerTest_" + Guid.NewGuid().ToString("N")
        };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var setup = new ApplicationDbContext(options);
        try
        {
            await setup.Database.MigrateAsync();
            setup.Users.Add(new AIVES.DAL.Entities.ApplicationUser { Id = "otp-user", UserName = "otp@test.local", DisplayName = "Test" });
            await setup.SaveChangesAsync();
            var repo = new EmailVerificationRepository(setup);
            await repo.ReplaceActiveAsync(new EmailVerificationCodeDto { UserId = "otp-user", Salt = "salt", CodeHash = "hash", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) }, default);
            await using var first = new ApplicationDbContext(options);
            await using var second = new ApplicationDbContext(options);
            var a = new EmailVerificationRepository(first);
            var b = new EmailVerificationRepository(second);
            var one = (await a.GetActiveAsync("otp-user", DateTime.UtcNow, default))!;
            var two = (await b.GetActiveAsync("otp-user", DateTime.UtcNow, default))!;
            one.FailedAttempts = two.FailedAttempts = 1;
            Assert.True(await a.TryUpdateAsync(one, 0, default));
            Assert.True(await b.TryUpdateAsync(two, 0, default));
            Assert.Equal(2, (await repo.GetActiveAsync("otp-user", DateTime.UtcNow, default))!.FailedAttempts);
            one.IsConsumed = two.IsConsumed = true;
            var results = await Task.WhenAll(a.TryUpdateAsync(one, 2, default), b.TryUpdateAsync(two, 2, default));
            Assert.Single(results, x => x);
            Assert.Null(await repo.GetActiveAsync("otp-user", DateTime.UtcNow, default));
        }
        finally { await setup.Database.EnsureDeletedAsync(); }
    }

    [SqlFact]
    public async Task ExistingMigrationChainAndDtoCrudWorkOnSqlServer()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AIVES_TEST_SQL_CONNECTION"))
        {
            InitialCatalog = "AIVES_LayerTest_" + Guid.NewGuid().ToString("N")
        };
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
        try
        {
            await db.Database.MigrateAsync();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Equal(12, applied.Count);
            Assert.Contains(applied, name => name.EndsWith("AddGradingRecordingAudit", StringComparison.Ordinal));
            Assert.Contains(applied, name => name.EndsWith("AddRubricMatrix", StringComparison.Ordinal));
            Assert.Contains(applied, name => name.EndsWith("LinkQuestionsToSubjectAndTopic", StringComparison.Ordinal));
            Assert.Contains(applied, name => name.EndsWith("AddDataProtectionKeys", StringComparison.Ordinal));
            Assert.Contains(applied, name => name.EndsWith("AddExams", StringComparison.Ordinal));
            Assert.Contains(applied, name => name.EndsWith("AddInterviews", StringComparison.Ordinal));
            Assert.Contains(applied, name => name.EndsWith("ExamSchedulingAndStatus", StringComparison.Ordinal));
            Assert.False(db.Database.HasPendingModelChanges());

            // A question must be able to point at both a subject and a topic, and must survive
            // without a rubric because the create form treats the rubric as optional.
            var subjects = new SubjectRepository(db);
            var topics = new TopicRepository(db);
            var subject = await subjects.AddAsync(new SubjectInput("Databases", "Catalog subject"));
            var topic = await topics.AddAsync(new TopicInput(subject.Id, "Indexing", "Catalog topic"));

            var rubrics = new RubricService(new RubricRepository(db));
            var rubric = await rubrics.CreateRubricAsync(RubricFixtures.Matrix("SQL integration rubric"));
            var questions = new QuestionService(new QuestionRepository(db), new RubricRepository(db), new BloomLevelRepository(db));
            var question = await questions.CreateQuestionAsync(new QuestionDto
            {
                Content = "Explain the three application layers",
                BloomLevelId = 1,
                RubricId = rubric.Id,
                SubjectId = subject.Id,
                TopicId = topic.Id
            });
            var detail = await questions.GetQuestionByIdAsync(question.Id);
            Assert.Equal("Remember", detail!.BloomLevelName);
            Assert.Equal(rubric.Name, detail.RubricName);
            Assert.Equal(subject.Id, detail.SubjectId);
            Assert.Equal("Databases", detail.SubjectName);
            Assert.Equal(topic.Id, detail.TopicId);
            Assert.Equal("Indexing", detail.TopicName);

            var noRubric = await questions.CreateQuestionAsync(new QuestionDto
            {
                Content = "A question stored without any rubric attached",
                BloomLevelId = 1,
                SubjectId = subject.Id
            });
            Assert.Null(noRubric.RubricId);
            Assert.Null((await questions.GetQuestionByIdAsync(noRubric.Id))!.RubricId);

            // The repository can reach a question from either side of the catalog.
            Assert.Contains(await questions.GetQuestionsBySubjectAsync(subject.Id), q => q.Id == question.Id);
            Assert.Contains(await questions.GetQuestionsByTopicAsync(topic.Id), q => q.Id == question.Id);

            await questions.UpdateQuestionAsync(new QuestionDto { Id = question.Id, Content = "Explain the revised three application layers", BloomLevelId = 1, RubricId = rubric.Id, SubjectId = subject.Id, TopicId = topic.Id });
            var updated = await questions.GetQuestionByIdAsync(question.Id);
            Assert.Equal(detail.CreatedDate, updated!.CreatedDate);
            Assert.Equal("Explain the revised three application layers", updated.Content);

            // Deleting the subject detaches its questions rather than taking them with it.
            await subjects.DeleteAsync(subject.Id);
            var orphaned = await questions.GetQuestionByIdAsync(question.Id);
            Assert.NotNull(orphaned);
            Assert.Null(orphaned!.SubjectId);

            await questions.DeleteQuestionAsync(noRubric.Id);
            await questions.DeleteQuestionAsync(question.Id);
            Assert.Null(await questions.GetQuestionByIdAsync(question.Id));
            await rubrics.DeleteRubricAsync(rubric.Id);
        }
        finally
        {
            // Only the unique database created by this test is removed.
            Assert.StartsWith("AIVES_LayerTest_", connection.InitialCatalog);
            await db.Database.EnsureDeletedAsync();
        }
    }
}

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIVES_TEST_SQL_CONNECTION")))
            Skip = "Set AIVES_TEST_SQL_CONNECTION to run against a temporary SQL Server database.";
    }
}
