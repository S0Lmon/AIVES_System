using System.ComponentModel.DataAnnotations;
using AIVES.DTO;

namespace AIVES.WebMVC.Models.ViewModels;

public static class RubricTabs
{
    public const string Bank = "bank";
    public const string Create = "create";
    public const string Ai = "ai";

    public static string Normalize(string? tab) => tab switch
    {
        Create => Create,
        Ai => Ai,
        _ => Bank
    };
}

public sealed class RubricViewModel
{
    public string ActiveTab { get; set; } = RubricTabs.Bank;

    public string? SubjectFilter { get; set; }
    public string? UsageFilter { get; set; }

    public int RubricCount { get; set; }
    public int CriteriaCount { get; set; }
    public int LevelCount { get; set; }
    public int MaxPoints { get; set; }

    public IReadOnlyList<RubricRow> Rubrics { get; set; } = [];
    public IReadOnlyList<SelectListItemOption> BloomLevels { get; set; } = [];

    public RubricMatrixModel Matrix { get; set; } = new();
    public AiRubricGeneratorViewModel Ai { get; set; } = new();
}

public sealed record SelectListItemOption(int Id, string Name);

public sealed class RubricRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TotalPoints { get; set; }
    public int CriterionCount { get; set; }
    public int LevelCount { get; set; }
    public int QuestionCount { get; set; }
    public IReadOnlyList<string> LevelNames { get; set; } = [];
    public DateTime ModifiedDate { get; set; }
}

/// <summary>
/// The editor form. Rows and columns are both user defined, so the grid is posted as parallel
/// lists rather than a fixed shape.
/// </summary>
public sealed class RubricMatrixModel
{
    public int Id { get; set; }
    public bool IsEdit { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Column headers, left to right.</summary>
    public List<MatrixColumnInput> Columns { get; set; } = MatrixColumnInput.Defaults();

    /// <summary>Rows, top to bottom.</summary>
    public List<MatrixRowInput> Rows { get; set; } = [];

    public int TotalPoints { get; set; }

    /// <summary>Rebuilds a DTO graph, resolving each cell to the column it sits under.</summary>
    public RubricDto ToDto()
    {
        var columns = Columns
            .Where(column => column is not null && !string.IsNullOrWhiteSpace(column.Name))
            .ToList();

        var dto = new RubricDto
        {
            Id = Id,
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            Levels = columns.Select((column, index) => new RubricLevelDto
            {
                Name = column.Name,
                Points = Math.Max(0, column.Points),
                Description = column.Description ?? string.Empty,
                Order = index
            }).ToList()
        };

        var rowOrder = 0;
        foreach (var row in Rows.Where(row => row is not null && !string.IsNullOrWhiteSpace(row.Criterion)))
        {
            var criterion = new RubricCriterionDto
            {
                Criterion = row.Criterion,
                Description = row.Description ?? string.Empty,
                Order = rowOrder++
            };

            var cellOrder = 0;
            foreach (var cell in row.Cells ?? [])
            {
                if (cell is null)
                    continue;
                criterion.Levels.Add(new RubricCriterionLevelDto
                {
                    // The grid posts cells by column position, so name the column for binding.
                    LevelName = cellOrder < columns.Count ? columns[cellOrder].Name : string.Empty,
                    Descriptor = cell.Descriptor ?? string.Empty,
                    Points = cell.Points
                });
                cellOrder++;
            }

            dto.Criteria.Add(criterion);
        }

        return dto;
    }

    /// <summary>Points the editor starts from when nothing is stored yet.</summary>
    public static RubricMatrixModel Empty() => new()
    {
        Columns = MatrixColumnInput.Defaults(),
        Rows = [new MatrixRowInput { Cells = MatrixColumnInput.DefaultCells() }]
    };
}

public sealed class MatrixColumnInput
{
    public string Name { get; set; } = string.Empty;
    public int Points { get; set; }
    public string? Description { get; set; }

    public static List<MatrixColumnInput> Defaults() =>
    [
        new() { Name = "Below", Points = 2 },
        new() { Name = "Meets", Points = 4 },
        new() { Name = "Exceeds", Points = 6 }
    ];

    /// <summary>An empty cell matching the default column count.</summary>
    public static List<MatrixCellInput> DefaultCells() =>
        [.. Defaults().Select(_ => new MatrixCellInput())];
}

public sealed class MatrixRowInput
{
    public string Criterion { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<MatrixCellInput>? Cells { get; set; } = [];
    public bool IsNew { get; set; } = true;
}

public sealed class MatrixCellInput
{
    public string Descriptor { get; set; } = string.Empty;
    public int Points { get; set; }
}

/// <summary>Drives the AI rubric tab: describe what is needed, review the grid, then save it.</summary>
public sealed class AiRubricGeneratorViewModel
{
    [Required]
    [StringLength(120)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Topic { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? LearningOutcomes { get; set; }

    [Range(1, 10)]
    public int CriterionCount { get; set; } = 4;

    [Range(2, 6)]
    public int LevelCount { get; set; } = 4;

    public bool UseMaterials { get; set; } = true;
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }

    /// <summary>Null lets the router choose; otherwise force a provider.</summary>
    public AiProvider? Provider { get; set; }

    public bool GeminiAvailable { get; set; }
    public bool OllamaAvailable { get; set; }
    public AiProvider ActiveProvider { get; set; } = AiProvider.Gemini;

    public IReadOnlyList<SubjectOption> Subjects { get; set; } = [];
    public IReadOnlyList<TopicOption> Topics { get; set; } = [];
    public IReadOnlyList<TopicOption> AllTopics { get; set; } = [];

    public bool HasResult { get; set; }
    public RubricMatrixModel Generated { get; set; } = new();
    public List<MaterialExcerptViewModel> Sources { get; set; } = [];
    public bool Selected { get; set; } = true;
}