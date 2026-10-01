using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class MaterialRepository(ApplicationDbContext context) : IMaterialRepository
{
    private static MaterialDto ToDto(Material material) => new(material.Id, material.TopicId, material.Topic.Name,
        material.Topic.Subject.Name, material.Title, material.Content, material.SourceFileName, material.SourceType,
        material.IsActive, material.CreatedDate, material.ModifiedDate, material.Content.Length);

    public async Task<IReadOnlyList<MaterialDto>> GetAllAsync(int? topicId = null, CancellationToken cancellationToken = default)
    {
        IQueryable<Material> query = context.Materials.AsNoTracking().Include(material => material.Topic).ThenInclude(topic => topic.Subject);
        if (topicId.HasValue)
            query = query.Where(material => material.TopicId == topicId.Value);
        return (await query.OrderByDescending(material => material.CreatedDate).ToListAsync(cancellationToken)).Select(ToDto).ToList();
    }

    public async Task<MaterialDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await context.Materials.AsNoTracking().Include(material => material.Topic).ThenInclude(topic => topic.Subject)
            .FirstOrDefaultAsync(material => material.Id == id, cancellationToken) is { } material ? ToDto(material) : null;

    public async Task<MaterialDto> AddAsync(MaterialInput input, CancellationToken cancellationToken = default)
    {
        if (!await context.Topics.AnyAsync(topic => topic.Id == input.TopicId, cancellationToken))
            throw new KeyNotFoundException($"Topic {input.TopicId} was not found.");

        var entity = new Material
        {
            TopicId = input.TopicId,
            Title = input.Title,
            Content = input.Content,
            SourceFileName = input.SourceFileName,
            SourceType = input.SourceType,
            IsActive = input.IsActive
        };
        context.Materials.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return new MaterialDto(entity.Id, entity.TopicId, string.Empty, string.Empty, entity.Title, entity.Content,
            entity.SourceFileName, entity.SourceType, entity.IsActive, entity.CreatedDate, entity.ModifiedDate, entity.Content.Length);
    }

    public async Task UpdateAsync(int id, MaterialInput input, CancellationToken cancellationToken = default)
    {
        var entity = await context.Materials.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"Material {id} was not found.");
        if (!await context.Topics.AnyAsync(topic => topic.Id == input.TopicId, cancellationToken))
            throw new KeyNotFoundException($"Topic {input.TopicId} was not found.");

        entity.TopicId = input.TopicId;
        entity.Title = input.Title;
        entity.Content = input.Content;
        entity.SourceFileName = input.SourceFileName ?? entity.SourceFileName;
        entity.SourceType = input.SourceType;
        entity.IsActive = input.IsActive;
        entity.ModifiedDate = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.Materials.FindAsync([id], cancellationToken);
        if (entity is null)
            return;
        context.Materials.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => context.Materials.CountAsync(cancellationToken);

    public async Task<IReadOnlyList<MaterialDto>> GetActiveForRagAsync(int? topicId, int? subjectId, int limit, CancellationToken cancellationToken = default)
    {
        var query = context.Materials.AsNoTracking()
            .Include(material => material.Topic).ThenInclude(topic => topic.Subject)
            .Where(material => material.IsActive && material.Content.Length > 0);

        if (topicId.HasValue)
            query = query.Where(material => material.TopicId == topicId.Value);
        else if (subjectId.HasValue)
            query = query.Where(material => material.Topic.SubjectId == subjectId.Value);

        return (await query.OrderByDescending(material => material.ModifiedDate).Take(limit).ToListAsync(cancellationToken)).Select(ToDto).ToList();
    }
}