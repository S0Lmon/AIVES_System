using AIVES.DTO;
using ClosedXML.Excel;
using System.Text;

namespace AIVES.BLL.Services.Import;

/// <summary>
/// The downloadable sample for the Import tab. Its headers and values are ones
/// <see cref="QuestionImportParser"/> accepts, so a lecturer can fill it in and upload it unchanged.
/// </summary>
public static class QuestionImportTemplate
{
    public const string CsvContentType = "text/csv";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] EnglishHeaders =
        ["Question", "Expected answer", "Bloom level", "Difficulty", "Follow-up 1", "Follow-up 2"];

    private static readonly string[] VietnameseHeaders =
        ["Câu hỏi", "Đáp án mong đợi", "Mức Bloom", "Độ khó", "Câu hỏi đào sâu 1", "Câu hỏi đào sâu 2"];

    private static readonly string[][] EnglishRows =
    [
        ["What is the role of the business logic layer in a three-layer application?", "Holds the rules and validation; sits between presentation and data access", "Understand", "Basic", "Why should the UI not call the database directly?", ""],
        ["Compare an abstract class with an interface in Java.", "State, constructors, single vs multiple inheritance, default methods", "Analyze", "Intermediate", "When would you pick an abstract class?", "What changed with default methods in Java 8?"]
    ];

    private static readonly string[][] VietnameseRows =
    [
        ["Lớp nghiệp vụ (BLL) trong mô hình 3 lớp có vai trò gì?", "Chứa quy tắc nghiệp vụ và kiểm tra dữ liệu; nằm giữa lớp giao diện và lớp truy cập dữ liệu", "Hiểu", "Dễ", "Vì sao giao diện không nên gọi thẳng cơ sở dữ liệu?", ""],
        ["So sánh abstract class và interface trong Java.", "Trạng thái, constructor, đơn kế thừa và đa kế thừa, default method", "Phân tích", "Trung bình", "Khi nào nên chọn abstract class?", "Java 8 thay đổi gì với default method?"]
    ];

    public static string FileName(string extension) => "question-import-template" + extension;

    /// <summary>UTF-8 with a byte order mark, so Excel opens Vietnamese text correctly.</summary>
    public static byte[] Csv(AppLanguage language)
    {
        var (headers, rows) = Content(language);
        var text = new StringBuilder();
        foreach (var record in rows.Prepend(headers))
            text.Append(string.Join(',', record.Select(Quote))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public static byte[] Xlsx(AppLanguage language)
    {
        var (headers, rows) = Content(language);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(language == AppLanguage.Vi ? "Câu hỏi" : "Questions");
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < headers.Length; column++)
                sheet.Cell(row + 2, column + 1).Value = rows[row][column];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, rows.Length + 1, 10, 70);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static (string[] Headers, string[][] Rows) Content(AppLanguage language) =>
        language == AppLanguage.Vi ? (VietnameseHeaders, VietnameseRows) : (EnglishHeaders, EnglishRows);

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r', ';']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
