using AIVES.DTO;
using AIVES.DTO.Localization;
using System.Text.Json;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// Shared validation for generated rubric matrices. The grid has to be rectangular: the same
/// columns on every row, a descriptor in every cell, and point values that rise from the
/// weakest column to the strongest. Anything else is rejected rather than shown to a lecturer.
/// </summary>
internal static class GeneratedRubricParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static string Invalid() => L10n.T("The model did not return a usable rubric matrix. Try again with a clearer topic.");

    public static GeneratedRubric Parse(string json, int criterionCount, int levelCount)
    {
        GeneratedRubric? rubric;
        try
        {
            rubric = JsonSerializer.Deserialize<GeneratedRubric>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Invalid(), ex);
        }

        if (rubric is null || string.IsNullOrWhiteSpace(rubric.Name) || rubric.Name.Length > 200
            || rubric.Description is null || rubric.Description.Length > 1000)
            throw new InvalidOperationException(Invalid());

        // Order the columns weakest first so the matrix always reads left to right.
        rubric.Levels ??= [];
        rubric.Levels = rubric.Levels
            .Where(level => level is not null && !string.IsNullOrWhiteSpace(level.Name) && level.Points >= 0)
            .OrderBy(level => level.Points)
            .ToList();

        rubric.Criteria ??= [];
        rubric.Criteria = rubric.Criteria
            .Where(criterion => criterion is not null && !string.IsNullOrWhiteSpace(criterion.Criterion))
            .ToList();

        if (rubric.Levels.Count != levelCount || rubric.Criteria.Count != criterionCount)
            throw new InvalidOperationException(Invalid());

        var columnNames = rubric.Levels.Select(level => level.Name.Trim()).ToList();
        if (columnNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columnNames.Count)
            throw new InvalidOperationException(Invalid());

        foreach (var criterion in rubric.Criteria)
        {
            criterion.Cells ??= [];
            var cells = criterion.Cells
                .Where(cell => cell is not null && !string.IsNullOrWhiteSpace(cell.LevelName)
                    && !string.IsNullOrWhiteSpace(cell.Descriptor) && cell.Points >= 0)
                .ToList();

            var cellColumns = cells.Select(cell => cell.LevelName.Trim()).ToList();
            if (cellColumns.Count != columnNames.Count
                || cellColumns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columnNames.Count
                || !columnNames.All(column => cellColumns.Contains(column, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException(Invalid());

            criterion.Cells = cells;
        }

        return rubric;
    }

    /// <summary>Parses the single column list some providers are asked for on its own.</summary>
    public static List<GeneratedRubricLevel> ParseLevels(string json, int levelCount)
    {
        List<GeneratedRubricLevel>? levels;
        try
        {
            levels = JsonSerializer.Deserialize<List<GeneratedRubricLevel>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Invalid(), ex);
        }

        if (levels is null || levels.Count != levelCount || levels.Any(level => level is null
            || string.IsNullOrWhiteSpace(level.Name) || level.Points < 0))
            throw new InvalidOperationException(Invalid());

        var names = levels.Select(level => level.Name.Trim()).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            throw new InvalidOperationException(Invalid());

        return levels;
    }

    /// <summary>Parses one row produced on its own, with the given columns.</summary>
    public static GeneratedRubricCriterion ParseCriterion(string json, IReadOnlyList<GeneratedRubricLevel> levels)
    {
        GeneratedRubricCriterion? criterion;
        try
        {
            criterion = JsonSerializer.Deserialize<GeneratedRubricCriterion>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(Invalid(), ex);
        }

        if (criterion is null || string.IsNullOrWhiteSpace(criterion.Criterion))
            throw new InvalidOperationException(Invalid());

        criterion.Cells ??= [];
        var cells = criterion.Cells
            .Where(cell => cell is not null && !string.IsNullOrWhiteSpace(cell.LevelName)
                && !string.IsNullOrWhiteSpace(cell.Descriptor) && cell.Points >= 0)
            .ToList();

        var columns = levels.Select(level => level.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cells.Count != levels.Count
            || !cells.All(cell => columns.Contains(cell.LevelName.Trim())))
            throw new InvalidOperationException(Invalid());

        // Bind the descriptors to the canonical column names so later lookups are exact.
        foreach (var cell in cells)
        {
            var match = levels.First(level => string.Equals(level.Name, cell.LevelName.Trim(), StringComparison.OrdinalIgnoreCase));
            cell.LevelName = match.Name;
            if (cell.Points == 0)
                cell.Points = match.Points;
        }

        criterion.Cells = cells;
        return criterion;
    }
}

