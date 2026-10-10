using AIVES.DTO;
using AIVES.DTO.Localization;
using ClosedXML.Excel;

namespace AIVES.BLL.Services.Grading;

public sealed class ReportOptions
{
    public const string SectionName = "Reports";

    /// <summary>Institution name printed at the top of the grade sheet.</summary>
    public string SchoolName { get; set; } = "TRƯỜNG ĐẠI HỌC FPT";

    /// <summary>Unit line under the school name, e.g. the faculty or campus.</summary>
    public string? UnitName
    {
        get; set;
    }
}

/// <summary>
/// The official grade sheet: school header, exam details, one row per candidate (order, student
/// code, name, email, each question's confirmed score, the total on a 10-point scale, notes) and
/// signature lines. Only grades the lecturer confirmed are printed; others are marked in the notes.
/// </summary>
public static class GradeSheetBuilder
{
    public static byte[] Build(ExamGradingDto exam, ReportOptions options, Func<DateTime, DateTime> toLocal)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(L10n.T("Grade sheet"));
        var questionCount = Math.Max(exam.MainQuestionCount, exam.Rows.Select(row => row.QuestionMaxScores.Count).DefaultIfEmpty(0).Max());
        var lastColumn = 4 + questionCount + 2;

        sheet.Cell(1, 1).Value = options.SchoolName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        if (!string.IsNullOrWhiteSpace(options.UnitName))
            sheet.Cell(2, 1).Value = options.UnitName;
        var title = sheet.Range(3, 1, 3, lastColumn).Merge();
        title.Value = L10n.T("VIVA EXAM GRADE SHEET");
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 14;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        sheet.Cell(5, 1).Value = L10n.T("Subject") + ":";
        sheet.Cell(5, 3).Value = exam.SubjectName + (exam.TopicName is null ? string.Empty : " / " + exam.TopicName);
        sheet.Cell(6, 1).Value = L10n.T("Exam") + ":";
        sheet.Cell(6, 3).Value = exam.Title;
        sheet.Cell(7, 1).Value = L10n.T("Exam date") + ":";
        sheet.Cell(7, 3).Value = toLocal(exam.StartsAtUtc).ToString("dd/MM/yyyy");

        const int headerRow = 9;
        var headers = new List<string> { L10n.T("No."), L10n.T("Student code"), L10n.T("Full name"), L10n.T("Email") };
        for (var i = 0; i < questionCount; i++)
        {
            // Each candidate draws different questions; show the maximum only when they all share it.
            var maxima = exam.Rows.Where(row => i < row.QuestionMaxScores.Count).Select(row => row.QuestionMaxScores[i]).Distinct().ToList();
            headers.Add(L10n.Format("Q{0}", i + 1) + (maxima.Count == 1 ? $" (/{maxima[0]:0.##})" : string.Empty));
        }
        headers.Add(L10n.T("Total (10-point scale)"));
        headers.Add(L10n.T("Notes"));
        for (var i = 0; i < headers.Count; i++)
            sheet.Cell(headerRow, i + 1).Value = headers[i];
        var header = sheet.Range(headerRow, 1, headerRow, lastColumn);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7ECF3");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.WrapText = true;

        var rowIndex = headerRow;
        foreach (var row in exam.Rows.OrderBy(row => row.Order))
        {
            rowIndex++;
            sheet.Cell(rowIndex, 1).Value = row.Order;
            sheet.Cell(rowIndex, 2).Value = row.StudentCode ?? string.Empty;
            sheet.Cell(rowIndex, 3).Value = row.DisplayName ?? string.Empty;
            sheet.Cell(rowIndex, 4).Value = row.Email;
            for (var i = 0; i < questionCount; i++)
            {
                if (i < row.QuestionScores.Count && row.QuestionScores[i] is { } score)
                    sheet.Cell(rowIndex, 5 + i).Value = score;
            }
            if (row.FinalScore is { } final)
            {
                sheet.Cell(rowIndex, 5 + questionCount).Value = final;
                sheet.Cell(rowIndex, 5 + questionCount).Style.Font.Bold = true;
            }
            sheet.Cell(rowIndex, 6 + questionCount).Value = Note(row);
        }

        var table = sheet.Range(headerRow, 1, Math.Max(rowIndex, headerRow), lastColumn);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(headerRow + 1, 5, Math.Max(rowIndex, headerRow + 1), 5 + questionCount).Style.NumberFormat.Format = "0.00";

        var signatureRow = rowIndex + 3;
        sheet.Cell(signatureRow, lastColumn - 1).Value = L10n.Format("Date: {0}", DateTime.Now.ToString("dd/MM/yyyy"));
        sheet.Cell(signatureRow + 1, 2).Value = L10n.T("Head of department");
        sheet.Cell(signatureRow + 1, lastColumn - 1).Value = L10n.T("Examiner");
        sheet.Cell(signatureRow + 2, 2).Value = L10n.T("(Signature, full name)");
        sheet.Cell(signatureRow + 2, lastColumn - 1).Value = L10n.T("(Signature, full name)");
        sheet.Range(signatureRow + 1, 1, signatureRow + 1, lastColumn).Style.Font.Bold = true;

        sheet.Column(1).Width = 6;
        sheet.Column(2).Width = 14;
        sheet.Column(3).Width = 28;
        sheet.Column(4).Width = 32;
        for (var i = 0; i < questionCount; i++)
            sheet.Column(5 + i).Width = 11;
        sheet.Column(5 + questionCount).Width = 14;
        sheet.Column(6 + questionCount).Width = 26;
        sheet.SheetView.FreezeRows(headerRow);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string Note(GradingRowDto row) => row switch
    {
        { FinalizedAtUtc: not null } => string.Empty,
        { InterviewStatus: InterviewStatus.NotStarted } => L10n.T("Did not sit"),
        { InterviewStatus: InterviewStatus.InProgress } => L10n.T("Viva not finished"),
        _ => L10n.T("Grade not confirmed")
    };
}
