using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IBloomLevelRepository
{
    Task<BloomLevelDto?> GetByIdAsync(int id);
    Task<IEnumerable<BloomLevelDto>> GetAllAsync();
}