/// <summary>Shared instruction text so every provider asks for the same matrix shape.</summary>
internal static class RubricGenerationPrompt
{
    public static void Validate(RubricGenerationRequest request)
    {
        if (request.CriterionCount is < 1 or > 10)
            throw new ArgumentException(L10n.T("The number of rows must be between 1 and 10."), nameof(request));
        if (request.LevelCount is < 2 or > 6)
            throw new ArgumentException(L10n.T("A rubric matrix needs between 2 and 6 level columns."), nameof(request));
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 120)
            throw new ArgumentException(L10n.T("The subject must be between 1 and 120 characters."), nameof(request));
        if (string.IsNullOrWhiteSpace(request.Topic) || request.Topic.Length > 200)
            throw new ArgumentException(L10n.T("The topic must be between 1 and 200 characters."), nameof(request));
        if (request.LearningOutcomes?.Length > 2000)
            throw new ArgumentException(L10n.T("Learning outcomes cannot exceed 2000 characters."), nameof(request));
    }

    private static string Context(RubricGenerationRequest request, bool vietnamese)
    {
        if (string.IsNullOrWhiteSpace(request.RetrievedContext))
            return string.Empty;

        return vietnamese
            ? $"""

                Tài liệu tham khảo (RAG), chỉ dùng nội dung này làm căn cứ:
                ---
                {request.RetrievedContext}
                ---
                """
            : $"""

                Reference material (RAG). Base the matrix on this content:
                ---
                {request.RetrievedContext}
                ---
                """;
    }

    /// <summary>Asks for the whole matrix in one response, used by the stronger providers.</summary>
    public static string Build(RubricGenerationRequest request, bool vietnamese)
    {
        var subject = request.Subject;
        var topic = request.Topic;
        var outcomes = request.LearningOutcomes;
        var context = Context(request, vietnamese);

        return vietnamese
            ? $"""
                Bạn là chuyên gia đánh giá học thuật cho hệ thống AIVES.
                Hãy thiết kế một ma trận rubric với đúng {request.CriterionCount} hàng (tiêu chí) và đúng {request.LevelCount} cột (mức độ) cho chủ đề "{topic}" thuộc môn "{subject}".
                Ghi chú của giảng viên: {outcomes ?? "Không cung cấp"}.
                {context}
                Yêu cầu:
                - Name là tên rubric, tối đa 200 ký tự.
                - Điểm của các mức phải tăng dần từ mức thấp nhất đến mức cao nhất.
                - Mỗi hàng có đúng một ô cho mỗi cột, tên cột phải khớp chính xác.
                - Mỗi ô mô tả rõ biểu hiện ở mức đó, dài 1-2 câu, không chung chung.
                - Các mức phải phân biệt rõ với nhau, không trùng lặp.
                """
            : $"""
                You are an assessment specialist for the AIVES viva examination system.
                Design a rubric matrix with exactly {request.CriterionCount} rows (criteria) and exactly {request.LevelCount} columns (levels) for the topic "{topic}" in the subject "{subject}".
                Lecturer notes: {outcomes ?? "Not provided"}.
                {context}
                Requirements:
                - Name the rubric in at most 200 characters.
                - Level points must increase from the weakest level to the strongest.
                - Every row has exactly one cell per column, and each cell's levelName must match a column name exactly.
                - Each descriptor explains what that level looks like for that criterion, in one or two sentences, never generic.
                - Levels must be clearly distinguishable from one another.
                """;
    }

    /// <summary>
    /// Asks for the column list alone. Small models answer a short list far more reliably than a
    /// whole grid, so the row by row path uses this first.
    /// </summary>
    public static string BuildLevels(RubricGenerationRequest request, bool vietnamese)
    {
        var subject = request.Subject;
        var topic = request.Topic;

        return vietnamese
            ? $"""
                Cho môn "{subject}", chủ đề "{topic}".
                Hãy đề xuất đúng {request.LevelCount} mức độ đánh giá, điểm tăng dần từ thấp đến cao.
                Mỗi mức gồm tên (tối đa 60 ký tự) và số điểm.
                """
            : $"""
                For the subject "{subject}" and the topic "{topic}".
                Propose exactly {request.LevelCount} assessment levels whose points increase from the lowest to the highest.
                Each level has a name of at most 60 characters and a point value.
                """;
    }

    /// <summary>Asks for a single row against an already agreed column list.</summary>
    public static string BuildCriterion(RubricGenerationRequest request, IReadOnlyList<GeneratedRubricLevel> levels, bool vietnamese)
    {
        var subject = request.Subject;
        var topic = request.Topic;
        var columns = string.Join(", ", levels.Select(level => $"\"{level.Name}\""));

        return vietnamese
            ? $"""
                Cho môn "{subject}", chủ đề "{topic}".
                Các mức độ đã thống nhất: {columns}.
                Hãy viết đúng 1 tiêu chí đánh giá cùng một ô cho mỗi mức trên.
                Mỗi ô mô tả biểu hiện ở mức đó trong 1-2 câu. Tên mức trong ô phải khớp chính xác danh sách.
                """
            : $"""
                For the subject "{subject}" and the topic "{topic}".
                The agreed levels are: {columns}.
                Write exactly one assessment criterion together with one cell for each of those levels.
                Each cell describes what that level looks like for this criterion, in one or two sentences. The levelName in every cell must match the list exactly.
                """;
    }
}