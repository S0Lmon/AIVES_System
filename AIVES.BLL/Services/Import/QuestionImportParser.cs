using System.Globalization;
using System.Text;
using System.Text.Json;
using AIVES.DTO;
using AIVES.DTO.Localization;
using ClosedXML.Excel;

namespace AIVES.BLL.Services.Import;

/// <summary>Result of reading an import file: usable questions plus a message per skipped row.</summary>
public sealed record QuestionImportResult(IReadOnlyList<GeneratedVivaQuestion> Questions, IReadOnlyList<string> Problems);

/// <summary>
/// Reads questions from a CSV, Excel (.xlsx) or JSON file into the same shape the AI generators
/// return, so imported questions go through the same review screen before they enter the bank.
/// The first row of a CSV/Excel file is a header; column names are matched in English or Vietnamese
/// (content/question/câu hỏi, expected answer/đáp án, bloom/mức bloom, difficulty/độ khó,
/// follow-up 1/follow-up 2). Bloom accepts the English name, the Vietnamese name or 1–6.
/// </summary>
public static class QuestionImportParser
{
    public const int MaxQuestions = 300;
    public static readonly IReadOnlyList<string> Extensions = [".csv", ".xlsx", ".json"];

    private static readonly Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["content"] = "content", ["question"] = "content", ["câu hỏi"] = "content", ["cau hoi"] = "content", ["nội dung"] = "content",
        ["expectedanswer"] = "answer", ["expected answer"] = "answer", ["answer"] = "answer", ["đáp án"] = "answer", ["dap an"] = "answer", ["đáp án mong đợi"] = "answer",
        ["bloom"] = "bloom", ["bloomlevel"] = "bloom", ["bloom level"] = "bloom", ["mức bloom"] = "bloom", ["muc bloom"] = "bloom", ["mức độ"] = "bloom",
        ["difficulty"] = "difficulty", ["độ khó"] = "difficulty", ["do kho"] = "difficulty",
        ["followup1"] = "followup1", ["follow-up 1"] = "followup1", ["follow up 1"] = "followup1", ["câu hỏi đào sâu 1"] = "followup1",
        ["followup2"] = "followup2", ["follow-up 2"] = "followup2", ["follow up 2"] = "followup2", ["câu hỏi đào sâu 2"] = "followup2"
    };

    private static readonly Dictionary<string, string> BloomAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1"] = "Remember", ["nhớ"] = "Remember", ["ghi nhớ"] = "Remember", ["biết"] = "Remember",
        ["2"] = "Understand", ["hiểu"] = "Understand", ["thông hiểu"] = "Understand",
        ["3"] = "Apply", ["vận dụng"] = "Apply", ["áp dụng"] = "Apply",
        ["4"] = "Analyze", ["phân tích"] = "Analyze", ["analyse"] = "Analyze",
        ["5"] = "Evaluate", ["đánh giá"] = "Evaluate",
        ["6"] = "Create", ["sáng tạo"] = "Create"
    };

    private static readonly Dictionary<string, string> DifficultyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dễ"] = "Basic", ["cơ bản"] = "Basic", ["easy"] = "Basic",
        ["trung bình"] = "Intermediate", ["medium"] = "Intermediate",
        ["khó"] = "Advanced", ["nâng cao"] = "Advanced", ["hard"] = "Advanced"
    };

    public static QuestionImportResult Parse(Stream content, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = extension switch
        {
            ".csv" => ReadCsv(content),
            ".xlsx" => ReadExcel(content),
            ".json" => ReadJson(content),
            _ => throw new ArgumentException(L10n.T("Supported formats: .csv, .xlsx, .json"))
        };

        var questions = new List<GeneratedVivaQuestion>();
        var problems = new List<string>();
        foreach (var (line, fields) in rows)
        {
            if (fields.Values.All(string.IsNullOrWhiteSpace))
                continue;
            var question = fields.GetValueOrDefault("content")?.Trim() ?? string.Empty;
            if (question.Length < 5)
            {
                problems.Add(L10n.Format("Row {0}: the question text is missing or too short.", line));
                continue;
            }
            var bloom = NormalizeBloom(fields.GetValueOrDefault("bloom"));
            if (bloom is null)
            {
                problems.Add(L10n.Format("Row {0}: unknown Bloom level '{1}'.", line, fields.GetValueOrDefault("bloom") ?? string.Empty));
                continue;
            }
            if (questions.Count >= MaxQuestions)
            {
                problems.Add(L10n.Format("Only the first {0} questions were read.", MaxQuestions));
                break;
            }
            var difficulty = fields.GetValueOrDefault("difficulty")?.Trim();
            questions.Add(new GeneratedVivaQuestion
            {
                Content = question,
                ExpectedAnswer = fields.GetValueOrDefault("answer")?.Trim() ?? string.Empty,
                BloomLevel = bloom,
                Difficulty = QuestionDifficulties.Normalize(difficulty)
                    ?? (difficulty is not null && DifficultyAliases.TryGetValue(difficulty, out var alias) ? alias : string.Empty),
                FollowUpQuestions = new[] { fields.GetValueOrDefault("followup1"), fields.GetValueOrDefault("followup2") }
                    .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!.Trim()).ToList()
            });
        }
        if (questions.Count == 0 && problems.Count == 0)
            problems.Add(L10n.T("The file contains no questions."));
        return new QuestionImportResult(questions, problems);
    }

    public static string? NormalizeBloom(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        return BloomLevels.Normalize(text) ?? (BloomAliases.TryGetValue(text, out var alias) ? alias : null);
    }

    private static IEnumerable<(int Line, Dictionary<string, string?> Fields)> Map(IReadOnlyList<string> header, IEnumerable<(int Line, IReadOnlyList<string> Cells)> rows)
    {
        var keys = header.Select(name => Columns.GetValueOrDefault(name.Trim())).ToList();
        if (!keys.Contains("content"))
            throw new ArgumentException(L10n.T("The first row must name the columns; a 'content' (or 'câu hỏi') column is required."));
        if (!keys.Contains("bloom"))
            throw new ArgumentException(L10n.T("A 'bloom' (or 'mức bloom') column is required."));
        foreach (var (line, cells) in rows)
        {
            var fields = new Dictionary<string, string?>();
            for (var i = 0; i < keys.Count && i < cells.Count; i++)
                if (keys[i] is { } key)
                    fields[key] = cells[i];
            yield return (line, fields);
        }
    }

    private static IEnumerable<(int, Dictionary<string, string?>)> ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var records = CsvRecords(text).ToList();
        if (records.Count == 0)
            return [];
        // Excel in Vietnamese locales saves CSV with ';'.
        var delimiterRecords = records[0].Count == 1 && records[0][0].Contains(';') ? CsvRecords(text, ';').ToList() : records;
        return Map(delimiterRecords[0], delimiterRecords.Skip(1).Select((cells, index) => (index + 2, (IReadOnlyList<string>)cells))).ToList();
    }

    /// <summary>RFC 4180 records: quoted fields may contain the delimiter, quotes ("") and line breaks.</summary>
    private static IEnumerable<List<string>> CsvRecords(string text, char delimiter = ',')
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) { record.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                record.Add(field.ToString());
                field.Clear();
                yield return record;
                record = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }

    private static IEnumerable<(int, Dictionary<string, string?>)> ReadExcel(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
            return [];
        var rows = used.RowsUsed().ToList();
        var width = used.ColumnCount();
        IReadOnlyList<string> Cells(IXLRangeRow row) => Enumerable.Range(1, width).Select(column => row.Cell(column).GetFormattedString()).ToList();
        return Map(Cells(rows[0]), rows.Skip(1).Select(row => (row.RowNumber(), Cells(row)))).ToList();
    }

    private static IEnumerable<(int, Dictionary<string, string?>)> ReadJson(Stream content)
    {
        using var document = JsonDocument.Parse(content);
        var array = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement
            : document.RootElement.TryGetProperty("questions", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : throw new ArgumentException(L10n.T("The JSON file must be an array of questions."));
        var result = new List<(int, Dictionary<string, string?>)>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var fields = new Dictionary<string, string?>();
            foreach (var property in item.EnumerateObject())
            {
                if (property.NameEquals("followUpQuestions") && property.Value.ValueKind == JsonValueKind.Array)
                {
                    var followUps = property.Value.EnumerateArray().Select(value => value.ToString()).ToList();
                    fields["followup1"] = followUps.ElementAtOrDefault(0);
                    fields["followup2"] = followUps.ElementAtOrDefault(1);
                }
                else if (Columns.TryGetValue(property.Name, out var key))
                {
                    fields[key] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()
                        : property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetRawText() : null;
                }
            }
            result.Add((index, fields));
        }
        return result;
    }
}
