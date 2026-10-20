using AIVES.BLL.Services.Exams;
using AIVES.BLL.Services.Grading;
using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.WebRazor.Pages.Exams;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AIVES.WebRazor.Pages.Grading;

public abstract class GradeExportPage(IGradingService grading, IAuditService audit, IOptions<ReportOptions> options, DisplayTimeZone timeZone, ILogger logger) : PageModel
{
    protected async Task<IActionResult> ExportAsync(int id, CancellationToken cancellationToken)
    {
        var exam = await grading.GetExamAsync(id, ExamPageActors.From(User), cancellationToken);
        if (exam is null)
            return NotFound();
        var content = GradeSheetBuilder.Build(exam, options.Value, timeZone.ToLocal);
        var actor = ExamPageActors.From(User);
        await audit.WriteAsync(new AuditEntryInput(AuditActions.GradeSheetExported, actor.UserId, actor.Email, id, null,
            L10n.Format("{0} of {1} grades confirmed", exam.Rows.Count(row => row.FinalizedAtUtc is not null), exam.Rows.Count)), cancellationToken);
        logger.LogInformation("User {UserId} exported the grade sheet of exam {ExamId}", actor.UserId, id);
        var slug = string.Join('-', new string(exam.SubjectName.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c == 'đ' || c == 'Đ' ? 'd' : char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : '-').ToArray())
            .Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
            slug = "exam";
        slug = slug[..Math.Min(40, slug.Length)];
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"grades-{slug}-{timeZone.ToLocal(exam.StartsAtUtc):yyyyMMdd}-{id}.xlsx");
    }
}
public sealed class ExportModel(IGradingService grading, IAuditService audit, IOptions<ReportOptions> options, DisplayTimeZone timeZone, ILogger<ExportModel> logger)
    : GradeExportPage(grading, audit, options, timeZone, logger)
{
    public Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) => ExportAsync(id, cancellationToken);
}
