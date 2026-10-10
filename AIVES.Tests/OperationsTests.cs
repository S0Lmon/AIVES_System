using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Import;
using AIVES.BLL.Services.Interview;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;

namespace AIVES.Tests;

public sealed class RecordingAndInterviewTests
{
    private const string Email = "student@fpt.edu.vn";
    private static readonly DateTime Now = new(2026, 10, 4, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Current);
    }

    private sealed class SufficientGenerator : IFollowUpGenerator
    {
        public List<FollowUpRequest> Requests { get; } = [];
        public bool IsConfigured => true;
        public Task<FollowUpDecision> DecideAsync(FollowUpRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FollowUpDecision(false, FollowUpReason.Sufficient, null));
        }
    }

    private static (ApplicationDbContext Db, FixedClock Clock, int CandidateId, int SubjectId) Build(bool recordAudio)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        var subject = new Subject { Name = "Databases" };
        db.Subjects.Add(subject);
        db.SaveChanges();
        db.GlossaryTerms.Add(new GlossaryTerm { SubjectId = subject.Id, Term = "SQL", SpokenForms = "ét kiu eo;sequel" });
        var candidate = new ExamCandidate { Order = 1, Email = Email };
        candidate.Questions.Add(new ExamCandidateQuestion { Order = 1, Content = "What is SQL?", ExpectedAnswer = "A query language", BloomLevelName = "Remember" });
        db.Exams.Add(new Exam
        {
            Title = "Viva",
            SubjectId = subject.Id,
            SubjectName = "Databases",
            StartsAtUtc = Now.AddMinutes(-1),
            SlotMinutes = 30,
            MainQuestionCount = 1,
            MaxFollowUpQuestions = 2,
            Language = "vi-VN",
            CreatedById = "owner",
            RecordAudio = recordAudio,
            Candidates = { candidate }
        });
        db.SaveChanges();
        return (db, new FixedClock(Now), candidate.Id, subject.Id);
    }

    private static InterviewService Interview(ApplicationDbContext db, FixedClock clock, IFollowUpGenerator ai) => new(
        new InterviewRepository(db), ai, TestServices.Glossary(db), TestServices.Settings(db, clock), TestServices.Audit(db, clock),
        Options.Create(new InterviewOptions()), clock, NullLogger<InterviewService>.Instance);

    [Fact]
    public async Task ARecordedVivaNeedsConsentToStart()
    {
        var (db, clock, candidateId, _) = Build(recordAudio: true);
        var service = Interview(db, clock, new SufficientGenerator());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(candidateId, Email));
        var state = await service.StartAsync(candidateId, Email, recordingConsent: true);

        Assert.Equal(RecordingMode.Audio, state.Recording);
        Assert.True(state.RecordingConsentGiven);
        Assert.Equal(["SQL"], state.Phrases);
        Assert.NotNull(state.Speech);
        Assert.Contains(db.AuditEntries, entry => entry.Action == AuditActions.InterviewStarted && entry.Details!.Contains("Audio"));
    }

    [Fact]
    public async Task MisheardTermsAreCorrectedAndTimingsAreKept()
    {
        var (db, clock, candidateId, _) = Build(recordAudio: false);
        var ai = new SufficientGenerator();
        var service = Interview(db, clock, ai);
        var state = await service.StartAsync(candidateId, Email);

        clock.Current = Now.AddSeconds(30);
        var done = await service.AnswerAsync(candidateId, Email, new InterviewAnswerInput(state.CurrentTurn!.TurnId, "ét kiu eo là ngôn ngữ truy vấn", AnswerInputMode.Speech, 1800, 12000));

        Assert.Equal(InterviewStatus.Completed, done.Status);
        var turn = db.ExamTurns.Single();
        Assert.Equal("SQL là ngôn ngữ truy vấn", turn.Answer);
        Assert.Equal("ét kiu eo là ngôn ngữ truy vấn", turn.RawAnswer);
        Assert.Equal(1800, turn.ResponseDelayMs);
        Assert.Equal(12000, turn.SpeakingMs);
        Assert.NotNull(turn.DecisionLatencyMs);
        Assert.Equal(["SQL"], ai.Requests.Single().Glossary);
        Assert.Contains(db.AuditEntries, entry => entry.Action == AuditActions.InterviewCompleted);
    }

    [Fact]
    public async Task RecordingsAreEncryptedAndOnlyTheExamOwnerCanPlayThem()
    {
        var (db, clock, candidateId, _) = Build(recordAudio: true);
        var service = Interview(db, clock, new SufficientGenerator());
        var state = await service.StartAsync(candidateId, Email, recordingConsent: true);
        var store = new MemoryRecordingStore();
        var recordings = TestServices.Recordings(db, clock, store);
        var audio = Encoding.UTF8.GetBytes("pretend this is opus audio");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            recordings.SaveAsync(candidateId, "other@fpt.edu.vn", state.CurrentTurn!.TurnId, new MemoryStream(audio), "audio/webm"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            recordings.SaveAsync(candidateId, Email, state.CurrentTurn!.TurnId, new MemoryStream(audio), "video/webm"));
        await recordings.SaveAsync(candidateId, Email, state.CurrentTurn!.TurnId, new MemoryStream(audio), "audio/webm;codecs=opus");

        var stored = Assert.Single(store.Files).Value;
        Assert.NotEqual(audio, stored);
        var recordingId = db.TurnRecordings.Single().Id;
        Assert.Null(await recordings.OpenAsync(recordingId, new ExamActor("someone-else", false)));
        var opened = await recordings.OpenAsync(recordingId, new ExamActor("owner", false, "owner@fpt.edu.vn"));
        Assert.Equal(audio, opened!.Data);
        Assert.Equal("audio/webm", opened.ContentType);
        Assert.Contains(db.AuditEntries, entry => entry.Action == AuditActions.RecordingViewed && entry.ActorEmail == "owner@fpt.edu.vn");

        // Past the retention period the file and its row are deleted.
        clock.Current = Now.AddDays(400);
        Assert.Equal(1, await recordings.PurgeExpiredAsync());
        Assert.Empty(store.Files);
        Assert.Empty(db.TurnRecordings);
    }

    [Fact]
    public async Task NothingIsRecordedWithoutConsentOrWhenTheExamIsNotRecorded()
    {
        var (db, clock, candidateId, _) = Build(recordAudio: false);
        var state = await Interview(db, clock, new SufficientGenerator()).StartAsync(candidateId, Email);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TestServices.Recordings(db, clock).SaveAsync(candidateId, Email, state.CurrentTurn!.TurnId, new MemoryStream([1, 2, 3]), "audio/webm"));
    }

    [Fact]
    public async Task SubjectsWithAssignedLecturersAreRestricted()
    {
        var (db, _, _, subjectId) = Build(recordAudio: false);
        var access = TestServices.SubjectAccess(db);
        var lecturer = new ExamActor("l1", false);
        Assert.True(await access.CanUseAsync(subjectId, lecturer));

        db.Users.Add(new ApplicationUser { Id = "l2", UserName = "l2@x.vn", Email = "l2@x.vn" });
        db.SaveChanges();
        await access.SetLecturersAsync(subjectId, ["l2"]);
        Assert.False(await access.CanUseAsync(subjectId, lecturer));
        Assert.True(await access.CanUseAsync(subjectId, new ExamActor("l2", false)));
        Assert.True(await access.CanUseAsync(subjectId, new ExamActor("admin", true)));
    }

    [Fact]
    public async Task SpeechSettingsAreValidatedAndStored()
    {
        var (db, clock, _, _) = Build(recordAudio: false);
        var settings = TestServices.Settings(db, clock);
        Assert.Equal(SpeechSettingsDto.Default.DefaultLanguage, (await settings.GetSpeechAsync()).DefaultLanguage);

        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveSpeechAsync(SpeechSettingsDto.Default with { EnabledLanguages = [AppLanguage.En] }));
        await settings.SaveSpeechAsync(new SpeechSettingsDto(AppLanguage.En, [AppLanguage.En], 1.1, null, "Google US English", 90));
        var saved = await TestServices.Settings(db, clock).GetSpeechAsync();
        Assert.Equal(AppLanguage.En, saved.DefaultLanguage);
        Assert.Equal([AppLanguage.En], saved.EnabledLanguages);
        Assert.Equal(1.1, saved.SpeechRate, 3);
        Assert.Equal("Google US English", saved.EnglishVoice);
        Assert.Equal(90, saved.RecordingRetentionDays);
    }
}

