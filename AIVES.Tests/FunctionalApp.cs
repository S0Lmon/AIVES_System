using System.Collections.Concurrent;
using AIVES.BLL.Services.Email;
using AIVES.BLL.Services.Gemini;
using AIVES.DAL.Data;
using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIVES.Tests;

public sealed class FunctionalApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public TestMailSender Mail { get; } = new();
    public TestQuestionGenerator Generator { get; } = new();
    private string? connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["DemoAccount:Enabled"] = "false",
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = "",
            ["Logging:LogLevel:Default"] = "Error"
        }));
        builder.ConfigureTestServices(services =>
        {
            // The real DI graph reads its connection at registration time. Replace
            // its options explicitly so tests can never fall back to the app DB.
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
            services.RemoveAll<IAppEmailSender>();
            services.AddSingleton<IAppEmailSender>(Mail);
            services.RemoveAll<IGeminiQuestionGenerator>();
            services.AddSingleton<IGeminiQuestionGenerator>(Generator);
        });
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });

    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("AIVES_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(source)) return;
        connectionString = new SqlConnectionStringBuilder(source)
        {
            InitialCatalog = "AIVES_FunctionalTest_" + Guid.NewGuid().ToString("N")
        }.ConnectionString;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Rubrics.Add(new Rubric { Name = "Functional rubric", Description = "Test rubric", TotalPoints = 10 });
        await db.SaveChangesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (connectionString is null) return;
        var connection = new SqlConnectionStringBuilder(connectionString);
        Assert.StartsWith("AIVES_FunctionalTest_", connection.InitialCatalog);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options);
        await db.Database.EnsureDeletedAsync();
    }

    public sealed class TestMailSender : IAppEmailSender
    {
        public bool IsConfigured => true;
        public ConcurrentDictionary<string, string> Codes { get; } = new();
        public Task SendVerificationCodeAsync(string email, string name, string code, CancellationToken ct = default)
        {
            Codes[email] = code;
            return Task.CompletedTask;
        }
    }

    public sealed class TestQuestionGenerator : IGeminiQuestionGenerator
    {
        public bool IsConfigured => true;
        public int Calls { get; private set; }
        public Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(string subject, string topic, string? outcomes, string difficulty, int count, CancellationToken ct = default)
        {
            Calls++;
            if (topic == "simulate-failure") throw new InvalidOperationException("Gemini test failure");
            return Task.FromResult<IReadOnlyList<GeneratedVivaQuestion>>(Enumerable.Range(1, count).Select(i => new GeneratedVivaQuestion
            {
                Content = $"Generated question {i} for {topic}", ExpectedAnswer = "Expected test answer", BloomLevel = "Understand",
                FollowUpQuestions = ["First follow up", "Second follow up"]
            }).ToList());
        }
    }
}
