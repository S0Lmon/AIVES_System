using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;

namespace AIVES.DAL.Data.Repositories;

public sealed class RubricRepository(ApplicationDbContext context) : IRubricRepository
{
    private static IQueryable<Rubric> Graph(IQueryable<Rubric> source) => source
        .Include(rubric => rubric.Levels)
            .ThenInclude(level => level.Cells)
        .Include(rubric => rubric.Criteria)
            .ThenInclude(criterion => criterion.Levels)
        .AsSplitQuery();

    public async Task<RubricDto?> GetByIdAsync(int id)
    {
        var r = await Graph(context.Rubrics.AsNoTracking()).SingleOrDefaultAsync(r => r.Id == id);
        return r is null ? null : DtoMapping.ToDto(r);
    }

    public async Task<IEnumerable<RubricDto>> GetAllAsync() =>
        (await Graph(context.Rubrics.AsNoTracking()).OrderBy(rubric => rubric.Name).ToListAsync()).Select(DtoMapping.ToDto).ToList();

    private static void Apply(RubricDto dto, Rubric entity)
    {
        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.TotalPoints = dto.TotalPoints;
        entity.CreatedDate = dto.CreatedDate;
        entity.ModifiedDate = dto.ModifiedDate;
    }

    /// <summary>
    /// Rebuilds the rows and columns. Cells are attached once both sides exist so the matrix is
    /// only wired up after the criteria and levels have real identifiers.
    /// </summary>
    private static void AttachGraph(RubricDto dto, Rubric entity)
    {
        entity.Levels.Clear();
        entity.Criteria.Clear();

        foreach (var levelDto in dto.Levels.OrderBy(level => level.Order))
        {
            var level = new RubricLevel
            {
                Rubric = entity,
                Name = levelDto.Name,
                Points = levelDto.Points,
                Description = levelDto.Description,
                Order = levelDto.Order
            };
            entity.Levels.Add(level);
        }

        foreach (var criterionDto in dto.Criteria.OrderBy(criterion => criterion.Order))
        {
            var criterion = new RubricCriterion
            {
                Rubric = entity,
                Criterion = criterionDto.Criterion,
                Description = criterionDto.Description,
                MaxPoints = criterionDto.MaxPoints,
                Order = criterionDto.Order
            };
            entity.Criteria.Add(criterion);

            foreach (var cellDto in criterionDto.Levels)
            {
                var level = entity.Levels.FirstOrDefault(candidate => candidate.Id == cellDto.RubricLevelId)
                    ?? entity.Levels.FirstOrDefault(candidate => string.Equals(candidate.Name, cellDto.LevelName, StringComparison.OrdinalIgnoreCase));
                if (level is null)
                    continue;
                // Added to the criterion collection so the cell is tracked, and pointed at the
                // column so the foreign key is set once EF assigns the level an identifier.
                criterion.Levels.Add(new RubricCriterionLevel
                {
                    RubricLevel = level,
                    Descriptor = cellDto.Descriptor,
                    Points = cellDto.Points
                });
            }
        }
    }

    public async Task AddAsync(RubricDto dto)
    {
        var entity = new Rubric();
        Apply(dto, entity);
        AttachGraph(dto, entity);
        context.Rubrics.Add(entity);
        await context.SaveChangesAsync();
        dto.Id = entity.Id;
    }

    /// <summary>
    /// The submitted grid replaces the stored one wholesale. Reconciling row by row would keep
    /// orphaned descriptors around whenever a lecturer removes a row or column.
    /// </summary>
    public Task UpdateAsync(RubricDto dto)
    {
        var entity = Graph(context.Rubrics).SingleOrDefault(rubric => rubric.Id == dto.Id)
            ?? throw new InvalidOperationException($"Rubric with ID {dto.Id} not found");

        context.RubricCriterionLevels.RemoveRange(entity.Levels.SelectMany(level => level.Cells).Concat(entity.Criteria.SelectMany(criterion => criterion.Levels)));
        entity.Levels.Clear();
        entity.Criteria.Clear();
        Apply(dto, entity);
        AttachGraph(dto, entity);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(RubricDto dto)
    {
        // FindAsync checks the change tracker first and then queries, because the rubric is
        // read AsNoTracking and so is not already tracked here.
        var entity = await context.Rubrics.FindAsync(dto.Id);
        if (entity is not null)
            context.Rubrics.Remove(entity);
    }

    public async Task SaveChangesAsync() => await context.SaveChangesAsync();
}