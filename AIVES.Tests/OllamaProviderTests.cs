using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Gemini;
using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Interview;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIVES.Tests;

/// <summary>Using a local Ollama model instead of Gemini for viva follow-ups and grading (Ollama:PreferLocal).</summary>
public sealed class OllamaProviderTests
{
    private sealed class Recorder(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(Uri Uri, JsonNode? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!, text.Length == 0 ? null : JsonNode.Parse(text)));
            return answer(request, text);
        }
    }

    private static HttpResponseMessage OllamaSays(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { model = "m", response = json, done = true }), Encoding.UTF8, "application/json")
    };

    private static HttpClient Client(Recorder recorder, string baseUrl = "http://localhost:11434/") => new(recorder) { BaseAddress = new Uri(baseUrl) };

    private static IOptions<OllamaOptions> Ollama(bool preferLocal = true, string model = "gemma2:27b", string? interview = null, string? grading = null) =>
        Options.Create(new OllamaOptions { Model = model, PreferLocal = preferLocal, InterviewModel = interview, GradingModel = grading });

    private static FollowUpRequest FollowUp() => new(AppLanguage.Vi, "Ká»¹ thuáº­t pháº§n má»m", "Kiáº¿n trÃºc ba lá»›p lÃ  gÃ¬?", "Giao diá»‡n, nghiá»‡p vá»¥, dá»¯ liá»‡u tÃ¡ch biá»‡t",
        [new InterviewExchange("Kiáº¿n trÃºc ba lá»›p lÃ  gÃ¬?", "LÃ  chia á»©ng dá»¥ng ra máº¥y pháº§n")], FollowUpsLeft: 2, Glossary: ["DTO"]);

    private static readonly RubricSnapshot Rubric = new("R", [new("Good", 4), new("Weak", 1)],
        [new RubricSnapshotCriterion("Accuracy", "", 4, [new("Good", "", 4), new("Weak", "", 1)]),
         new RubricSnapshotCriterion("Depth", "", 6, [new("Good", "", 6), new("Weak", "", 2)])]);

    private static GradingRequest Grading(RubricSnapshot? rubric = null) => new(AppLanguage.Vi, "SE", "Why DTOs?", "Decoupling layers", "Understand", rubric,
        rubric?.TotalPoints ?? 10, [new InterviewExchange("Why DTOs?", "To decouple the layers")], ["DTO"]);

    private const string FollowUpJson = """{"needsFollowUp":true,"reason":"vague","followUpQuestion":"Ba lá»›p Ä‘Ã³ lÃ  nhá»¯ng lá»›p nÃ o?"}""";
    private const string GradeJson = """{"criteria":[],"score":7.5,"strengths":["ÄÃºng Ã½ chÃ­nh"],"weaknesses":[],"missingPoints":["VÃ­ dá»¥"],"summary":"KhÃ¡"}""";

    [Trait("Requirement", "FR-08")]
    [Fact]
    public async Task OllamaFollowUpsUseTheInterviewPromptAndAJsonSchemaOllamaUnderstands()
    {
        var recorder = new Recorder((_, _) => OllamaSays(FollowUpJson));
        var generator = new OllamaFollowUpGenerator(Client(recorder), Ollama(interview: "gemma2:9b"), NullLogger<OllamaFollowUpGenerator>.Instance);

        var decision = await generator.DecideAsync(FollowUp());

        Assert.True(decision.NeedsFollowUp);
        Assert.Equal(FollowUpReason.Vague, decision.Reason);
        Assert.Equal("Ba lá»›p Ä‘Ã³ lÃ  nhá»¯ng lá»›p nÃ o?", decision.FollowUpQuestion);
        var (uri, body) = Assert.Single(recorder.Requests);
        Assert.Equal("http://localhost:11434/api/generate", uri.ToString());
        Assert.Equal("gemma2:9b", (string?)body!["model"]);
        Assert.False((bool)body["stream"]!);
        Assert.Equal("30m", (string?)body["keep_alive"]);
        Assert.Contains("LÃ  chia á»©ng dá»¥ng ra máº¥y pháº§n", (string?)body["prompt"]);
        // Gemini's "OBJECT"/"STRING" schema becomes JSON Schema for Ollama's structured output.
        var format = body["format"]!;
        Assert.Equal("object", (string?)format["type"]);
        Assert.Equal("boolean", (string?)format["properties"]!["needsFollowUp"]!["type"]);
        Assert.Contains("off_topic", format["properties"]!["reason"]!["enum"]!.AsArray().Select(item => (string?)item));
        Assert.Contains("needsFollowUp", format["required"]!.AsArray().Select(item => (string?)item));
    }

    [Trait("Requirement", "FR-09")]
    [Fact]
    public async Task OllamaGradingUsesTheGradingModelAndTheSameClampingAsGemini()
    {
        var recorder = new Recorder((_, _) => OllamaSays(
            """{"criteria":[{"criterion":"Accuracy","level":"Good","points":99,"rationale":"x"},{"criterion":"Depth","level":"Weak","points":2,"rationale":"y"}],"score":100,"strengths":[],"weaknesses":[],"missingPoints":[],"summary":"ok"}"""));
        var grader = new OllamaAnswerGrader(Client(recorder), Ollama(grading: "gemma2:27b"), NullLogger<OllamaAnswerGrader>.Instance);

        var suggestion = await grader.GradeAsync(Grading(Rubric));

        Assert.Equal(4, suggestion.Criteria[0].Points); // "Good" is worth 4 whatever the model wrote
        Assert.Equal(6, suggestion.Score);               // recomputed from the rubric, not the model's 100
        Assert.Equal("ollama/gemma2:27b", suggestion.Model);
        var body = Assert.Single(recorder.Requests).Body!;
        Assert.Equal("gemma2:27b", (string?)body["model"]);
        Assert.Equal("array", (string?)body["format"]!["properties"]!["criteria"]!["type"]);
        Assert.Equal(8192, (int)body["options"]!["num_ctx"]!); // the same window as every other call, so Ollama never reloads the model
    }

    [Trait("Requirement", "FR-09")]
    [Fact]
    public async Task OllamaErrorsAreReportedWithTheModelToPull()
    {
        var recorder = new Recorder((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"model not found"}""") });
        var grader = new OllamaAnswerGrader(Client(recorder), Ollama(model: "qwen2.5:7b"), NullLogger<OllamaAnswerGrader>.Instance);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => grader.GradeAsync(Grading()));
        Assert.Contains("ollama pull qwen2.5:7b", error.Message);
    }

    [Trait("Requirement", "FR-08")]
    [Trait("Requirement", "FR-09")]
    [Fact]
    public void NullableGeminiTypesBecomeJsonSchemaTypeLists()
    {
        var schema = OllamaJsonClient.ToJsonSchema(new { type = "OBJECT", properties = new { note = new { type = "STRING", nullable = true } } });
        Assert.Equal("object", (string?)schema["type"]);
        Assert.Equal(["string", "null"], schema["properties"]!["note"]!["type"]!.AsArray().Select(item => (string?)item));
        Assert.Null(schema["properties"]!["note"]!["nullable"]);
    }

    [Trait("Requirement", "FR-08")]
    [Fact]
    public async Task PreferLocalAsksOllamaFirstAndFallsBackToGeminiWhenItFails()
    {
        var ollama = new Recorder((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") });
        var gemini = new Recorder((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = FollowUpJson } } } } } }))
        });
        var routed = new RoutedFollowUpGenerator(
            new GeminiFollowUpGenerator(Client(gemini, "https://generativelanguage.googleapis.com/"), Options.Create(new GeminiOptions { ApiKey = "key" }),
                Options.Create(new InterviewOptions()), NullLogger<GeminiFollowUpGenerator>.Instance),
            new OllamaFollowUpGenerator(Client(ollama), Ollama(), NullLogger<OllamaFollowUpGenerator>.Instance),
            Ollama(), NullLogger<RoutedFollowUpGenerator>.Instance);

        var decision = await routed.DecideAsync(FollowUp());

        Assert.Equal(FollowUpReason.Vague, decision.Reason);
        Assert.Single(ollama.Requests);  // tried first
        Assert.Single(gemini.Requests);  // then the fallback
    }

    [Trait("Requirement", "FR-09")]
    [Fact]
    public async Task WithoutAGeminiKeyGradingUsesOllamaEvenWhenNotPreferred()
    {
        var ollama = new Recorder((_, _) => OllamaSays(GradeJson));
        var gemini = new Recorder((_, _) => throw new InvalidOperationException("Gemini must not be called without a key"));
        var routed = new RoutedAnswerGrader(
            new GeminiAnswerGrader(Client(gemini, "https://generativelanguage.googleapis.com/"), Options.Create(new GeminiOptions()),
                Options.Create(new GradingOptions()), NullLogger<GeminiAnswerGrader>.Instance),
            new OllamaAnswerGrader(Client(ollama), Ollama(preferLocal: false), NullLogger<OllamaAnswerGrader>.Instance),
            Ollama(preferLocal: false), NullLogger<RoutedAnswerGrader>.Instance);

        Assert.True(routed.IsConfigured);
        Assert.Equal("ollama/gemma2:27b", routed.ModelName);
        Assert.Equal(7.5m, (await routed.GradeAsync(Grading())).Score);
        Assert.Empty(gemini.Requests);
    }

    [Trait("Requirement", "FR-04")]
    [Fact]
    public void PreferLocalPutsOllamaFirstForQuestionGeneration()
    {
        var generators = new IQuestionGenerator[] { new Stub(AiProvider.Gemini), new Stub(AiProvider.Ollama) };
        Assert.Equal(AiProvider.Gemini, new QuestionGeneratorRouter(generators, NullLogger<QuestionGeneratorRouter>.Instance, Ollama(preferLocal: false)).ActiveProvider);
        Assert.Equal(AiProvider.Ollama, new QuestionGeneratorRouter(generators, NullLogger<QuestionGeneratorRouter>.Instance, Ollama()).ActiveProvider);
        // An explicit choice in the UI still wins.
        Assert.Equal(AiProvider.Gemini, new QuestionGeneratorRouter(generators, NullLogger<QuestionGeneratorRouter>.Instance, Ollama()).ResolveProvider(AiProvider.Gemini));
    }

    private sealed class Stub(AiProvider provider) : IQuestionGenerator
    {
        public AiProvider Provider => provider;
        public bool IsConfigured => true;
        public Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GeneratedVivaQuestion>>([]);
    }

    /// <summary>
    /// Real local model, end to end. Opt in with AIVES_TEST_OLLAMA_MODEL (e.g. gemma2:27b) and a running Ollama
    /// (AIVES_TEST_OLLAMA_URL, default http://localhost:11434). Prints how long each call took.
    /// </summary>
    [Trait("Requirement", "FR-08")]
    [Trait("Requirement", "FR-09")]
    [LiveOllamaFact]
    public async Task LiveOllamaDecidesAFollowUpAndGradesAnAnswer()
    {
        var model = Environment.GetEnvironmentVariable("AIVES_TEST_OLLAMA_MODEL")!;
        var url = Environment.GetEnvironmentVariable("AIVES_TEST_OLLAMA_URL") ?? "http://localhost:11434";
        var options = Options.Create(new OllamaOptions { BaseUrl = url, Model = model, PreferLocal = true });
        using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(10) };

        var watch = Stopwatch.StartNew();
        var decision = await new OllamaFollowUpGenerator(http, options, NullLogger<OllamaFollowUpGenerator>.Instance).DecideAsync(FollowUp());
        var followUpSeconds = watch.Elapsed.TotalSeconds;
        watch.Restart();
        var suggestion = await new OllamaAnswerGrader(http, options, NullLogger<OllamaAnswerGrader>.Instance).GradeAsync(Grading(Rubric));
        var gradeSeconds = watch.Elapsed.TotalSeconds;

        Console.WriteLine($"[live-ollama] {model}: follow-up {followUpSeconds:F1} s -> {decision.Reason} \"{decision.FollowUpQuestion}\"; grade {gradeSeconds:F1} s -> {suggestion.Score}/10");
        Assert.True(Enum.IsDefined(decision.Reason));
        Assert.Equal(decision.NeedsFollowUp, !string.IsNullOrWhiteSpace(decision.FollowUpQuestion));
        Assert.InRange(suggestion.Score, 0, 10);
        Assert.Equal(2, suggestion.Criteria.Count);
    }
}

public sealed class LiveOllamaFactAttribute : FactAttribute
{
    public LiveOllamaFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIVES_TEST_OLLAMA_MODEL")))
            Skip = "Set AIVES_TEST_OLLAMA_MODEL (and a running Ollama) to opt into one live local-model request.";
    }
}
