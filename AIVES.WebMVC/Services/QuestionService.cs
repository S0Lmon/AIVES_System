using AIVES.WebMVC.Data.Repositories;
using AIVES.WebMVC.Models.Entities;

namespace AIVES.WebMVC.Services
{
    public class QuestionService : IQuestionService
    {
        private readonly IQuestionRepository _questionRepository;
        private readonly IRepository<Rubric> _rubricRepository;
        private readonly IRepository<BloomLevel> _bloomLevelRepository;

        public QuestionService(
            IQuestionRepository questionRepository,
            IRepository<Rubric> rubricRepository,
            IRepository<BloomLevel> bloomLevelRepository)
        {
            _questionRepository = questionRepository;
            _rubricRepository = rubricRepository;
            _bloomLevelRepository = bloomLevelRepository;
        }

        public async Task<Question> GetQuestionByIdAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid question ID", nameof(id));

            return await _questionRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<Question>> GetAllQuestionsAsync()
        {
            return await _questionRepository.GetAllAsync();
        }

        public async Task<IEnumerable<Question>> GetActiveQuestionsAsync()
        {
            return await _questionRepository.GetActiveQuestionsAsync();
        }

        public async Task<IEnumerable<Question>> GetQuestionsByBloomLevelAsync(int bloomLevelId)
        {
            if (bloomLevelId <= 0)
                throw new ArgumentException("Invalid Bloom level ID", nameof(bloomLevelId));

            return await _questionRepository.GetByBloomLevelAsync(bloomLevelId);
        }

        public async Task<IEnumerable<Question>> GetQuestionsByContextAsync(string context)
        {
            if (string.IsNullOrWhiteSpace(context))
                throw new ArgumentException("Context cannot be empty", nameof(context));

            return await _questionRepository.GetQuestionsByContextAsync(context);
        }

        public async Task<Question> CreateQuestionAsync(Question question)
        {
            if (!await ValidateQuestionAsync(question))
                throw new InvalidOperationException("Question validation failed");

            question.CreatedDate = DateTime.UtcNow;
            question.ModifiedDate = DateTime.UtcNow;

            await _questionRepository.AddAsync(question);
            await _questionRepository.SaveChangesAsync();
            return question;
        }

        public async Task<Question> UpdateQuestionAsync(Question question)
        {
            if (question?.Id <= 0)
                throw new ArgumentException("Invalid question ID", nameof(question));

            if (!await ValidateQuestionAsync(question))
                throw new InvalidOperationException("Question validation failed");

            var existingQuestion = await _questionRepository.GetByIdAsync(question.Id);
            if (existingQuestion == null)
                throw new InvalidOperationException($"Question with ID {question.Id} not found");

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

        public async Task<bool> ValidateQuestionAsync(Question question)
        {
            if (question == null)
                return false;

            if (string.IsNullOrWhiteSpace(question.Content))
                return false;

            if (question.BloomLevelId <= 0)
                return false;

            if (question.RubricId <= 0)
                return false;
            var bloomLevel = await _bloomLevelRepository.GetByIdAsync(question.BloomLevelId);
            if (bloomLevel == null)
                return false;
            var rubric = await _rubricRepository.GetByIdAsync(question.RubricId);
            if (rubric == null)
                return false;

            return true;
        }
    }
}