public sealed class TranscriptNormalizerTests
{
    private static readonly IReadOnlyList<GlossaryTermDto> Glossary =
    [
        new(1, 1, "API", ["ây pi ai", "a pi ai"]),
        new(2, 1, "REST API", ["rét ây pi ai"]),
        new(3, 1, "SQL", ["ét kiu eo"])
    ];

    [Theory]
    [InlineData("rét ây pi ai dùng HTTP", "REST API dùng HTTP")]
    [InlineData("gọi ây  pi ai qua mạng", "gọi API qua mạng")]
    [InlineData("Ét Kiu Eo và sql", "SQL và SQL")]
    [InlineData("tapi ai không đổi", "tapi ai không đổi")]
    public void CorrectsWholeWordsOnly(string input, string expected) =>
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input, Glossary));
}

public sealed class QuestionImportTests
{
    [Fact]
    public void CsvWithVietnameseHeadersQuotesAndAliasesIsRead()
    {
        var csv = "﻿câu hỏi,đáp án,mức bloom,độ khó,follow-up 1\n" +
                  "\"Giải thích mô hình 3 lớp, cho ví dụ?\",\"UI, BLL, DAL\",Hiểu,dễ,\"Vì sao?\"\n" +
                  "\"Phân tích \"\"coupling\"\"\",Giảm phụ thuộc,4,,\n" +
                  "Câu này sai bloom,x,Ghi chép,,\n";
        var result = QuestionImportParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "q.csv");

