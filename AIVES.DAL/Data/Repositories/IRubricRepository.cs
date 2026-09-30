using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IRubricRepository
{
    Task<RubricDto?> GetByIdAsync(int id);
    Task<IEnumerable<RubricDto>> GetAllAsync();
    Task AddAsync(RubricDto dto);
    Task UpdateAsync(RubricDto dto);
    Task DeleteAsync(RubricDto dto);
    Task SaveChangesAsync();
}
