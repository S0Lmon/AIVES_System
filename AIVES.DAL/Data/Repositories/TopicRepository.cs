using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class TopicRepository(ApplicationDbContext context) : ITopicRepository
{
    private static TopicDto ToDto(Topic topic) => new(topic.Id, topic.SubjectId, topic.Subject.Name, topic.Name,
        topic.Description, topic.Materials.Count);

    public async Task<IReadOnlyList<TopicDto>> GetAllAsync(int? subjectId = null, CancellationToken cancellationToken = default)
    {
        IQueryable<Topic> query = context.Topics.AsNoTracking().Include(topic => topic.Subject).Include(topic => topic.Materials);
        if (subjectId.HasValue)
            query = query.Where(topic => topic.SubjectId == subjectId.Value);
        return await query.OrderBy(topic => topic.Subject.Name).ThenBy(topic => topic.Name).ToListAsync(cancellationToken) is { } list
            ? list.Select(ToDto).ToList()
            : [];
    }

    public async Task<TopicDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await context.Topics.AsNoTracking().Include(topic => topic.Subject).Include(topic => topic.Materials)
            .FirstOrDefaultAsync(topic => topic.Id == id, cancellationToken) is { } topic ? ToDto(topic) : null;

    public async Task<TopicInput> AddAsync(TopicInput input, CancellationToken cancellationToken = default)
    {
        if (!await context.Subjects.AnyAsync(subject => subject.Id == input.SubjectId, cancellationToken))
            throw new KeyNotFoundException($"Subject {input.SubjectId} was not found.");

        var entity = new Topic { SubjectId = input.SubjectId, Name = input.Name, Description = input.Description };
        context.Topics.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return input;
    }

    public async Task UpdateAsync(int id, TopicInput input, CancellationToken cancellationToken = default)
    {
        var entity = await context.Topics.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"Topic {id} was not found.");
        if (!await context.Subjects.AnyAsync(subject => subject.Id == input.SubjectId, cancellationToken))
            throw new KeyNotFoundException($"Subject {input.SubjectId} was not found.");

        entity.SubjectId = input.SubjectId;
        entity.Name = input.Name;
        entity.Description = input.Description;
        entity.ModifiedDate = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.Topics.FindAsync([id], cancellationToken);
        if (entity is null)
            return;
        context.Topics.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}