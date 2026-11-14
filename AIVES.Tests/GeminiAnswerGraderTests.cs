using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Gemini;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace AIVES.Tests;

public sealed class GeminiAnswerGraderTests
{
    [Fact]
    public async Task HttpErrorsExposeGeminisReasonForTheFailedGradingRequest()
    {
        using var client = new HttpClient(new ErrorHandler())
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var grader = new GeminiAnswerGrader(client,
            Options.Create(new GeminiOptions { ApiKey = "test-key", Model = "test-model" }),
            Options.Create(new GradingOptions()),
            NullLogger<GeminiAnswerGrader>.Instance);
        var request = new GradingRequest(AppLanguage.En, "Computer Science", "What is a layer?", "A separation of concerns.",
            "Understand", null, 10, [new InterviewExchange("What is a layer?", "A boundary.")], []);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => grader.GradeAsync(request));

        Assert.Equal("API key not valid", error.Message);
    }

    private sealed class ErrorHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"API key not valid"}}""", Encoding.UTF8, "application/json")
            });
    }
}
