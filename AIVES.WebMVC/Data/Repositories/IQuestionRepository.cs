using AIVES.WebMVC.Models.Entities;

namespace AIVES.WebMVC.Data.Repositories
{
    public interface IQuestionRepository : IRepository<Question>
    {
        Task<IEnumerable<Question>> GetByBloomLevelAsync(int bloomLevelId);
        Task<IEnumerable<Question>> GetByRubricAsync(int rubricId);
        Task<IEnumerable<Question>> GetActiveQuestionsAsync();
        Task<IEnumerable<Question>> GetQuestionsByContextAsync(string context);
    }
}
