using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IMaterialRepository
{
    Task<IReadOnlyList<MaterialDto>> GetAllAsync(int? topicId = null, CancellationToken cancellationToken = default);
    Task<MaterialDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<MaterialDto> AddAsync(MaterialInput input, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, MaterialInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>Active materials for a topic, or for a whole subject when only subjectId is supplied.</summary>
    Task<IReadOnlyList<MaterialDto>> GetActiveForRagAsync(int? topicId, int? subjectId, int limit, CancellationToken cancellationToken = default);
}