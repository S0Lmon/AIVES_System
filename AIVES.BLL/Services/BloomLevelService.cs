using AIVES.DTO;
using AIVES.DAL.Data.Repositories;
namespace AIVES.BLL.Services;

public sealed class BloomLevelService(IBloomLevelRepository repository) : IBloomLevelService
{
    public Task<IEnumerable<BloomLevelDto>> GetAllAsync() => repository.GetAllAsync();
}
