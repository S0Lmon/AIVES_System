using AIVES.BLL.Services.Accounts;
using AIVES.BLL.Services.Catalog;
using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Operations;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace AIVES.WebRazor.Pages.Exams;

public sealed class DetailsModel(IExamService exams, DisplayTimeZone timeZone, TimeProvider clock, ILiveUpdates liveUpdates) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id
    {
        get; set;
    }
    public ExamDetailsDto Exam { get; private set; } = null!;
    public IReadOnlyList<ExamPoolQuestion> QuestionPool { get; private set; } = [];
    public DateTime NowUtc
    {
        get; private set;
    }
    public bool Started => Exam.HasStarted(NowUtc);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostReassignAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await exams.ReassignQuestionsAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.T("New questions were drawn for every candidate.");
            if (result.ConsecutiveOverlaps > 0)
                TempData["ExamWarning"] = L10n.Format("The question pool is small: consecutive candidates share {0} question(s). Add questions to the subject or lower the number of main questions, then draw again.", result.ConsecutiveOverlaps);
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Updated, Id, exam?.Title ?? string.Empty, ExamLive.Emails(exam));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostResetScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await exams.ResetScheduleAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.T("The schedule was regenerated from the exam settings.");
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Updated, Id, exam?.Title ?? string.Empty, ExamLive.Emails(exam));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostChangeQuestionAsync(
        int order, int questionOrder, int questionId, CancellationToken cancellationToken)
    {
        try
        {
            await exams.UpdateCandidateQuestionAsync(Id, order, questionOrder, questionId, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.Format("Candidate #{0}, question {1} was updated.", order, questionOrder);
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Updated, Id, exam?.Title ?? string.Empty, ExamLive.Emails(exam));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            var title = exam?.Title ?? string.Empty;
            await exams.DeleteAsync(Id, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.T("The exam was deleted.");
            await liveUpdates.ExamChangedAsync(LiveActions.Deleted, Id, title, ExamLive.Emails(exam));
            return RedirectToPage("./Index");
        }
        catch (KeyNotFoundException) { return RedirectToPage("./Index"); }
        catch (InvalidOperationException ex) { TempData["ExamError"] = ex.Message; return RedirectToPage(new { id = Id }); }
    }

    public async Task<IActionResult> OnPostAdjustSlotAsync(int order, DateTime slotStartLocal, CancellationToken cancellationToken)
    {
        try
        {
            await exams.AdjustSlotAsync(Id, order, timeZone.ToUtc(slotStartLocal), ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = L10n.Format("Candidate #{0} was moved to a new time.", order);
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Updated, Id, exam?.Title ?? string.Empty, ExamLive.Emails(exam));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    public async Task<IActionResult> OnPostCandidateStatusAsync(int order, string? status, CancellationToken cancellationToken)
    {
        CandidateStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CandidateStatus>(status, out var value))
            {
                TempData["ExamError"] = L10n.T("That status does not exist.");
                return RedirectToPage(new
                {
                    id = Id
                });
            }
            parsed = value;
        }
        try
        {
            await exams.SetCandidateStatusAsync(Id, order, parsed, ExamPageActors.From(User), cancellationToken);
            TempData["ExamMessage"] = parsed is null ? L10n.Format("Candidate #{0} follows the schedule again.", order) : L10n.Format("Candidate #{0} was marked {1}.", order, ViewText.Status(parsed.Value));
            var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
            await liveUpdates.ExamChangedAsync(LiveActions.Updated, Id, exam?.Title ?? string.Empty, ExamLive.Emails(exam));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { TempData["ExamError"] = ex.Message; }
        return RedirectToPage(new
        {
            id = Id
        });
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        NowUtc = clock.GetUtcNow().UtcDateTime;
        var exam = await exams.GetAsync(Id, ExamPageActors.From(User), cancellationToken);
        if (exam is null)
            return false;
        Exam = exam;
        if (exam.SubjectId is int subjectId)
        {
            var selectedIds = (exam.SelectedQuestionIds ?? []).ToHashSet();
            QuestionPool = (await exams.GetQuestionBankAsync(subjectId, exam.TopicId, exam.RubricId,
                    ExamPageActors.From(User), cancellationToken))
                .Where(question => selectedIds.Contains(question.Id))
                .ToList();
        }
        return true;
    }
}
