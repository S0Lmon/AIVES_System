using AIVES.BLL.Services.Grading;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using AIVES.DTO;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AIVES.Tests;

/// <summary>FR-12: the class report (/Reports/Exam/{id}) and the Excel grade sheet (/Grading/Export).</summary>
[Trait("Requirement", "FR-12")]
public sealed class ReportTests
{
    private const string Owner = "lecturer-1";
    private static readonly DateTime Now = new(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);
    private static readonly ExamActor Lecturer = new(Owner, false, "gv@fpt.edu.vn");

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class UnusedGrader : IAnswerGrader
    {
        public bool IsConfigured => false;
        public string ModelName => "none";
        public Task<GradeSuggestion> GradeAsync(GradingRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The report tests grade by hand.");
    }

    /// <summary>
    /// One candidate with one question worth 10 points. <paramref name="Status"/> null = did not sit.
    /// <paramref name="Score"/> is the lecturer's score; it is confirmed only when <paramref name="Confirm"/> is true.
    /// </summary>
    private sealed record Sitting(string Email, InterviewStatus? Status, decimal? Score = null, bool Confirm = true,
        int Bank = 1, string Bloom = "Understand", int FollowUps = 0, int[]? LatenciesMs = null);

    private sealed record Harness(GradingService Service, ApplicationDbContext Db, int ExamId, IReadOnlyList<int> CandidateIds);

    private static async Task<Harness> Build(params Sitting[] sittings)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var exam = new Exam
        {
            Title = "Giữa kỳ vấn đáp",
            SubjectName = "Kỹ thuật phần mềm",
            TopicName = "Kiến trúc",
            // 20:00 UTC is already the next day in Vietnam (UTC+7).
            StartsAtUtc = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc),
            SlotMinutes = 15,
            MainQuestionCount = 1,
            MaxFollowUpQuestions = 20,
            Language = "vi-VN",
            CreatedById = Owner
        };
        var order = 0;
        foreach (var sitting in sittings)
        {
            var candidate = new ExamCandidate
            {
                Order = ++order,
                Email = sitting.Email,
                Questions =
                {
                    new ExamCandidateQuestion
                    {
                        Order = 1,
                        Content = $"Question {sitting.Bank}",
                        ExpectedAnswer = "Expected",
                        BloomLevelName = sitting.Bloom,
                        QuestionId = sitting.Bank
                    }
                }
            };
            if (sitting.Status is { } status)
            {
                var latencies = sitting.LatenciesMs ?? [];
                var attempt = new ExamAttempt
                {
                    StartedAtUtc = Now.AddMinutes(-30),
                    CompletedAtUtc = status == InterviewStatus.Completed ? Now.AddMinutes(-10) : null,
                    Status = status
                };
                attempt.Turns.Add(new ExamTurn
                {
                    Order = 1, Kind = TurnKind.Main, MainIndex = 1, QuestionText = $"Question {sitting.Bank}",
                    AskedAtUtc = Now.AddMinutes(-30), AnsweredAtUtc = Now.AddMinutes(-29), Answer = "an answer", InputMode = AnswerInputMode.Speech,
                    Decision = sitting.FollowUps == 0 ? FollowUpReason.Sufficient : FollowUpReason.Vague,
                    DecisionLatencyMs = latencies.Length > 0 ? latencies[0] : null
                });
                for (var i = 1; i <= sitting.FollowUps; i++)
                    attempt.Turns.Add(new ExamTurn
                    {
                        Order = 1 + i, Kind = TurnKind.FollowUp, MainIndex = 1, FollowUpIndex = i, QuestionText = $"Follow-up {i}",
                        AskedAtUtc = Now.AddMinutes(-29), AnsweredAtUtc = Now.AddMinutes(-28), Answer = "more", InputMode = AnswerInputMode.Speech,
                        Decision = i == sitting.FollowUps ? FollowUpReason.Sufficient : FollowUpReason.Vague,
                        DecisionLatencyMs = latencies.Length > i ? latencies[i] : null
                    });
                candidate.Attempt = attempt;
            }
            exam.Candidates.Add(candidate);
        }
        db.Exams.Add(exam);
        db.SaveChanges();

