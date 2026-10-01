using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface ISubjectRepository
{
    Task<IReadOnlyList<SubjectDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<SubjectDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<SubjectDto> AddAsync(SubjectInput input, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, SubjectInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}