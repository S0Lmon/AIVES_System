using AIVES.BLL.Services;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;

namespace AIVES.Tests;

/// <summary>
/// Covers the matrix rules: a rubric needs at least one row and one column, cells must line up
/// with a column, and the stored total is derived from the grid rather than trusted.
/// </summary>
public sealed class RubricMatrixTests
{
    private static ApplicationDbContext CreateDatabase(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"rubric-matrix-{name}-{Guid.NewGuid():N}")
            .Options);

    [Fact]
    public async Task MatrixRoundTripsRowsColumnsAndCells()
    {
        using var db = CreateDatabase("round-trip");
        var service = new RubricService(new RubricRepository(db));

        var saved = await service.CreateRubricAsync(RubricFixtures.Matrix("Oral defence"));

        var stored = await service.GetRubricByIdAsync(saved.Id);
        Assert.NotNull(stored);
        Assert.Equal(2, stored!.Levels.Count);
        Assert.Equal(2, stored.Criteria.Count);
        Assert.Equal("Below", stored.Levels.OrderBy(level => level.Order).First().Name);

        var criterion = stored.Criteria.OrderBy(row => row.Order).First();
        Assert.Equal(2, criterion.Levels.Count);
        Assert.All(criterion.Levels, cell => Assert.False(string.IsNullOrWhiteSpace(cell.Descriptor)));
        Assert.All(criterion.Levels, cell => Assert.Contains(stored.Levels, level => level.Id == cell.RubricLevelId));
    }

    [Fact]
    public async Task TotalIsDerivedFromTheBestCellInEachRow()
    {
        using var db = CreateDatabase("total");
        var service = new RubricService(new RubricRepository(db));

        // Below is worth 1 and Exceeds 4, so each of the two rows caps at 4.
        var saved = await service.CreateRubricAsync(RubricFixtures.Matrix("Derived total"));

        var stored = await service.GetRubricByIdAsync(saved.Id);
        Assert.Equal(8, stored!.TotalPoints);
        Assert.All(stored.Criteria, criterion => Assert.Equal(4, criterion.MaxPoints));
    }

    [Fact]
    public async Task ARubricWithoutRowsOrColumnsIsRejected()
    {
        using var db = CreateDatabase("empty");
        var service = new RubricService(new RubricRepository(db));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateRubricAsync(new RubricDto { Name = "No rows", Levels = [new RubricLevelDto { Name = "Only", Points = 1 }] }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateRubricAsync(new RubricDto { Name = "No columns", Criteria = [new RubricCriterionDto { Criterion = "Only row" }] }));

        Assert.Empty(db.Rubrics);
    }

    [Fact]
    public async Task ACellThatNamesNoColumnIsRejected()
    {
        using var db = CreateDatabase("orphan-cell");
        var service = new RubricService(new RubricRepository(db));

        var rubric = RubricFixtures.Matrix("Orphan");
        rubric.Criteria[0].Levels[0].LevelName = "Not a column";

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRubricAsync(rubric));
        Assert.Empty(db.Rubrics);
    }

    [Fact]
    public async Task RepeatingAColumnInOneRowIsRejected()
    {
        using var db = CreateDatabase("repeat-cell");
        var service = new RubricService(new RubricRepository(db));

        var rubric = RubricFixtures.Matrix("Repeat");
        rubric.Criteria[0].Levels.Add(new RubricCriterionLevelDto
        {
            LevelName = rubric.Criteria[0].Levels[0].LevelName,
            Descriptor = "Duplicate",
            Points = 3
        });

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRubricAsync(rubric));
        Assert.Empty(db.Rubrics);
    }

    [Fact]
    public async Task DuplicateColumnNamesAreRejected()
    {
        using var db = CreateDatabase("duplicate-level");
        var service = new RubricService(new RubricRepository(db));

        var rubric = RubricFixtures.Matrix("Duplicate levels");
        rubric.Levels[1].Name = rubric.Levels[0].Name;

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateRubricAsync(rubric));
        Assert.Empty(db.Rubrics);
    }

    [Fact]
    public async Task BlankRowsAndColumnsAreDroppedRatherThanStored()
    {
        using var db = CreateDatabase("blank");
        var service = new RubricService(new RubricRepository(db));

        var rubric = RubricFixtures.Matrix("Trimmed");
        rubric.Criteria.Add(new RubricCriterionDto { Criterion = "   " });
        rubric.Levels.Add(new RubricLevelDto { Name = string.Empty, Points = 9 });

        var saved = await service.CreateRubricAsync(rubric);

        var stored = await service.GetRubricByIdAsync(saved.Id);
        Assert.Equal(2, stored!.Criteria.Count);
        Assert.Equal(2, stored.Levels.Count);
    }

    [Fact]
    public async Task UpdatingReplacesTheGridSoRemovedRowsLeaveNothingBehind()
    {
        using var db = CreateDatabase("update");
        var service = new RubricService(new RubricRepository(db));

        var saved = await service.CreateRubricAsync(RubricFixtures.Matrix("Original grid"));
        var before = await db.RubricCriterionLevels.CountAsync();

        var replacement = RubricFixtures.Matrix("Narrower grid");
        replacement.Id = saved.Id;
        replacement.Criteria.RemoveAt(1);
        replacement.Levels.RemoveAt(1);
        foreach (var criterion in replacement.Criteria)
        {
            criterion.Levels.RemoveAt(1);
        }

        await service.UpdateRubricAsync(replacement);

        var stored = await service.GetRubricByIdAsync(saved.Id);
        Assert.Single(stored!.Criteria);
        Assert.Single(stored.Levels);
        Assert.Single(stored.Criteria[0].Levels);
        // Four cells become one, and nothing is orphaned in the database.
        Assert.Equal(1, await db.RubricCriterionLevels.CountAsync());
        Assert.True(before > 1);
    }

    [Fact]
    public async Task DeletingARubricRemovesItsRowsColumnsAndCells()
    {
        using var db = CreateDatabase("delete");
        var service = new RubricService(new RubricRepository(db));

        var saved = await service.CreateRubricAsync(RubricFixtures.Matrix("Doomed"));

        // Read it back the way a controller does before deleting, so the delete path is
        // exercised against an untracked instance.
        Assert.NotNull(await service.GetRubricByIdAsync(saved.Id));
        await service.DeleteRubricAsync(saved.Id);

        Assert.Empty(db.Rubrics);
        Assert.Empty(db.RubricLevels);
        Assert.Empty(db.RubricCriteria);
        Assert.Empty(db.RubricCriterionLevels);
    }
}