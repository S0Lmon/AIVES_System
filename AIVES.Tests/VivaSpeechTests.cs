extern alias Razor;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Recordings;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using RazorRealtime = Razor::AIVES.WebRazor.Realtime;

namespace AIVES.Tests;

public sealed class AudioTests
{
    [Fact]
    public void WavHeaderDescribesMono16BitPcm()
    {
        var wav = Audio.ToWav([0f, 0.5f, -0.5f, 1f], 16000);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(16000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal(8, BitConverter.ToInt32(wav, 40));
        Assert.Equal(44 + 8, wav.Length);
    }
}

/// <summary>
/// The viva page with the BLL replaced by fakes: questions are read by the server voice, answers are
/// submitted over HTTP, and lecturers watching the exam over SignalR are told about every step.
/// No database and no speech model needed.
/// </summary>
public sealed class InterviewHubTests : IClassFixture<InterviewHubTests.VivaApp>
{
    private const int CandidateId = 11;
    private const int ExamId = 7;
    private const string Student = "sinhvien@fpt.edu.vn";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly VivaApp app;

    public InterviewHubTests(VivaApp app)
    {
        this.app = app;
        app.Interviews.Reset();
        app.Voice.Reset();
    }

    [Fact]
    public async Task LecturerWatchingTheExamSeesQuestionsAnswersAndFollowUps()
    {
        await using var lecturer = await ConnectAsync("u-lecturer", "gv@fpt.edu.vn", AppRoles.Lecturer);
        var events = Channel.CreateUnbounded<RazorRealtime.InterviewEvent>();
        lecturer.On<RazorRealtime.InterviewEvent>("InterviewEvent", change => events.Writer.TryWrite(change));
        await lecturer.InvokeAsync("WatchExam", ExamId);

        using var student = StudentClient();
        var token = await TokenAsync(student);
        var started = await PostAsync(student, token, "Start", new { recordingConsent = false });
        var turnId = started.GetProperty("currentTurn").GetProperty("turnId").GetInt32();
        await PostAsync(student, token, "Answer", new { turnId, transcript = "Ba lớp là giao diện, nghiệp vụ, dữ liệu.", inputMode = 0 });

        Assert.Equal("started", (await ReadAsync(events.Reader)).Kind);
        Assert.Equal("question", (await ReadAsync(events.Reader)).Kind);
        var answer = await ReadAsync(events.Reader);
        Assert.Equal("answer", answer.Kind);
        Assert.Equal("Ba lớp là giao diện, nghiệp vụ, dữ liệu.", answer.Text);
        var followUp = await ReadAsync(events.Reader);
        Assert.Equal("followup", followUp.Kind);
        Assert.Equal(FakeInterviewService.FollowUpText, followUp.Text);
    }

    [Fact]
    public async Task StudentsCannotWatchAnExam()
    {
        await using var student = await ConnectAsync("u-student", Student, AppRoles.Student);

        await Assert.ThrowsAsync<HubException>(() => student.InvokeAsync("WatchExam", ExamId));
    }

    [Fact]
    public async Task TheOpenQuestionIsReadAloudByTheServerVoice()
    {
        using var student = StudentClient();
        var token = await TokenAsync(student);
        var started = await PostAsync(student, token, "Start", new { recordingConsent = false });
        var turn = started.GetProperty("currentTurn");
        var turnId = turn.GetProperty("turnId").GetInt32();

        var audio = await student.GetAsync($"/Interview/{CandidateId}/QuestionAudio/{turnId}");

        Assert.Equal(HttpStatusCode.OK, audio.StatusCode);
        Assert.Equal("audio/wav", audio.Content.Headers.ContentType?.MediaType);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(await audio.Content.ReadAsByteArrayAsync(), 0, 4));
        Assert.Equal(turn.GetProperty("questionText").GetString(), Assert.Single(app.Voice.Spoken));
    }

