extern alias Razor;

using System.Collections.Concurrent;
using System.Threading.Channels;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Recordings;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RazorRealtime = Razor::AIVES.WebRazor.Realtime;

namespace AIVES.Tests;

/// <summary>Records every call and answers "preview" or "final" text, so tests see which ran.</summary>
internal sealed class FakeSpeechToText : ISpeechToText
{
    public ConcurrentQueue<(int Samples, bool Preview, string? Prompt)> Calls { get; } = new();

    public bool IsAvailable { get; set; } = true;

    public Task<string> TranscribeAsync(ReadOnlyMemory<float> samples, AppLanguage language, string? prompt, bool preview,
        CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((samples.Length, preview, prompt));
        if (!Audio.HasSpeech(samples.Span))
            return Task.FromResult(string.Empty);
        return Task.FromResult(preview ? $"tạm{Calls.Count}" : $"đoạn{Calls.Count(call => !call.Preview)}");
    }
}

public sealed class LiveTranscriberTests
{
    private static readonly SpeechOptions Options = new() { PreviewEverySeconds = 1, CommitAfterSeconds = 4, MaxWindowSeconds = 6 };

    /// <summary>A tone for "speech" and zeros for a pause.</summary>
    private static float[] Tone(double seconds, float level = 0.3f) =>
        Enumerable.Range(0, (int)(seconds * Audio.SampleRate)).Select(i => level * MathF.Sin(i * 0.05f)).ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * Audio.SampleRate)];

    [Fact]
    public async Task PreviewIsRefreshedOnlyAfterEnoughNewAudio()
    {
        var speech = new FakeSpeechToText();
        var transcriber = new LiveTranscriber(speech, Options, AppLanguage.Vi, "prompt", 60);

        transcriber.Append(Tone(0.5));
        Assert.Null(await transcriber.UpdateAsync());

        transcriber.Append(Tone(0.6));
        var text = await transcriber.UpdateAsync();

        Assert.Equal("tạm1", text);
        Assert.All(speech.Calls, call => Assert.True(call.Preview));
        Assert.All(speech.Calls, call => Assert.Equal("prompt", call.Prompt));
    }

    [Fact]
    public async Task LongAnswersAreCommittedAtAPauseSoEachCallStaysShort()
    {
        var speech = new FakeSpeechToText();
        var transcriber = new LiveTranscriber(speech, Options, AppLanguage.Vi, null, 60);

        // 3 s speech, a pause, 1.5 s speech: the 4 s commit should cut inside the pause.
        foreach (var part in new[] { Tone(3), Silence(0.4), Tone(1.5) })
        {
            transcriber.Append(part);
            await transcriber.UpdateAsync();
        }

        var commit = speech.Calls.First(call => !call.Preview);
        Assert.InRange(commit.Samples / (double)Audio.SampleRate, 3.0, 3.4);
        Assert.All(speech.Calls, call => Assert.True(call.Samples <= Options.MaxWindowSeconds * Audio.SampleRate));
    }

    [Fact]
    public async Task FinishTranscribesTheRestWithTheFullModelAndJoinsTheWindows()
    {
        var speech = new FakeSpeechToText();
        var transcriber = new LiveTranscriber(speech, Options, AppLanguage.Vi, null, 60);
        transcriber.Append(Tone(5));
        await transcriber.UpdateAsync();
        transcriber.Append(Tone(2));

        var final = await transcriber.FinishAsync();

        Assert.Equal("đoạn1 đoạn2", final);
        Assert.False(speech.Calls.Last().Preview);
        Assert.Equal(final, transcriber.Text);
    }

    [Fact]
    public async Task SilenceProducesNoText()
    {
        var transcriber = new LiveTranscriber(new FakeSpeechToText(), Options, AppLanguage.Vi, null, 60);
        transcriber.Append(Silence(3));

        Assert.Equal(string.Empty, await transcriber.FinishAsync());
    }

    [Fact]
    public void AudioPastTheTimeLimitIsIgnored()
    {
        var transcriber = new LiveTranscriber(new FakeSpeechToText(), Options, AppLanguage.Vi, null, maxSeconds: 2);
        transcriber.Append(Tone(1.5));
        transcriber.Append(Tone(1.5));

        Assert.True(transcriber.IsFull);
        Assert.Equal(2.0, transcriber.Seconds, 3);
    }
}

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

    [Fact]
    public void Pcm16RoundTripsThroughFloats()
    {
        var bytes = new byte[] { 0x00, 0x40, 0x00, 0xC0 };
        var samples = Audio.FromPcm16(bytes);

        Assert.Equal([0.5f, -0.5f], samples);
    }

    [Fact]
    public void ResampleKeepsTheDuration()
    {
        var resampled = Audio.Resample(new float[22050], 22050, 16000);
        Assert.Equal(16000, resampled.Length);
    }

    [Theory]
    [InlineData("Hãy subscribe cho kênh Ghiền Mì Gõ để không bỏ lỡ", true)]
    [InlineData("Cảm ơn các bạn đã theo dõi", true)]
    [InlineData(" www.thichews.ac.com", true)]
    [InlineData("SignalR dùng WebSocket.", false)]
    [InlineData("Kiến trúc ba lớp gồm presentation, business và data access", false)]
    public void KnownWhisperHallucinationsAreDropped(string text, bool hallucination) =>
        Assert.Equal(hallucination, WhisperSpeechToText.IsHallucination(text));

    [Fact]
    public void CleanRemovesTagsAndExtraSpaces() =>
        Assert.Equal("xin chào thầy", WhisperSpeechToText.Clean("  [âm nhạc] xin   chào (nhạc nền) thầy "));
}

