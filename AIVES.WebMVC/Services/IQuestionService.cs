using AIVES.WebMVC.Models.Entities;

namespace AIVES.WebMVC.Services
{
    public interface IQuestionService
    {
        Task<Question> GetQuestionByIdAsync(int id);
        Task<IEnumerable<Question>> GetAllQuestionsAsync();
        Task<IEnumerable<Question>> GetActiveQuestionsAsync();
        Task<IEnumerable<Question>> GetQuestionsByBloomLevelAsync(int bloomLevelId);
        Task<IEnumerable<Question>> GetQuestionsByContextAsync(string context);
        Task<Question> CreateQuestionAsync(Question question);
        Task<Question> UpdateQuestionAsync(Question question);
        Task DeleteQuestionAsync(int id);
        Task<bool> ValidateQuestionAsync(Question question);
    }
}
