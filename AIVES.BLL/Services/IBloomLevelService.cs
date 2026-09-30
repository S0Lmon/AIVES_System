using AIVES.DTO;
namespace AIVES.BLL.Services;

public interface IBloomLevelService
{
    Task<IEnumerable<BloomLevelDto>> GetAllAsync();
}
