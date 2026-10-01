using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface ITopicRepository
{
    Task<IReadOnlyList<TopicDto>> GetAllAsync(int? subjectId = null, CancellationToken cancellationToken = default);
    Task<TopicDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<TopicInput> AddAsync(TopicInput input, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, TopicInput input, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}