using AIVES.BLL.Services;
using AIVES.BLL.Services.Email;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using AIVES.WebRazor.Pages.Questions;
using Microsoft.EntityFrameworkCore;

namespace AIVES.Tests;

public sealed class LayerTests
{
    private static ApplicationDbContext CreateDatabase() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static QuestionService Questions(ApplicationDbContext db) => new(new QuestionRepository(db), new RubricRepository(db), new BloomLevelRepository(db));

    [Fact]
    public void PresentationAndBusinessDoNotReferenceEfOrPersistenceEntities()
    {
        var webReferences = typeof(AIVES.WebRazor.Pages.Questions.IndexModel).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("AIVES.DAL", webReferences);
        Assert.DoesNotContain(typeof(QuestionService).Assembly.GetReferencedAssemblies(), a => a.Name == "Microsoft.AspNetCore.Mvc" || a.Name == "Microsoft.AspNetCore.Authentication.Google");
        Assert.DoesNotContain(webReferences, a => a!.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(typeof(QuestionService).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(typeof(ApplicationDbContext).Assembly.GetReferencedAssemblies(), a => a.Name is "AIVES.BLL" or "AIVES.WebRazor");
        Assert.DoesNotContain(typeof(QuestionDto).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("AIVES.") || a.Name.StartsWith("Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public async Task QuestionCrudReturnsNamesAndPreservesCreatedDateInSameScope()
    {
        using var db = CreateDatabase();
        db.BloomLevels.Add(new BloomLevel { Id = 1, Name = "Remember", Order = 1 });
        db.Rubrics.Add(new Rubric { Id = 1, Name = "Rubric" });
        await db.SaveChangesAsync();
        var service = Questions(db);
        var dto = await service.CreateQuestionAsync(new QuestionDto { Content = "Explain a concept clearly", BloomLevelId = 1, RubricId = 1 });
        Assert.True(dto.Id > 0);
        var details = await service.GetQuestionByIdAsync(dto.Id);
        Assert.Equal("Remember", details!.BloomLevelName);
        Assert.Equal("Rubric", details.RubricName);
        var created = details.CreatedDate;
        await service.UpdateQuestionAsync(new QuestionDto { Id = dto.Id, Content = "Explain another concept clearly", BloomLevelId = 1, RubricId = 1, CreatedDate = DateTime.MinValue });
        var updated = await service.GetQuestionByIdAsync(dto.Id);
        Assert.Equal(created, updated!.CreatedDate);
        Assert.Equal("Explain another concept clearly", updated.Content);
        Assert.Single(await service.GetAllQuestionsAsync());
        await service.DeleteQuestionAsync(dto.Id);
        Assert.Null(await service.GetQuestionByIdAsync(dto.Id));
    }

    [Theory]
    [InlineData("short", 1, 1)]
    [InlineData("A valid question content", 999, 1)]
    [InlineData("A valid question content", 1, 999)]
    public async Task InvalidQuestionCannotReachPersistence(string content, int bloomId, int rubricId)
    {
        using var db = CreateDatabase();
        db.BloomLevels.Add(new BloomLevel { Id = 1, Name = "Remember" });
        db.Rubrics.Add(new Rubric { Id = 1, Name = "Rubric" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Questions(db).CreateQuestionAsync(new QuestionDto { Content = content, BloomLevelId = bloomId, RubricId = rubricId }));
        Assert.Empty(db.Questions);
    }

    [Fact]
    public async Task RubricUpdatePreservesCreatedDate()
    {
        using var db = CreateDatabase();
        var service = new RubricService(new RubricRepository(db));
        var dto = await service.CreateRubricAsync(Matrix("Original"));
        var replacement = Matrix("Updated");
        replacement.Id = dto.Id;
        replacement.CreatedDate = DateTime.MinValue;
        await service.UpdateRubricAsync(replacement);
        var updated = await service.GetRubricByIdAsync(dto.Id);
        Assert.Equal(dto.CreatedDate, updated!.CreatedDate);
        Assert.Equal("Updated", updated.Name);
    }

    [Theory]
    [InlineData(201, 0)]
    [InlineData(0, 1001)]
    public async Task InvalidRubricCannotReachPersistence(int nameLength, int descriptionLength)
    {
        using var db = CreateDatabase();
        var service = new RubricService(new RubricRepository(db));
        var dto = Matrix(new string('a', Math.Max(1, nameLength)));
        dto.Description = new string('b', descriptionLength);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRubricAsync(dto));
        Assert.Empty(db.Rubrics);
    }

    /// <summary>A valid two by two matrix, which is what a rubric is now required to be.</summary>
    private static RubricDto Matrix(string name) => RubricFixtures.Matrix(name);

    [Fact]
    public async Task VerificationCodeIsHashedRateLimitedAndSingleUse()
    {
        using var db = CreateDatabase();
        var sender = new CapturingSender();
        var service = new EmailVerificationService(new VerificationTestRepository(db), sender);
        await service.IssueAndSendAsync("user", "test@gmail.com", "Test");
        var saved = Assert.Single(db.EmailVerificationCodes);
        Assert.NotEqual(sender.Code, saved.CodeHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAndSendAsync("user", "test@gmail.com", "Test"));
        Assert.True(await service.VerifyAsync("user", sender.Code));
        Assert.False(await service.VerifyAsync("user", sender.Code));
    }

    [Fact]
    public async Task VerificationStopsAfterFiveWrongAttempts()
    {
        using var db = CreateDatabase();
        var sender = new CapturingSender();
        var service = new EmailVerificationService(new VerificationTestRepository(db), sender);
        await service.IssueAndSendAsync("user", "test@gmail.com", "Test");
        for (var attempt = 0; attempt < 5; attempt++)
            Assert.False(await service.VerifyAsync("user", "000000"));
        Assert.False(await service.VerifyAsync("user", sender.Code));
        Assert.Equal(5, Assert.Single(db.EmailVerificationCodes).FailedAttempts);
    }

    [Fact]
    public async Task ExpiredVerificationIsRejected()
    {
        using var db = CreateDatabase();
        var sender = new CapturingSender();
        var service = new EmailVerificationService(new VerificationTestRepository(db), sender);
        await service.IssueAndSendAsync("user", "test@gmail.com", "Test");
        Assert.Single(db.EmailVerificationCodes).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        Assert.False(await service.VerifyAsync("user", sender.Code));
    }

    [Fact]
    public void QuestionPostModelDoesNotContainDisplayOnlyNames()
    {
        Assert.Null(typeof(QuestionInput).GetProperty("BloomLevelName"));
        Assert.Null(typeof(QuestionInput).GetProperty("RubricName"));
    }

    private sealed class VerificationTestRepository(ApplicationDbContext db) : IEmailVerificationRepository
    {
        private readonly EmailVerificationRepository repository = new(db);
        public Task<DateTime?> GetLatestCreatedAtAsync(string id, CancellationToken ct) => repository.GetLatestCreatedAtAsync(id, ct);
        public Task ReplaceActiveAsync(EmailVerificationCodeDto dto, CancellationToken ct) => repository.ReplaceActiveAsync(dto, ct);
        public Task<EmailVerificationCodeDto?> GetActiveAsync(string id, DateTime now, CancellationToken ct) => repository.GetActiveAsync(id, now, ct);
        public async Task<bool> TryUpdateAsync(EmailVerificationCodeDto dto, int attempts, CancellationToken ct)
        {
            var entity = await db.EmailVerificationCodes.SingleAsync(x => x.Id == dto.Id, ct);
            if (entity.IsConsumed || entity.ExpiresAtUtc <= DateTime.UtcNow || entity.FailedAttempts != attempts)
                return false;
            entity.IsConsumed = dto.IsConsumed;
            entity.FailedAttempts = dto.FailedAttempts;
            await db.SaveChangesAsync(ct);
            return true;
        }
    }
    private sealed class CapturingSender : IAppEmailSender
    {
        public bool IsConfigured => true;
        public string Code { get; private set; } = string.Empty;
        public Task SendVerificationCodeAsync(string email, string name, string code, CancellationToken cancellationToken = default)
        {
            Code = code;
            return Task.CompletedTask;
        }
    }
}
