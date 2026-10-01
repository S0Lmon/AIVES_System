using AIVES.DTO;
namespace AIVES.BLL.Services.Catalog;

public sealed record RagContext(string Text, IReadOnlyList<MaterialExcerpt> Sources)
{
    public static readonly RagContext Empty = new(string.Empty, []);
}

public interface ICatalogService
{
    Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken = default);
    Task<SubjectDto> CreateSubjectAsync(SubjectInput input, CancellationToken cancellationToken = default);
    Task UpdateSubjectAsync(int id, SubjectInput input, CancellationToken cancellationToken = default);
    Task DeleteSubjectAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopicDto>> GetTopicsAsync(int? subjectId = null, CancellationToken cancellationToken = default);
    Task<TopicDto> CreateTopicAsync(TopicInput input, CancellationToken cancellationToken = default);
    Task UpdateTopicAsync(int id, TopicInput input, CancellationToken cancellationToken = default);
    Task DeleteTopicAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialDto>> GetMaterialsAsync(int? topicId = null, CancellationToken cancellationToken = default);
    Task<MaterialDto> CreateMaterialAsync(MaterialInput input, CancellationToken cancellationToken = default);
    Task UpdateMaterialAsync(int id, MaterialInput input, CancellationToken cancellationToken = default);
    Task DeleteMaterialAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Ranks active material for a topic (or subject) against the request query.</summary>
    Task<RagContext> BuildRagContextAsync(int? topicId, int? subjectId, string query, CancellationToken cancellationToken = default);
}