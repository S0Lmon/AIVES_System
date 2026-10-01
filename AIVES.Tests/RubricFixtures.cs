using AIVES.DTO;

namespace AIVES.Tests;

/// <summary>
/// A rubric is a matrix, so tests that only care about the header still need one row and one
/// column to get past validation.
/// </summary>
internal static class RubricFixtures
{
    /// <summary>A valid two by two matrix: two criteria across two levels.</summary>
    public static RubricDto Matrix(string name, string description = "Two rows, two columns")
    {
        var dto = new RubricDto { Name = name, Description = description };
        foreach (var (levelName, points) in new[] { ("Below", 1), ("Exceeds", 4) })
        {
            dto.Levels.Add(new RubricLevelDto { Name = levelName, Points = points });
        }

        foreach (var row in new[] { "Correctness", "Reasoning" })
        {
            var criterion = new RubricCriterionDto { Criterion = row };
            foreach (var level in dto.Levels)
            {
                criterion.Levels.Add(new RubricCriterionLevelDto
                {
                    LevelName = level.Name,
                    Descriptor = $"{row} at {level.Name}",
                    Points = level.Points
                });
            }

            dto.Criteria.Add(criterion);
        }

        return dto;
    }
}