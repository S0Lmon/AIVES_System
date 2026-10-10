using AIVES.BLL.Services.Ai;
using AIVES.BLL.Services.Catalog;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

public sealed class AiProviderTests
{
    private static QuestionGenerationRequest Request(string topic = "Layers", int count = 1) =>
        new("Software", topic, null, count, Difficulty: "Intermediate");

    [Fact]
    public void GeminiIsPreferredWhenBothProvidersAreConfigured()
    {
        var router = new QuestionGeneratorRouter(
            [new StubGenerator(AiProvider.Gemini, true), new StubGenerator(AiProvider.Ollama, true)],
            NullLogger<QuestionGeneratorRouter>.Instance);

        Assert.Equal(AiProvider.Gemini, router.ActiveProvider);
        Assert.Equal(AiProvider.Gemini, router.ResolveProvider(null));
        Assert.True(router.IsProviderAvailable(AiProvider.Ollama));
    }

    [Fact]
    public void OllamaTakesOverWhenGeminiHasNoApiKey()
    {
        var router = new QuestionGeneratorRouter(
            [new StubGenerator(AiProvider.Gemini, false), new StubGenerator(AiProvider.Ollama, true)],
            NullLogger<QuestionGeneratorRouter>.Instance);

        Assert.Equal(AiProvider.Ollama, router.ActiveProvider);
        Assert.Equal(AiProvider.Ollama, router.ResolveProvider(null));
        Assert.False(router.IsProviderAvailable(AiProvider.Gemini));
    }

    [Fact]
    public void ExplicitProviderIsHonouredWhenAvailable()
    {
        var router = new QuestionGeneratorRouter(
            [new StubGenerator(AiProvider.Gemini, true), new StubGenerator(AiProvider.Ollama, true)],
            NullLogger<QuestionGeneratorRouter>.Instance);

        Assert.Equal(AiProvider.Ollama, router.ResolveProvider(AiProvider.Ollama));
    }

    [Fact]
    public void UnavailableExplicitProviderFallsBackInsteadOfFailing()
    {
        var router = new QuestionGeneratorRouter(
            [new StubGenerator(AiProvider.Gemini, true), new StubGenerator(AiProvider.Ollama, false)],
            NullLogger<QuestionGeneratorRouter>.Instance);

        Assert.Equal(AiProvider.Gemini, router.ResolveProvider(AiProvider.Ollama));
    }

    [Fact]
    public async Task GenerationReachesTheResolvedProvider()
    {
        var ollama = new StubGenerator(AiProvider.Ollama, true);
        var router = new QuestionGeneratorRouter([new StubGenerator(AiProvider.Gemini, false), ollama],
            NullLogger<QuestionGeneratorRouter>.Instance);

        var questions = await router.GenerateAsync(Request());

        Assert.Single(questions);
        Assert.Equal(1, ollama.Calls);
    }

    [Fact]
    public void NoConfiguredProviderIsAControlledFailure()
    {
        var router = new QuestionGeneratorRouter(
            [new StubGenerator(AiProvider.Gemini, false), new StubGenerator(AiProvider.Ollama, false)],
            NullLogger<QuestionGeneratorRouter>.Instance);

        Assert.Null(router.ActiveProvider);
        Assert.Throws<InvalidOperationException>(() => router.ResolveProvider(null));
    }

    [Fact]
    public void RoutedRequestCarriesRetrievedContextAndSources()
    {
        var sources = new[] { new MaterialExcerpt(1, "Slide", "text") };
        var request = new QuestionGenerationRequest("Software", "Layers", null, 1, "grounded text", sources);

        Assert.Equal("grounded text", request.RetrievedContext);
        Assert.Single(request.RetrievedSources);
        Assert.Empty(Request().RetrievedSources);
    }

    [Fact]
    public async Task RagContextRanksMaterialByQueryTermsAndRespectsTheBudget()
    {
        var service = new CatalogService(new StubSubjects(), new StubTopics(), new StubMaterials([
            Material("Unrelated notes about gardening", "sunlight soil watering"),
            Material("Three layer architecture", "presentation bll dal layers"),
            Material("Architecture patterns", "layering boundary dependency")
        ]), Options.Create(new OllamaOptions { MaxContextCharacters = 1000 }));

        var context = await service.BuildRagContextAsync(topicId: 1, subjectId: null, query: "architecture layers dal");

        Assert.NotEmpty(context.Sources);
        Assert.Contains(context.Sources, excerpt => excerpt.Title == "Three layer architecture");
        Assert.DoesNotContain(context.Sources, excerpt => excerpt.Title == "Unrelated notes about gardening");
        Assert.True(context.Text.Length <= 1000 + 200, "context must stay near the configured budget");
    }