        Assert.Equal(2, result.Questions.Count);
        Assert.Equal("Giải thích mô hình 3 lớp, cho ví dụ?", result.Questions[0].Content);
        Assert.Equal("Understand", result.Questions[0].BloomLevel);
        Assert.Equal("Basic", result.Questions[0].Difficulty);
        Assert.Equal(["Vì sao?"], result.Questions[0].FollowUpQuestions);
        Assert.Equal("Phân tích \"coupling\"", result.Questions[1].Content);
        Assert.Equal("Analyze", result.Questions[1].BloomLevel);
        Assert.Single(result.Problems);
    }

    [Fact]
    public void SemicolonCsvExcelAndJsonAreRead()
    {
        var semicolon = QuestionImportParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes("content;bloom\nWhat is a DTO?;Remember\n")), "q.csv");
        Assert.Equal("What is a DTO?", Assert.Single(semicolon.Questions).Content);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Q");
        sheet.Cell(1, 1).Value = "Question";
        sheet.Cell(1, 2).Value = "Bloom level";
        sheet.Cell(1, 3).Value = "Expected answer";
        sheet.Cell(2, 1).Value = "Compare TCP and UDP";
        sheet.Cell(2, 2).Value = "Analyze";
        sheet.Cell(2, 3).Value = "Reliability vs speed";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var excel = QuestionImportParser.Parse(stream, "q.xlsx");
        Assert.Equal("Reliability vs speed", Assert.Single(excel.Questions).ExpectedAnswer);

        var json = QuestionImportParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(
            """[{"content":"Define normalisation","expectedAnswer":"Removing redundancy","bloomLevel":"Remember","followUpQuestions":["1NF?","2NF?"]}]""")), "q.json");
        Assert.Equal(["1NF?", "2NF?"], Assert.Single(json.Questions).FollowUpQuestions);
    }

    [Fact]
    public void AFileWithoutTheRequiredColumnsIsRejected() =>
        Assert.Throws<ArgumentException>(() => QuestionImportParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n")), "q.csv"));
}

public sealed class MaterialRetrievalTests
{
    private static MaterialDto Material(int id, string title, string content) =>
        new(id, 1, "Databases", "CSDL", title, content, null, MaterialSourceType.ImportedFile, true, DateTime.UtcNow, DateTime.UtcNow, content.Length);

    [Fact]
    public void OnlyThePassagesAboutTheRequestAreUsed()
    {
        var filler = string.Join("\n\n", Enumerable.Range(1, 30).Select(i => $"## Slide {i}\nĐoạn {i} nói về lịch sử máy tính và các thế hệ phần cứng."));
        var slides = filler + "\n\n## Slide 31\nChuẩn hoá cơ sở dữ liệu: dạng chuẩn 1NF, 2NF, 3NF giúp loại bỏ dư thừa dữ liệu.";
        var context = MaterialRetriever.Retrieve([Material(1, "Bài giảng CSDL", slides), Material(2, "Gardening", "soil and water")],
            "chuẩn hoá cơ sở dữ liệu dạng chuẩn", 1500);

        var source = Assert.Single(context.Sources);
        Assert.Equal("Bài giảng CSDL", source.Title);
        Assert.Contains("3NF", context.Text);
        Assert.Contains("## Slide 31", context.Text);
        Assert.True(context.Text.Length <= 1700);
        Assert.DoesNotContain("Đoạn 2 nói", context.Text);
    }

    [Fact]
    public async Task WordDocumentsAreTurnedIntoText()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text("Chương 1: Kiến trúc"))),
                new Paragraph(new Run(new Text("Mô hình ba lớp tách giao diện, nghiệp vụ và dữ liệu.")))));
        }
        stream.Position = 0;
        var text = await MaterialTextExtractor.ExtractAsync(stream, "chuong1.docx");
        Assert.Contains("## Chương 1: Kiến trúc", text);
        Assert.Contains("Mô hình ba lớp", text);
        await Assert.ThrowsAsync<ArgumentException>(() => MaterialTextExtractor.ExtractAsync(new MemoryStream([1, 2, 3]), "broken.pptx"));
    }
}
