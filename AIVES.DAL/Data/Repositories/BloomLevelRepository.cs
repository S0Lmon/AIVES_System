using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class BloomLevelRepository(ApplicationDbContext context) : IBloomLevelRepository
{
    public async Task<BloomLevelDto?> GetByIdAsync(int id)
    {
        var b = await context.BloomLevels.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        return b is null ? null : DtoMapping.ToDto(b);
    }
    public async Task<IEnumerable<BloomLevelDto>> GetAllAsync() => (await context.BloomLevels.AsNoTracking().OrderBy(b => b.Order).ToListAsync()).Select(DtoMapping.ToDto).ToList();
}
