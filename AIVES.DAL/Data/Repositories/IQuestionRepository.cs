using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IQuestionRepository
{
    Task<QuestionDto?> GetByIdAsync(int id);
    Task<IEnumerable<QuestionDto>> GetAllAsync();
    Task<IEnumerable<QuestionDto>> GetByBloomLevelAsync(int id);
    Task<IEnumerable<QuestionDto>> GetByRubricAsync(int id);
    Task<IEnumerable<QuestionDto>> GetBySubjectAsync(int id);
    Task<IEnumerable<QuestionDto>> GetByTopicAsync(int id);
    Task<IEnumerable<QuestionDto>> GetActiveQuestionsAsync();
    Task<IEnumerable<QuestionDto>> GetQuestionsByContextAsync(string context);
    Task AddAsync(QuestionDto question);
    Task UpdateAsync(QuestionDto question);
    Task DeleteAsync(QuestionDto question);
    Task SaveChangesAsync();
}