        var clock = new FixedClock(Now);
        var service = new GradingService(new GradingRepository(db), new ExamRepository(db), new UnusedGrader(), TestServices.Glossary(db),
            TestServices.Audit(db, clock), Options.Create(new GradingOptions()), clock, NullLogger<GradingService>.Instance);
        var ids = exam.Candidates.OrderBy(candidate => candidate.Order).Select(candidate => candidate.Id).ToList();
        for (var i = 0; i < sittings.Length; i++)
        {
            if (sittings[i].Score is not { } score)
                continue;
            var question = (await service.GetCandidateAsync(ids[i], Lecturer))!.Questions.Single();
            await service.SaveAsync(ids[i], new CandidateGradeInput([new(question.Id, score, null)], null, sittings[i].Confirm), Lecturer);
        }
        return new Harness(service, db, exam.Id, ids);
    }

    // TC-12-01: sat / confirmed counts, mean, median, min, max, pass rate (≥ 5) and the distribution use confirmed grades only.
    [Fact]
    public async Task ClassStatisticsCountOnlyConfirmedGrades()
    {
        var h = await Build(
            new("a@fpt.edu.vn", InterviewStatus.Completed, 2),
            new("b@fpt.edu.vn", InterviewStatus.Completed, 4.5m),
            new("c@fpt.edu.vn", InterviewStatus.Completed, 6),
            new("d@fpt.edu.vn", InterviewStatus.Completed, 8),
            new("e@fpt.edu.vn", InterviewStatus.Completed, 10),
            new("draft@fpt.edu.vn", InterviewStatus.Completed, 9, Confirm: false),
            new("absent@fpt.edu.vn", null),
            new("late@fpt.edu.vn", InterviewStatus.InProgress));

        var report = (await h.Service.GetReportAsync(h.ExamId, Lecturer))!;

        Assert.Equal(8, report.CandidateCount);
        Assert.Equal(6, report.CompletedCount);
        Assert.Equal(5, report.FinalizedCount);
        Assert.Equal(6.1m, report.Average);
        Assert.Equal(6m, report.Median);
        Assert.Equal(2m, report.Minimum);
        Assert.Equal(10m, report.Maximum);
        Assert.Equal(60m, report.PassRate);
        Assert.Equal(10, report.Distribution.Count);
        Assert.Equal(5, report.Distribution.Sum(bucket => bucket.Count));
        Assert.Equal([0, 0, 1, 0, 1, 0, 1, 0, 1, 1], report.Distribution.Select(bucket => bucket.Count));
        Assert.Equal("Kỹ thuật phần mềm", report.SubjectName);
        Assert.Equal("Kiến trúc", report.TopicName);
    }

    [Fact]
    public async Task TheMedianOfAnEvenNumberOfGradesIsTheMeanOfTheMiddleTwo()
    {
        var h = await Build(
            new("a@fpt.edu.vn", InterviewStatus.Completed, 3),
            new("b@fpt.edu.vn", InterviewStatus.Completed, 5),
            new("c@fpt.edu.vn", InterviewStatus.Completed, 7.5m),
            new("d@fpt.edu.vn", InterviewStatus.Completed, 9));

        var report = (await h.Service.GetReportAsync(h.ExamId, Lecturer))!;
        Assert.Equal(6.25m, report.Median);
        Assert.Equal(75m, report.PassRate);
        // A grade of exactly 5 passes.
        Assert.Equal(1, report.Distribution.Single(bucket => bucket.From == 5).Count);
    }

    // TC-12-02: hardest question first, per-Bloom averages and follow-ups, matching the graded data.
    [Fact]
    public async Task HardestQuestionsComeFirstWithBloomAveragesAndFollowUps()
    {
        var h = await Build(
            new("a@fpt.edu.vn", InterviewStatus.Completed, 9, Bank: 1, Bloom: "Remember"),
            new("b@fpt.edu.vn", InterviewStatus.Completed, 10, Bank: 1, Bloom: "Remember"),
            new("c@fpt.edu.vn", InterviewStatus.Completed, 3, Bank: 2, Bloom: "Analyze", FollowUps: 1),
            new("d@fpt.edu.vn", InterviewStatus.Completed, 5, Bank: 2, Bloom: "Analyze", FollowUps: 1),
            new("e@fpt.edu.vn", InterviewStatus.Completed, 7, Bank: 3, Bloom: "Apply"));

        var report = (await h.Service.GetReportAsync(h.ExamId, Lecturer))!;

        Assert.Equal(["Question 2", "Question 3", "Question 1"], report.Questions.Select(question => question.Content));
        var hardest = report.Questions[0];
        Assert.Equal(2, hardest.TimesAsked);
        Assert.Equal(40m, hardest.AveragePercent);
        Assert.Equal(0m, hardest.GoodAnswerRate);
        Assert.Equal(1, hardest.FollowUpsPerAsk);
        Assert.Equal(0, hardest.SufficientFirstTime);
        // 7/10 is exactly the "answered well" threshold (70 %).
        Assert.Equal(100m, report.Questions[1].GoodAnswerRate);
        Assert.Equal(95m, report.Questions[2].AveragePercent);
        Assert.Equal(2, report.Questions[2].SufficientFirstTime);

        Assert.Equal(["Analyze", "Apply", "Remember"], report.BloomLevels.Select(level => level.BloomLevelName));
        Assert.Equal([40m, 70m, 95m], report.BloomLevels.Select(level => level.AveragePercent));
        Assert.Equal([2, 1, 2], report.BloomLevels.Select(level => level.TimesAsked));
        Assert.Equal(0.4, report.FollowUpsPerAnswer); // 2 follow-ups over 5 answered main questions
    }

    // TC-12-02: AI latency is reported as a mean and a 95th percentile over every examiner decision.
    [Fact]
    public async Task AiLatencyIsTheMeanAndThe95thPercentileOfEveryDecision()
    {
        var latencies = Enumerable.Range(1, 20).Select(i => i * 100).Reverse().ToArray(); // 2000 … 100 ms, out of order on purpose
        var h = await Build(new Sitting("a@fpt.edu.vn", InterviewStatus.Completed, FollowUps: 19, LatenciesMs: latencies));

        var report = (await h.Service.GetReportAsync(h.ExamId, Lecturer))!;
        Assert.Equal(1050, report.AverageAiLatencyMs);
        Assert.Equal(1900, report.P95AiLatencyMs); // the 19th of 20 sorted values
        Assert.Equal(19, report.FollowUpsPerAnswer);
    }

    // TC-12-03: an exam nobody has been graded in yet (or with no candidates at all) reports empty values, not errors.
    [Fact]
    public async Task AnExamWithNothingConfirmedYetReportsEmptyValuesWithoutDividingByZero()
    {
        var h = await Build(
            new("absent@fpt.edu.vn", null),
            new("late@fpt.edu.vn", InterviewStatus.InProgress),
            new("ungraded@fpt.edu.vn", InterviewStatus.Completed));

        var report = (await h.Service.GetReportAsync(h.ExamId, Lecturer))!;
        Assert.Equal(3, report.CandidateCount);
        Assert.Equal(0, report.FinalizedCount);
        Assert.Null(report.Average);
        Assert.Null(report.Median);
        Assert.Null(report.Minimum);
        Assert.Null(report.Maximum);
        Assert.Null(report.PassRate);
        Assert.All(report.Distribution, bucket => Assert.Equal(0, bucket.Count));
        Assert.Null(report.AverageAiLatencyMs);
        Assert.Null(report.P95AiLatencyMs);
        Assert.All(report.Questions, question => Assert.Null(question.AveragePercent));

        var empty = await Build();
        var nothing = (await empty.Service.GetReportAsync(empty.ExamId, Lecturer))!;
        Assert.Equal(0, nothing.CandidateCount);
        Assert.Empty(nothing.Questions);
        Assert.Empty(nothing.BloomLevels);
        Assert.Null(nothing.FollowUpsPerAnswer);
        Assert.NotNull(GradeSheetBuilder.Build((await empty.Service.GetExamAsync(empty.ExamId, Lecturer))!, new ReportOptions(), time => time));
    }

    // TC-12-04: school, subject, exam, date, order, student code, name, email, each question, total /10, notes and signatures.
    [Fact]
    public async Task TheGradeSheetCarriesTheHeaderEveryCandidateTheirScoresAndSignatureLines()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en-US");
        try
        {
            var h = await Build(
                new("anhndse123456@fpt.edu.vn", InterviewStatus.Completed, 7.75m),
                new("draft@fpt.edu.vn", InterviewStatus.Completed, 9, Confirm: false),
                new("late@fpt.edu.vn", InterviewStatus.InProgress),
                new("absent@fpt.edu.vn", null));
            var exam = (await h.Service.GetExamAsync(h.ExamId, Lecturer))!;
            var bytes = GradeSheetBuilder.Build(exam, new ReportOptions { SchoolName = "TRƯỜNG ĐẠI HỌC FPT", UnitName = "Khoa CNTT" },
                utc => utc.AddHours(7));

            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            var sheet = workbook.Worksheets.First();
            Assert.Equal("TRƯỜNG ĐẠI HỌC FPT", sheet.Cell(1, 1).GetString());
            Assert.Equal("Khoa CNTT", sheet.Cell(2, 1).GetString());
            Assert.Equal("VIVA EXAM GRADE SHEET", sheet.Cell(3, 1).GetString());
            Assert.Equal("Kỹ thuật phần mềm / Kiến trúc", sheet.Cell(5, 3).GetString());
            Assert.Equal("Giữa kỳ vấn đáp", sheet.Cell(6, 3).GetString());
            Assert.Equal("11/11/2026", sheet.Cell(7, 3).GetString()); // the local date, not the UTC one

            Assert.Equal(["No.", "Student code", "Full name", "Email", "Q1 (/10)", "Total (10-point scale)", "Notes"],
                Enumerable.Range(1, 7).Select(column => sheet.Cell(9, column).GetString()));

            Assert.Equal(1, sheet.Cell(10, 1).GetDouble());
            Assert.Equal("SE123456", sheet.Cell(10, 2).GetString());
            Assert.Equal("anhndse123456@fpt.edu.vn", sheet.Cell(10, 4).GetString());
            Assert.Equal(7.75, sheet.Cell(10, 5).GetDouble());
            Assert.Equal(7.75, sheet.Cell(10, 6).GetDouble());
            Assert.Equal(string.Empty, sheet.Cell(10, 7).GetString());

            // Unconfirmed grades are never printed; the notes say why.
            Assert.True(sheet.Cell(11, 5).IsEmpty());
            Assert.True(sheet.Cell(11, 6).IsEmpty());
            Assert.Equal(["Grade not confirmed", "Viva not finished", "Did not sit"], new[] { 11, 12, 13 }.Select(row => sheet.Cell(row, 7).GetString()));
            Assert.Equal(string.Empty, sheet.Cell(11, 2).GetString()); // not an FPT student email, so no code

            var texts = sheet.CellsUsed().Select(cell => cell.GetString()).ToList();
            Assert.Contains("Head of department", texts);
            Assert.Contains("Examiner", texts);
            Assert.Equal(2, texts.Count(text => text == "(Signature, full name)"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // TC-12-05 (service level): another lecturer gets neither the report nor the grade sheet; admins get both.
    [Fact]
    public async Task OnlyTheExamOwnerAndAdminsGetTheReportAndTheGradeSheet()
    {
        var h = await Build(new Sitting("a@fpt.edu.vn", InterviewStatus.Completed, 8));
        var stranger = new ExamActor("lecturer-2", false);
        var admin = new ExamActor("admin", true);

        Assert.Null(await h.Service.GetReportAsync(h.ExamId, stranger));
        Assert.Null(await h.Service.GetExamAsync(h.ExamId, stranger));
        Assert.NotNull(await h.Service.GetReportAsync(h.ExamId, admin));
        Assert.NotNull(await h.Service.GetExamAsync(h.ExamId, admin));
        Assert.Null(await h.Service.GetReportAsync(h.ExamId + 1000, Lecturer));
    }
}
