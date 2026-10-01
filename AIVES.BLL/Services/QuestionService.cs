using AIVES.DAL.Data.Repositories;
using AIVES.DTO;

namespace AIVES.BLL.Services
{
    public class QuestionService : IQuestionService
    {
        private readonly IQuestionRepository _questionRepository;
        private readonly IRubricRepository _rubricRepository;
        private readonly IBloomLevelRepository _bloomLevelRepository;

        public QuestionService(
            IQuestionRepository questionRepository,
            IRubricRepository rubricRepository,
            IBloomLevelRepository bloomLevelRepository)
        {
            _questionRepository = questionRepository;
            _rubricRepository = rubricRepository;
            _bloomLevelRepository = bloomLevelRepository;
        }

        public async Task<QuestionDto?> GetQuestionByIdAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid question ID", nameof(id));

            return await _questionRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<QuestionDto>> GetAllQuestionsAsync()
        {
            return await _questionRepository.GetAllAsync();
        }

        public async Task<IEnumerable<QuestionDto>> GetActiveQuestionsAsync()
        {
            return await _questionRepository.GetActiveQuestionsAsync();
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByBloomLevelAsync(int bloomLevelId)
        {
            if (bloomLevelId <= 0)
                throw new ArgumentException("Invalid Bloom level ID", nameof(bloomLevelId));

            return await _questionRepository.GetByBloomLevelAsync(bloomLevelId);
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsBySubjectAsync(int subjectId)
        {
            if (subjectId <= 0)
                throw new ArgumentException("Invalid subject ID", nameof(subjectId));

            return await _questionRepository.GetBySubjectAsync(subjectId);
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByTopicAsync(int topicId)
        {
            if (topicId <= 0)
                throw new ArgumentException("Invalid topic ID", nameof(topicId));

            return await _questionRepository.GetByTopicAsync(topicId);
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByContextAsync(string context)
        {
            if (string.IsNullOrWhiteSpace(context))
                throw new ArgumentException("Context cannot be empty", nameof(context));

            return await _questionRepository.GetQuestionsByContextAsync(context);
        }

        public async Task<QuestionDto> CreateQuestionAsync(QuestionDto question)
        {
            if (!await ValidateQuestionAsync(question))
                throw new InvalidOperationException("Question validation failed");

            question.CreatedDate = DateTime.UtcNow;
            question.ModifiedDate = DateTime.UtcNow;

            await _questionRepository.AddAsync(question);
            return question;
        }

        public async Task<QuestionDto> UpdateQuestionAsync(QuestionDto question)
        {
            if (question is null || question.Id <= 0)
                throw new ArgumentException("Invalid question ID", nameof(question));

            if (!await ValidateQuestionAsync(question))
                throw new InvalidOperationException("Question validation failed");

            var existingQuestion = await _questionRepository.GetByIdAsync(question.Id);
            if (existingQuestion == null)
                throw new InvalidOperationException($"Question with ID {question.Id} not found");

            question.CreatedDate = existingQuestion.CreatedDate;
            question.ModifiedDate = DateTime.UtcNow;
            await _questionRepository.UpdateAsync(question);
            await _questionRepository.SaveChangesAsync();
            return question;
        }

        public async Task DeleteQuestionAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid question ID", nameof(id));

            var question = await _questionRepository.GetByIdAsync(id);
            if (question == null)
                throw new InvalidOperationException($"Question with ID {id} not found");

            await _questionRepository.DeleteAsync(question);
            await _questionRepository.SaveChangesAsync();
        }

        public async Task<bool> ValidateQuestionAsync(QuestionDto question)
        {
            if (question == null)
                return false;

            if (string.IsNullOrWhiteSpace(question.Content))
                return false;

            if (question.Content.Length is < 10 or > 5000 || (question.Context?.Length ?? 0) > 300
                || (question.ExpectedAnswer?.Length ?? 0) > 5000 || question.DisplayOrder is < 0 or > 10000)
                return false;

            if (question.BloomLevelId <= 0)
                return false;

            var bloomLevel = await _bloomLevelRepository.GetByIdAsync(question.BloomLevelId);
            if (bloomLevel == null)
                return false;

            // Rubric, subject and topic are all optional links. When a rubric is chosen it has to
            // exist, but leaving it blank must save rather than fail the way it used to.
            if (question.RubricId is > 0)
            {
                var rubric = await _rubricRepository.GetByIdAsync(question.RubricId.Value);
                if (rubric == null)
                    return false;
            }

            if (question.SubjectId is <= 0)
                return false;

            // Difficulty is optional, but a value that is present has to be one the app knows, so a
            // stale or hand-edited value cannot quietly enter the bank.
            if (question.Difficulty is not null && !QuestionDifficulties.IsAllowed(question.Difficulty))
                return false;

            if (question.TopicId is <= 0)
                return false;

            return true;
        }
    }
}
