namespace AIVES.DTO;

/// <summary>
/// A rubric matrix proposed by the model: one set of columns and one set of rows, with a
/// descriptor and a point value for every intersection.
/// </summary>
public sealed class GeneratedRubric
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<GeneratedRubricLevel> Levels { get; set; } = [];
    public List<GeneratedRubricCriterion> Criteria { get; set; } = [];
}

/// <summary>A column: the name of the level and the score it is worth.</summary>
public sealed class GeneratedRubricLevel
{
    public string Name { get; set; } = string.Empty;
    public int Points
    {
        get; set;
    }
}

/// <summary>A row, plus one descriptor per column keyed by the column name.</summary>
public sealed class GeneratedRubricCriterion
{
    public string Criterion { get; set; } = string.Empty;
    public List<GeneratedRubricCell> Cells { get; set; } = [];
}

/// <summary>One cell of the generated grid.</summary>
public sealed class GeneratedRubricCell
{
    public string LevelName { get; set; } = string.Empty;
    public string Descriptor { get; set; } = string.Empty;
    public int Points
    {
        get; set;
    }
}