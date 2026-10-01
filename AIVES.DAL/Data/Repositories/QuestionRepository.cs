using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class QuestionRepository(ApplicationDbContext context) : IQuestionRepository
{
    private IQueryable<Question> Query => context.Questions
        .Include(q => q.BloomLevel)
        .Include(q => q.Rubric)
        .Include(q => q.Topic)
        .Include(q => q.Subject);
    private static async Task<IEnumerable<QuestionDto>> ReadAsync(IQueryable<Question> query) => (await query.AsNoTracking().ToListAsync()).Select(DtoMapping.ToDto).ToList();
    public async Task<QuestionDto?> GetByIdAsync(int id)
    {
        var q = await Query.AsNoTracking().SingleOrDefaultAsync(q => q.Id == id);
        return q is null ? null : DtoMapping.ToDto(q);
    }
    public Task<IEnumerable<QuestionDto>> GetAllAsync() => ReadAsync(Query);
    public Task<IEnumerable<QuestionDto>> GetByBloomLevelAsync(int id) => ReadAsync(Query.Where(q => q.BloomLevelId == id));
    public Task<IEnumerable<QuestionDto>> GetByRubricAsync(int id) => ReadAsync(Query.Where(q => q.RubricId == id));
    public Task<IEnumerable<QuestionDto>> GetBySubjectAsync(int id) => ReadAsync(Query.Where(q => q.SubjectId == id));
    public Task<IEnumerable<QuestionDto>> GetByTopicAsync(int id) => ReadAsync(Query.Where(q => q.TopicId == id));
    public Task<IEnumerable<QuestionDto>> GetActiveQuestionsAsync() => ReadAsync(Query.Where(q => q.IsActive).OrderBy(q => q.DisplayOrder));
    public Task<IEnumerable<QuestionDto>> GetQuestionsByContextAsync(string value) => ReadAsync(Query.Where(q => q.Context == value && q.IsActive));
    public async Task AddAsync(QuestionDto dto)
    {
        var entity = new Question();
        DtoMapping.Apply(dto, entity);
        context.Questions.Add(entity);
        await context.SaveChangesAsync();
        dto.Id = entity.Id;
    }
    public async Task UpdateAsync(QuestionDto dto)
    {
        var entity = await context.Questions.FindAsync(dto.Id) ?? throw new InvalidOperationException($"Question with ID {dto.Id} not found");
        DtoMapping.Apply(dto, entity);
    }
    public async Task DeleteAsync(QuestionDto dto)
    {
        var entity = await context.Questions.FindAsync(dto.Id);
        if (entity is not null)
            context.Questions.Remove(entity);
    }
    public async Task SaveChangesAsync() => await context.SaveChangesAsync();
}