    [Fact]
    public async Task OnlyTheOpenQuestionOfTheCandidateCanBeReadAloud()
    {
        using var student = StudentClient();
        var token = await TokenAsync(student);
        await PostAsync(student, token, "Start", new { recordingConsent = false });

        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/Interview/{CandidateId}/QuestionAudio/999")).StatusCode);
        using var stranger = app.CreateClient();
        stranger.DefaultRequestHeaders.Add(RazorRealtimeTests.TestAuthHandler.UserHeader, "u-other");
        stranger.DefaultRequestHeaders.Add(RazorRealtimeTests.TestAuthHandler.NameHeader, "nguoikhac@fpt.edu.vn");
        stranger.DefaultRequestHeaders.Add(RazorRealtimeTests.TestAuthHandler.RoleHeader, AppRoles.Student);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/Interview/{CandidateId}/QuestionAudio/101")).StatusCode);
        Assert.Empty(app.Voice.Spoken);
    }

    private HttpClient StudentClient()
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(RazorRealtimeTests.TestAuthHandler.UserHeader, "u-student");
        client.DefaultRequestHeaders.Add(RazorRealtimeTests.TestAuthHandler.NameHeader, Student);
        client.DefaultRequestHeaders.Add(RazorRealtimeTests.TestAuthHandler.RoleHeader, AppRoles.Student);
        return client;
    }

    /// <summary>The page carries the antiforgery token that its JSON endpoints want in a header.</summary>
    private static async Task<string> TokenAsync(HttpClient client)
    {
        var page = await client.GetAsync($"/Interview/{CandidateId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var match = Regex.Match(await page.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<System.Text.Json.JsonElement> PostAsync(HttpClient client, string token, string action, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/Interview/{CandidateId}/{action}");
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = System.Net.Http.Json.JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<HubConnection> ConnectAsync(string userId, string name, string role)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(app.Server.BaseAddress, RazorRealtime.InterviewHub.Path), options =>
            {
                options.HttpMessageHandlerFactory = _ => app.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers[RazorRealtimeTests.TestAuthHandler.UserHeader] = userId;
                options.Headers[RazorRealtimeTests.TestAuthHandler.NameHeader] = name;
                options.Headers[RazorRealtimeTests.TestAuthHandler.RoleHeader] = role;
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
    {
        using var timeout = new CancellationTokenSource(Wait);
        return await reader.ReadAsync(timeout.Token);
    }

    public sealed class VivaApp : RazorRealtimeTests.RazorApp
    {
        public FakeInterviewService Interviews { get; } = new();
        public FakeTextToSpeech Voice { get; } = new();

        protected override void ConfigureFakes(IServiceCollection services)
        {
            services.RemoveAll<IInterviewService>();
            services.AddSingleton<IInterviewService>(Interviews);
            services.RemoveAll<IExamService>();
            services.AddSingleton<IExamService>(new FakeExamService());
            services.RemoveAll<IRecordingService>();
            services.AddSingleton<IRecordingService>(new FakeRecordingService());
            services.RemoveAll<ITextToSpeech>();
            services.AddSingleton<ITextToSpeech>(Voice);
            // The antiforgery token is protected with keys kept in the database, which these tests do not have.
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        }
    }

    public sealed class FakeTextToSpeech : ITextToSpeech
    {
        public ConcurrentQueue<string> Spoken { get; } = new();

        public void Reset() => Spoken.Clear();

        public bool IsAvailable(AppLanguage language) => true;

        public Task<byte[]> SynthesizeWavAsync(string text, AppLanguage language, double speed, CancellationToken cancellationToken = default)
        {
            Spoken.Enqueue(text);
            return Task.FromResult(Audio.ToWav([0f, 0.1f, -0.1f], 24000));
        }
    }

    /// <summary>Two main questions; the first answer always gets one follow-up.</summary>
    public sealed class FakeInterviewService : IInterviewService
    {
        public const string FollowUpText = "Em hãy nói rõ hơn vai trò của lớp nghiệp vụ?";
        private readonly Lock gate = new();
        private InterviewStatus status;
        private InterviewTurnDto? current;
        private int nextTurnId;

        public ConcurrentQueue<InterviewAnswerInput> Answers { get; } = new();

        public void Reset()
        {
            lock (gate)
            {
                status = InterviewStatus.NotStarted;
                current = null;
                nextTurnId = 100;
                Answers.Clear();
            }
        }

        public Task<InterviewStateDto> GetStateAsync(int candidateId, string email, CancellationToken cancellationToken = default)
        {
            Check(candidateId, email);
            lock (gate)
                return Task.FromResult(State());
        }

        public Task<InterviewStateDto> StartAsync(int candidateId, string email, bool recordingConsent = false, CancellationToken cancellationToken = default)
        {
            Check(candidateId, email);
            lock (gate)
            {
                if (status == InterviewStatus.NotStarted)
                {
                    status = InterviewStatus.InProgress;
                    current = Turn(TurnKind.Main, 1, 0, "Trình bày kiến trúc ba lớp trong ASP.NET Core.");
                }
                return Task.FromResult(State());
            }
        }

        public Task<InterviewStateDto> AnswerAsync(int candidateId, string email, InterviewAnswerInput input, CancellationToken cancellationToken = default)
        {
            Check(candidateId, email);
            lock (gate)
            {
                if (current is null || current.TurnId != input.TurnId)
                    return Task.FromResult(State());
                Answers.Enqueue(input);
                current = current switch
                {
                    { Kind: TurnKind.Main, MainIndex: 1 } => Turn(TurnKind.FollowUp, 1, 1, FollowUpText),
                    { Kind: TurnKind.FollowUp } => Turn(TurnKind.Main, 2, 0, "SignalR dùng để làm gì?"),
                    _ => null
                };
                if (current is null)
                    status = InterviewStatus.Completed;
                return Task.FromResult(State());
            }
        }

        private InterviewTurnDto Turn(TurnKind kind, int main, int followUp, string text) =>
            new(++nextTurnId, kind, text, main, 2, followUp, DateTime.UtcNow, 120, 2);

        private InterviewStateDto State() =>
            new(CandidateId, "Kỳ thi thử", AppLanguage.Vi, status, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(30), current, DateTime.UtcNow,
                Phrases: ["Razor Pages"]);

        private static void Check(int candidateId, string email)
        {
            if (candidateId != CandidateId || !string.Equals(email, Student, StringComparison.OrdinalIgnoreCase))
                throw new KeyNotFoundException("This exam slot was not found.");
        }
    }

    private sealed class FakeExamService : IExamService
    {
        public Task<IReadOnlyList<StudentExamDto>> ListForCandidateAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudentExamDto>>(string.Equals(email, Student, StringComparison.OrdinalIgnoreCase)
                ? [new StudentExamDto(ExamId, "Kỳ thi thử", "PRN222", null, 1, 1, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 2, 2, CandidateId)]
                : []);

        public Task<ExamDetailsDto?> GetAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExamDetailsDto?>(id == ExamId && actor.UserId == "u-lecturer"
                ? new ExamDetailsDto(ExamId, "Kỳ thi thử", 1, "PRN222", null, null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 15, 2, 4, "u-lecturer", [])
                : null);
        public Task<IReadOnlyList<ExamPoolQuestion>> GetQuestionBankAsync(int subjectId, int? topicId, int? rubricId, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ExamSummaryDto>> ListAsync(ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamSaveResult> CreateAsync(ExamInput input, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamSaveResult> UpdateAsync(int id, ExamInput input, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamSaveResult> ReassignQuestionsAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateCandidateQuestionAsync(int id, int candidateOrder, int questionOrder, int questionId, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AdjustSlotAsync(int id, int order, DateTime newStartsAtUtc, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetScheduleAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetCandidateStatusAsync(int id, int order, CandidateStatus? status, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountPoolAsync(int subjectId, int? topicId, int? rubricId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyList<string> ParseCandidateEmails(string? text) => throw new NotSupportedException();
    }

    private sealed class FakeRecordingService : IRecordingService
    {
        public Task SaveAsync(int candidateId, string email, int turnId, Stream content, string contentType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RecordingContent?> OpenAsync(int recordingId, ExamActor actor, CancellationToken cancellationToken = default) => Task.FromResult<RecordingContent?>(null);
        public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task DeleteForExamAsync(int examId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
