using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class SubjectRepository(ApplicationDbContext context) : ISubjectRepository
{
    private static SubjectDto ToDto(Subject subject) => new(subject.Id, subject.Name, subject.Description,
        subject.Topics.Count, subject.Topics.Sum(topic => topic.Materials.Count));

    public async Task<IReadOnlyList<SubjectDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await context.Subjects.AsNoTracking()
            .Include(subject => subject.Topics).ThenInclude(topic => topic.Materials)
            .AsSplitQuery()
            .OrderBy(subject => subject.Name)
            .Select(subject => ToDto(subject))
            .ToListAsync(cancellationToken);

    public async Task<SubjectDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await context.Subjects.AsNoTracking()
            .Include(subject => subject.Topics).ThenInclude(topic => topic.Materials)
            .AsSplitQuery()
            .FirstOrDefaultAsync(subject => subject.Id == id, cancellationToken) is { } subject ? ToDto(subject) : null;

    public async Task<SubjectInput> AddAsync(SubjectInput input, CancellationToken cancellationToken = default)
    {
        var entity = new Subject { Name = input.Name, Description = input.Description };
        context.Subjects.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return input with { Name = entity.Name };
    }

    public async Task UpdateAsync(int id, SubjectInput input, CancellationToken cancellationToken = default)
    {
        var entity = await context.Subjects.FindAsync([id], cancellationToken)
            ?? throw new KeyNotFoundException($"Subject {id} was not found.");
        entity.Name = input.Name;
        entity.Description = input.Description;
        entity.ModifiedDate = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.Subjects.FindAsync([id], cancellationToken);
        if (entity is null)
            return;
        context.Subjects.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }
}