/// <summary>
/// The viva over SignalR with the BLL replaced by fakes: audio in, transcript out, answers submitted,
/// lecturers notified. No database and no speech model needed.
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
    }

    [Fact]
    public async Task SpokenAnswerIsTranscribedLiveAndSubmittedAsSpeech()
    {
        await using var student = await ConnectAsync("u-student", Student, AppRoles.Student);
        var transcripts = Channel.CreateUnbounded<(int TurnId, string Text, bool Final)>();
        student.On<int, string, bool>("Transcript", (turn, text, final) => transcripts.Writer.TryWrite((turn, text, final)));

        var started = await student.InvokeAsync<InterviewStateDto>("Start", CandidateId, false);
        Assert.Equal(InterviewStatus.InProgress, started.Status);
        var turnId = started.CurrentTurn!.TurnId;

        // 2.5 s of "speech" in 250 ms chunks: enough for a live preview, then a final transcript.
        await student.SendAsync("StreamAnswer", CandidateId, turnId, Chunks(2.5));
        var partial = await ReadUntilAsync(transcripts.Reader, item => !item.Final);
        Assert.StartsWith("tạm", partial.Text);
        var final = await ReadUntilAsync(transcripts.Reader, item => item.Final);
        Assert.Equal(turnId, final.TurnId);
        Assert.False(string.IsNullOrWhiteSpace(final.Text));

        var next = await student.InvokeAsync<InterviewStateDto>("SubmitAnswer", CandidateId, turnId, final.Text, 300);

        var answer = Assert.Single(app.Interviews.Answers);
        Assert.Equal(final.Text, answer.Transcript);
        Assert.Equal(AnswerInputMode.Speech, answer.InputMode);
        Assert.NotNull(answer.SpeakingMs);
        // The fake examiner always probes once: the next turn is a follow-up of the same question.
        Assert.Equal(TurnKind.FollowUp, next.CurrentTurn!.Kind);
        Assert.Equal(1, next.CurrentTurn.MainIndex);
    }

    [Fact]
    public async Task EditedTranscriptIsRecordedAsTyped()
    {
        await using var student = await ConnectAsync("u-student", Student, AppRoles.Student);
        var finals = Channel.CreateUnbounded<string>();
        student.On<int, string, bool>("Transcript", (_, text, final) => { if (final) finals.Writer.TryWrite(text); });
        var turnId = (await student.InvokeAsync<InterviewStateDto>("Start", CandidateId, false)).CurrentTurn!.TurnId;

        await student.SendAsync("StreamAnswer", CandidateId, turnId, Chunks(1));
        var final = await ReadAsync(finals.Reader);
        await student.InvokeAsync<InterviewStateDto>("SubmitAnswer", CandidateId, turnId, final + " và thêm ý gõ tay", null);

        Assert.Equal(AnswerInputMode.Typed, Assert.Single(app.Interviews.Answers).InputMode);
    }

    [Fact]
    public async Task StreamingForAQuestionThatIsNotOpenIsRefused()
    {
        await using var student = await ConnectAsync("u-student", Student, AppRoles.Student);
        await student.InvokeAsync<InterviewStateDto>("Start", CandidateId, false);

        await Assert.ThrowsAsync<HubException>(() => student.InvokeAsync("StreamAnswer", CandidateId, 999, Chunks(0.5)));
    }

    [Fact]
    public async Task OtherPeopleCannotDriveSomeoneElsesViva()
    {
        await using var stranger = await ConnectAsync("u-other", "nguoikhac@fpt.edu.vn", AppRoles.Student);

        await Assert.ThrowsAsync<HubException>(() => stranger.InvokeAsync<InterviewStateDto>("Start", CandidateId, false));
    }

    [Fact]
    public async Task LecturerWatchingTheExamSeesQuestionsAnswersAndFollowUps()
    {
        await using var lecturer = await ConnectAsync("u-lecturer", "gv@fpt.edu.vn", AppRoles.Lecturer);
        var events = Channel.CreateUnbounded<RazorRealtime.InterviewEvent>();
        lecturer.On<RazorRealtime.InterviewEvent>("InterviewEvent", change => events.Writer.TryWrite(change));
        await lecturer.InvokeAsync("WatchExam", ExamId);

        await using var student = await ConnectAsync("u-student", Student, AppRoles.Student);
        var turnId = (await student.InvokeAsync<InterviewStateDto>("Start", CandidateId, false)).CurrentTurn!.TurnId;
        await student.InvokeAsync<InterviewStateDto>("SubmitAnswer", CandidateId, turnId, "Ba lớp là giao diện, nghiệp vụ, dữ liệu.", null);

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

    private static async IAsyncEnumerable<string> Chunks(double seconds)
    {
        var total = (int)(seconds * Audio.SampleRate);
        const int chunk = Audio.SampleRate / 4;
        for (var start = 0; start < total; start += chunk)
        {
            var bytes = new byte[Math.Min(chunk, total - start) * 2];
            for (var i = 0; i < bytes.Length / 2; i++)
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), (short)(9000 * Math.Sin((start + i) * 0.05)));
            yield return Convert.ToBase64String(bytes);
            await Task.Yield();
        }
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

    private static async Task<T> ReadUntilAsync<T>(ChannelReader<T> reader, Func<T, bool> match)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            var item = await reader.ReadAsync(timeout.Token);
            if (match(item))
                return item;
        }
    }

    public sealed class VivaApp : RazorRealtimeTests.RazorApp
    {
        public FakeInterviewService Interviews { get; } = new();

        protected override void ConfigureFakes(IServiceCollection services)
        {
            services.RemoveAll<IInterviewService>();
            services.AddSingleton<IInterviewService>(Interviews);
            services.RemoveAll<IExamService>();
            services.AddSingleton<IExamService>(new FakeExamService());
            services.RemoveAll<IRecordingService>();
            services.AddSingleton<IRecordingService>(new FakeRecordingService());
            services.RemoveAll<ISpeechToText>();
            services.AddSingleton<ISpeechToText>(new FakeSpeechToText());
            // Short windows so a couple of seconds of test audio exercise the preview.
            services.Configure<SpeechOptions>(options => options.PreviewEverySeconds = 1);
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
                    current = Turn(TurnKind.Main, 1, 0, "Trình bày kiến trúc ba lớp.");
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
            new(CandidateId, "Kỳ thi thử", AppLanguage.Vi, status, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(30), current, DateTime.UtcNow);

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

        public Task<IReadOnlyList<ExamSummaryDto>> ListAsync(ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamSaveResult> CreateAsync(ExamInput input, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamSaveResult> UpdateAsync(int id, ExamInput input, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ExamSaveResult> ReassignQuestionsAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AdjustSlotAsync(int id, int order, DateTime newStartsAtUtc, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetScheduleAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetCandidateStatusAsync(int id, int order, CandidateStatus? status, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, ExamActor actor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountPoolAsync(int subjectId, int? topicId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

/// <summary>
/// Real models, end to end: the Vietnamese voice reads a sentence and Whisper hears it back.
/// Set AIVES_SPEECH_MODELS to the models folder (see scripts/download-speech-models.ps1) to run.
/// </summary>
public sealed class SpeechModelTests
{
    private static readonly string? Models = Environment.GetEnvironmentVariable("AIVES_SPEECH_MODELS");

    public sealed class NeedsModelsFact : FactAttribute
    {
        public NeedsModelsFact()
        {
            if (string.IsNullOrWhiteSpace(Models) || !Directory.Exists(Models))
                Skip = "Set AIVES_SPEECH_MODELS to a folder with the Whisper and Piper models.";
        }
    }

    [NeedsModelsFact]
    public async Task VietnameseQuestionReadAloudIsRecognisedAgain()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SpeechOptions { ModelsPath = Models! });
        var environment = new HostEnvironmentStub();
        using var voice = new SherpaTextToSpeech(options, environment, new MemoryCache(new MemoryCacheOptions()), NullLogger<SherpaTextToSpeech>.Instance);
        using var whisper = new WhisperSpeechToText(options, environment, NullLogger<WhisperSpeechToText>.Instance);
        Assert.True(voice.IsAvailable(AppLanguage.Vi));
        Assert.True(whisper.IsAvailable);

        var wav = await voice.SynthesizeWavAsync("Hãy giải thích vai trò của từng lớp trong kiến trúc ba lớp.", AppLanguage.Vi, 1);
        var samples = Audio.FromPcm16(wav.AsSpan(44));
        var sampleRate = BitConverter.ToInt32(wav, 24);
        var text = await whisper.TranscribeAsync(Audio.Resample(samples, sampleRate, Audio.SampleRate), AppLanguage.Vi, null, preview: false);

        // Synthetic speech often lands on a neighbouring tone ("vài trò"), so compare without diacritics.
        var compare = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        const System.Globalization.CompareOptions loose = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
        Assert.True(compare.IndexOf(text, "giai thich", loose) >= 0, text);
        Assert.True(compare.IndexOf(text, "lop", loose) >= 0, text);
    }

    private sealed class HostEnvironmentStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AIVES.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
