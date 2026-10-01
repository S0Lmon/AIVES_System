using AIVES.DTO;

namespace AIVES.BLL.Services
{
    public interface IQuestionService
    {
        Task<QuestionDto?> GetQuestionByIdAsync(int id);
        Task<IEnumerable<QuestionDto>> GetAllQuestionsAsync();
        Task<IEnumerable<QuestionDto>> GetActiveQuestionsAsync();
        Task<IEnumerable<QuestionDto>> GetQuestionsByBloomLevelAsync(int bloomLevelId);
        Task<IEnumerable<QuestionDto>> GetQuestionsBySubjectAsync(int subjectId);
        Task<IEnumerable<QuestionDto>> GetQuestionsByTopicAsync(int topicId);
        Task<IEnumerable<QuestionDto>> GetQuestionsByContextAsync(string context);
        Task<QuestionDto> CreateQuestionAsync(QuestionDto question);
        Task<QuestionDto> UpdateQuestionAsync(QuestionDto question);
        Task DeleteQuestionAsync(int id);
        Task<bool> ValidateQuestionAsync(QuestionDto question);
    }
}
