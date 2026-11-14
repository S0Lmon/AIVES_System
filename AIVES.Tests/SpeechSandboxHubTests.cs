extern alias Razor;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AIVES.Tests;

public sealed class SpeechSandboxHubTests : IClassFixture<SpeechSandboxHubTests.SandboxApp>
{
    private readonly SandboxApp app;

    public SpeechSandboxHubTests(SandboxApp app) => this.app = app;

    [Fact]
    public async Task ServerSpeechRecognitionHubIsDisabled()
    {
        using var client = app.CreateClient();
        using var response = await client.PostAsync(
            "/hubs/speech-recognition/negotiate?negotiateVersion=1",
            new StringContent(string.Empty));

        Assert.False(response.IsSuccessStatusCode);
    }

    public sealed class SandboxApp : WebApplicationFactory<Razor::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=127.0.0.1,1;Database=None;User Id=none;Password=none;Connect Timeout=1;TrustServerCertificate=True",
                ["Database:MigrateOnStartup"] = "false",
                ["Grading:WorkerEnabled"] = "false",
                ["Authentication:Google:ClientId"] = ""
            }));
        }
    }
}
