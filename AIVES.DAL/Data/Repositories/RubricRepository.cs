using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class RubricRepository(ApplicationDbContext context) : IRubricRepository
{
    public async Task<RubricDto?> GetByIdAsync(int id)
    {
        var r = await context.Rubrics.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
        return r is null ? null : DtoMapping.ToDto(r);
    }
    public async Task<IEnumerable<RubricDto>> GetAllAsync() => (await context.Rubrics.AsNoTracking().ToListAsync()).Select(DtoMapping.ToDto).ToList();
    private static void Apply(RubricDto dto, Rubric entity)
    {
        entity.Name = dto.Name;
        entity.Description = dto.Description;
        entity.TotalPoints = dto.TotalPoints;
        entity.CreatedDate = dto.CreatedDate;
        entity.ModifiedDate = dto.ModifiedDate;
    }
    public async Task AddAsync(RubricDto dto)
    {
        var entity = new Rubric();
        Apply(dto, entity);
        context.Rubrics.Add(entity);
        await context.SaveChangesAsync();
        dto.Id = entity.Id;
    }
    public async Task UpdateAsync(RubricDto dto)
    {
        var entity = await context.Rubrics.FindAsync(dto.Id) ?? throw new InvalidOperationException($"Rubric with ID {dto.Id} not found");
        Apply(dto, entity);
    }
    public async Task DeleteAsync(RubricDto dto)
    {
        var entity = await context.Rubrics.FindAsync(dto.Id);
        if (entity is not null)
            context.Rubrics.Remove(entity);
    }
    public async Task SaveChangesAsync() => await context.SaveChangesAsync();
}