    [Fact]
    public async Task RagContextIsEmptyWhenNothingMatchesTheQuery()
    {
        var service = new CatalogService(new StubSubjects(), new StubTopics(), new StubMaterials([
            Material("Gardening", "sunlight soil watering")
        ]), Options.Create(new OllamaOptions { MaxContextCharacters = 2000 }));

        var context = await service.BuildRagContextAsync(topicId: 1, subjectId: null, query: "architecture layers dal");

        Assert.Empty(context.Sources);
        Assert.Equal(string.Empty, context.Text);
    }

    [Fact]
    public async Task RagContextIsEmptyWhenThereIsNoMaterialAtAll()
    {
        var service = new CatalogService(new StubSubjects(), new StubTopics(), new StubMaterials([]),
            Options.Create(new OllamaOptions()));

        Assert.Equal(RagContext.Empty, await service.BuildRagContextAsync(null, null, "anything"));
    }

    [Fact]
    public async Task MaterialMustHaveUsableContentAndTitle()
    {
        var service = new CatalogService(new StubSubjects(), new StubTopics(), new StubMaterials([]),
            Options.Create(new OllamaOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMaterialAsync(new MaterialInput(1, "ok", "short", null, MaterialSourceType.Manual)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateMaterialAsync(new MaterialInput(1, "x", new string('c', 50), null, MaterialSourceType.Manual)));
    }

    [Fact]
    public async Task CatalogNamesAreTrimmedBeforeTheyAreStored()
    {
        var store = new StubSubjects();
        var service = new CatalogService(store, new StubTopics(), new StubMaterials([]), Options.Create(new OllamaOptions()));

        var stored = await service.CreateSubjectAsync(new SubjectInput("  Software engineering  ", "  description  "));

        Assert.Equal("Software engineering", stored.Name);
        Assert.Equal("description", stored.Description);
    }

    private static MaterialDto Material(string title, string content) => new(
        1, 1, "Layers", "Software", title, content, null, MaterialSourceType.Manual, true,
        DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, content.Length);

    private sealed class StubGenerator(AiProvider provider, bool configured) : IQuestionGenerator
    {
        public int Calls
        {
            get; private set;
        }
        public AiProvider Provider { get; } = provider;
        public bool IsConfigured { get; } = configured;

        public Task<IReadOnlyList<GeneratedVivaQuestion>> GenerateAsync(QuestionGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<GeneratedVivaQuestion>>([
                new GeneratedVivaQuestion { Content = request.Topic, ExpectedAnswer = "answer", BloomLevel = "Understand", Difficulty = "Intermediate", FollowUpQuestions = ["a", "b"] }
            ]);
        }
    }

    private sealed class StubSubjects : ISubjectRepository
    {
        public Task<IReadOnlyList<SubjectDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SubjectDto>>([]);
        public Task<SubjectDto> AddAsync(SubjectInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SubjectDto(1, input.Name, input.Description, 0, 0));
        public Task<SubjectDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SubjectDto?>(null);
        public Task UpdateAsync(int id, SubjectInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubTopics : ITopicRepository
    {
        public Task<IReadOnlyList<TopicDto>> GetAllAsync(int? subjectId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TopicDto>>([]);
        public Task<TopicDto> AddAsync(TopicInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TopicDto(1, input.SubjectId, "subject", input.Name, input.Description, 0));
        public Task<TopicDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<TopicDto?>(null);
        public Task UpdateAsync(int id, TopicInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubMaterials(IReadOnlyList<MaterialDto> stored) : IMaterialRepository
    {
        public Task<IReadOnlyList<MaterialDto>> GetAllAsync(int? topicId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(stored);
        public Task<MaterialDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MaterialDto?>(stored.FirstOrDefault(material => material.Id == id));
        public Task<MaterialDto> AddAsync(MaterialInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Material(input.TopicId, input.Title, input.Content));
        public Task UpdateAsync(int id, MaterialInput input, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(stored.Count);
        public Task<IReadOnlyList<MaterialDto>> GetActiveForRagAsync(int? topicId, int? subjectId, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MaterialDto>>(stored.Where(material => material.IsActive).Take(limit).ToList());

        private static MaterialDto Material(int topicId, string title, string content) =>
            new(1, topicId, "Layers", "Software", title, content, null, MaterialSourceType.Manual, true,
                DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, content.Length);
    }
}