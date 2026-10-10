using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
namespace AIVES.BLL.Services;

public sealed class BloomLevelService(IBloomLevelRepository repository) : IBloomLevelService
{
    public Task<IEnumerable<BloomLevelDto>> GetAllAsync() => repository.GetAllAsync();
}
