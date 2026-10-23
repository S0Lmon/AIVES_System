using AIVES.BLL.Services.Speech;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Net;
using System.Text.Json.Nodes;

namespace AIVES.Tests;

public sealed class VieNeuSpeechTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T08:00:00Z");

    private sealed record Harness(VieNeuSpeechClient Client, HybridTextToSpeech Voice, StubHandler Handler, MovableClock Clock, MemoryCache Cache);

    private static Harness Create(string? voice = "Ngọc Huyền", string? url = "http://vieneu:8000",
        SpeechProvider provider = SpeechProvider.VieNeu, bool readsEnglish = false)
    {
        var handler = new StubHandler();
        var clock = new MovableClock(Now);
        var options = Options.Create(new SpeechOptions
        {
            TtsProvider = provider,
            VieNeuUrl = url,
            VieNeuVoice = voice,
            VieNeuTimeoutSeconds = 1,
            VieNeuPrepareTimeoutSeconds = 30,
            VieNeuReadsEnglish = readsEnglish,
            // No Piper models in the test output, so the local voice only answers from the cache.
            ModelsPath = Path.Combine(AppContext.BaseDirectory, "no-speech-models")
        });
        var client = new VieNeuSpeechClient(new StubFactory(handler), options, clock, NullLogger<VieNeuSpeechClient>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var local = new SherpaTextToSpeech(options, new HostEnvironmentStub(), cache, NullLogger<SherpaTextToSpeech>.Instance);
        var gemini = new GeminiSpeechClient(new StubFactory(handler), Options.Create(new AIVES.BLL.Services.Gemini.GeminiOptions()),
            options, clock, NullLogger<GeminiSpeechClient>.Instance);
        return new Harness(client, new HybridTextToSpeech(local, gemini, client, options, cache), handler, clock, cache);
    }

    /// <summary>16-bit mono PCM of the given length, as VieNeu sends with response_format "pcm".</summary>
    private static byte[] Pcm(int samples)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)(i % 2000));
        return bytes;
    }

    /// <summary>What the local Piper voice would return, put where SherpaTextToSpeech looks first.</summary>
    private static byte[] SeedLocal(Harness h, string text)
    {
        var wav = Audio.ToWav(new float[160], 16_000);
        h.Cache.Set((nameof(SherpaTextToSpeech), AppLanguage.Vi, 1.0, text.Trim()), wav);
        return wav;
    }

    [Fact]
    public async Task AsksForPcmInTheConfiguredVoiceAndWrapsItInAWavOfTheReportedRate()
    {
        var h = Create();
        h.Handler.Respond(HttpStatusCode.OK, Pcm(2400), sampleRate: 24_000);

        var wav = await h.Client.SynthesizeWavAsync("Lớp nghiệp vụ có vai trò gì?");

        var request = Assert.Single(h.Handler.Requests);
        Assert.Equal("/v1/audio/speech", request.Path);
        var body = JsonNode.Parse(request.Body)!;
        Assert.Equal("Lớp nghiệp vụ có vai trò gì?", (string?)body["input"]);
        Assert.Equal("Ngọc Huyền", (string?)body["voice"]);
        Assert.Equal("pcm", (string?)body["response_format"]);
        Assert.Equal(24_000, (int?)body["sample_rate"]);
        Assert.NotNull(wav);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(24_000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(2400 * 2, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
    }

    [Fact]
    public async Task WithoutAVoiceTheServerDefaultIsUsed()
    {
        var h = Create(voice: " ");
        h.Handler.Respond(HttpStatusCode.OK, Pcm(10));

        await h.Client.SynthesizeWavAsync("Câu hỏi");

        Assert.False(JsonNode.Parse(Assert.Single(h.Handler.Requests).Body)!.AsObject().ContainsKey("voice"));
    }

    [Fact]
    public async Task ABusyServerIsLeftAloneForItsRetryAfter()
    {
        var h = Create();
        h.Handler.Respond(HttpStatusCode.TooManyRequests, [], retryAfterSeconds: 3);

        Assert.Null(await h.Client.SynthesizeWavAsync("Câu một"));
        Assert.Null(await h.Client.SynthesizeWavAsync("Câu hai"));
        Assert.Single(h.Handler.Requests);

        h.Clock.Advance(TimeSpan.FromSeconds(4));
        h.Handler.Respond(HttpStatusCode.OK, Pcm(10));
        Assert.NotNull(await h.Client.SynthesizeWavAsync("Câu ba"));
    }

    [Fact]
    public async Task ASlowVoiceGivesUpAtTheTimeout()
    {
        var h = Create();
        h.Handler.Respond(HttpStatusCode.OK, Pcm(10));
        h.Handler.Delay = TimeSpan.FromSeconds(5);

        var started = DateTime.UtcNow;
        Assert.Null(await h.Client.SynthesizeWavAsync("Câu hỏi chậm"));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
    }

    [Theory]
    [InlineData(SpeechProvider.Local, "http://vieneu:8000")]
    [InlineData(SpeechProvider.VieNeu, null)]
    public async Task ItIsOnlyCalledWhenSwitchedOnWithAnAddress(SpeechProvider provider, string? url)
    {
        var h = Create(url: url, provider: provider);

        Assert.False(h.Client.IsEnabled);
        Assert.Null(await h.Client.SynthesizeWavAsync("Câu hỏi"));
        Assert.Empty(h.Handler.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EnglishQuestionsUseItOnlyWhenAsked(bool readsEnglish, bool expected)
    {
        var h = Create(readsEnglish: readsEnglish);

        Assert.True(h.Client.Reads(AppLanguage.Vi));
        Assert.Equal(expected, h.Client.Reads(AppLanguage.En));
    }

    [Fact]
    public async Task TheSameQuestionIsSynthesisedOnceAndThenServedFromTheCache()
    {
        var h = Create();
        h.Handler.Respond(HttpStatusCode.OK, Pcm(100));

        var first = await h.Voice.SynthesizeWavAsync("Giải thích mô hình ba lớp.", AppLanguage.Vi, 1);
        var second = await h.Voice.SynthesizeWavAsync("Giải thích mô hình ba lớp. ", AppLanguage.Vi, 1);

        Assert.Equal(first, second);
        Assert.Single(h.Handler.Requests);
    }

    [Fact]
    public async Task WhenVieNeuFailsTheLocalVoiceReadsTheQuestion()
    {
        var h = Create();
        var local = SeedLocal(h, "Câu hỏi xoáy?");
        h.Handler.Respond(HttpStatusCode.ServiceUnavailable, []);

        Assert.Equal(local, await h.Voice.SynthesizeWavAsync("Câu hỏi xoáy?", AppLanguage.Vi, 1));
    }

    [Fact]
    public async Task APreparedQuestionIsReadWithoutWaiting()
    {
        var h = Create();
        h.Handler.Respond(HttpStatusCode.OK, Pcm(100));
        // Slower than the 1 s live timeout, but within the 30 s allowed when preparing.
        h.Handler.Delay = TimeSpan.FromSeconds(1.5);

        Assert.True(await h.Voice.PrepareAsync("Câu hỏi chính số một?", AppLanguage.Vi));
        h.Handler.Delay = TimeSpan.Zero;
        h.Handler.Respond(HttpStatusCode.ServiceUnavailable, []);
        var wav = await h.Voice.SynthesizeWavAsync("Câu hỏi chính số một?", AppLanguage.Vi, 1);

        Assert.Equal(44 + 200, wav.Length);
        Assert.Single(h.Handler.Requests);
        Assert.False(await h.Voice.PrepareAsync("An English question?", AppLanguage.En));
    }

    [Fact]
    public async Task UpcomingQuestionsSkipFinishedAbsentEndedAndFarAwaySlots()
    {
        using var db = Database();
        await AssertUpcomingAsync(db);
    }

    /// <summary>The same query on SQL Server, where EF must translate the computed slot times.</summary>
    [SqlFact]
    public async Task UpcomingQuestionsQueryRunsOnSqlServer()
    {
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AIVES_TEST_SQL_CONNECTION"))
        {
            InitialCatalog = "AIVES_LayerTest_" + Guid.NewGuid().ToString("N")
        };
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options);
        try
        {
            await db.Database.MigrateAsync();
            await AssertUpcomingAsync(db);
        }
        finally
        {
            Assert.StartsWith("AIVES_LayerTest_", connection.InitialCatalog);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertUpcomingAsync(ApplicationDbContext db)
    {
        var now = Now.UtcDateTime;
        Seed(db, "Đang thi", now.AddMinutes(-5), 30);
        Seed(db, "Sắp thi", now.AddMinutes(20), 30);
        Seed(db, "Còn xa", now.AddHours(5), 30);
        Seed(db, "Đã hết giờ", now.AddMinutes(-60), 30);
        Seed(db, "Đã thi xong", now.AddMinutes(-5), 30, attempt: InterviewStatus.Completed);
        Seed(db, "Vắng", now.AddMinutes(10), 30, status: CandidateStatus.Absent);

        var upcoming = await new InterviewRepository(db).ListUpcomingQuestionsAsync(now, now.AddHours(2), 50);

        Assert.Equal(["Đang thi 1", "Đang thi 2", "Sắp thi 1", "Sắp thi 2"], upcoming.Select(question => question.Text));
        Assert.All(upcoming, question => Assert.Equal("vi-VN", question.Language));
    }

    [Fact]
    public async Task TheWorkerPreparesEachUpcomingQuestionOnce()
    {
        var h = Create();
        h.Handler.Respond(HttpStatusCode.OK, Pcm(10));
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(name))
            .AddScoped<IInterviewRepository, InterviewRepository>()
            .BuildServiceProvider();
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Seed(db, "Câu chung", Now.UtcDateTime.AddMinutes(10), 15);
            Seed(db, "Câu chung", Now.UtcDateTime.AddMinutes(25), 15);
        }
        var worker = new QuestionAudioWorker(services.GetRequiredService<IServiceScopeFactory>(), h.Voice, h.Client,
            Options.Create(new SpeechOptions { PrepareMinutesAhead = 60 }), h.Clock, NullLogger<QuestionAudioWorker>.Instance);

        Assert.Equal(2, await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, h.Handler.Requests.Count);    // "Câu chung 1" and "Câu chung 2", shared by both candidates
        Assert.Equal(2, await worker.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, h.Handler.Requests.Count);    // the second pass is served from the cache
    }

    [LiveVieNeuFact]
    public async Task ARealVieNeuServerReadsAVietnameseQuestion()
    {
        var url = Environment.GetEnvironmentVariable("AIVES_TEST_VIENEU_URL")!;
        var services = new ServiceCollection().AddHttpClient(VieNeuSpeechClient.HttpClientName,
            client => client.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/")).Services.BuildServiceProvider();
        var client = new VieNeuSpeechClient(services.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new SpeechOptions { TtsProvider = SpeechProvider.VieNeu, VieNeuUrl = url, VieNeuVoice = "Mai Anh" }),
            TimeProvider.System, NullLogger<VieNeuSpeechClient>.Instance);

        var wav = await client.SynthesizeWavAsync("Vì sao lớp giao diện không nên gọi thẳng cơ sở dữ liệu?", TimeSpan.FromSeconds(60));

        Assert.NotNull(wav);
        var seconds = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)) / 2.0 / BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24));
        Assert.InRange(seconds, 1.5, 10);
        Assert.True(Audio.Rms(Audio.FromPcm16(wav.AsSpan(44))) > 0.01, "The audio should not be silent.");
    }

    private static ApplicationDbContext Database() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void Seed(ApplicationDbContext db, string prefix, DateTime slotStartsUtc, int slotMinutes,
        InterviewStatus? attempt = null, CandidateStatus? status = null)
    {
        var candidate = new ExamCandidate { Order = 1, Email = $"{Guid.NewGuid():N}@fpt.edu.vn", SlotStartsAtUtc = slotStartsUtc, StatusOverride = status };
        for (var i = 1; i <= 2; i++)
            candidate.Questions.Add(new ExamCandidateQuestion { Order = i, Content = $"{prefix} {i}", ExpectedAnswer = "x", BloomLevelName = "Understand" });
        if (attempt is { } attemptStatus)
            candidate.Attempt = new ExamAttempt { Status = attemptStatus, StartedAtUtc = slotStartsUtc };
        db.Exams.Add(new Exam
        {
            Title = prefix,
            SubjectName = "Java",
            StartsAtUtc = slotStartsUtc,
            SlotMinutes = slotMinutes,
            MainQuestionCount = 2,
            Language = "vi-VN",
            CreatedById = "lecturer",
            Candidates = { candidate }
        });
        db.SaveChanges();
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("http://vieneu:8000/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.OK;
        private byte[] body = [];
        private int? sampleRate;
        private int? retryAfter;

        public sealed record Seen(string Path, string Body);

        public List<Seen> Requests { get; } = [];
        public TimeSpan Delay
        {
            get; set;
        }

        public void Respond(HttpStatusCode code, byte[] content, int? sampleRate = null, int? retryAfterSeconds = null)
        {
            status = code;
            body = content;
            this.sampleRate = sampleRate;
            retryAfter = retryAfterSeconds;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
            if (sampleRate is { } rate)
                response.Headers.Add("X-Sample-Rate", rate.ToString());
            if (retryAfter is { } seconds)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            return response;
        }
    }

    private sealed class HostEnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AIVES.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

public sealed class LiveVieNeuFactAttribute : FactAttribute
{
    public LiveVieNeuFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIVES_TEST_VIENEU_URL")))
            Skip = "Set AIVES_TEST_VIENEU_URL (e.g. http://127.0.0.1:8000) to read one question with a running VieNeu server.";
    }
}
