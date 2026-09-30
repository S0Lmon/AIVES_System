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
            Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
            Assert.False(db.Database.HasPendingModelChanges());
            var rubrics = new RubricService(new RubricRepository(db));
            var rubric = await rubrics.CreateRubricAsync(new RubricDto { Name = "SQL integration rubric" });
            var questions = new QuestionService(new QuestionRepository(db), new RubricRepository(db), new BloomLevelRepository(db));
            var question = await questions.CreateQuestionAsync(new QuestionDto { Content = "Explain the three application layers", BloomLevelId = 1, RubricId = rubric.Id });
            var detail = await questions.GetQuestionByIdAsync(question.Id);
            Assert.Equal("Remember", detail!.BloomLevelName);
            Assert.Equal(rubric.Name, detail.RubricName);
            await questions.UpdateQuestionAsync(new QuestionDto { Id = question.Id, Content = "Explain the revised three application layers", BloomLevelId = 1, RubricId = rubric.Id });
            var updated = await questions.GetQuestionByIdAsync(question.Id);
            Assert.Equal(detail.CreatedDate, updated!.CreatedDate);
            Assert.Equal("Explain the revised three application layers", updated.Content);
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
