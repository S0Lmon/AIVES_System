using AIVES.WebMVC.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIVES.WebMVC.Data.Repositories
{
    public class QuestionRepository : Repository<Question>, IQuestionRepository
    {
        public QuestionRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Question>> GetByBloomLevelAsync(int bloomLevelId)
        {
            return await _dbSet
                .Where(q => q.BloomLevelId == bloomLevelId)
                .Include(q => q.BloomLevel)
                .Include(q => q.Rubric)
                .ToListAsync();
        }

        public async Task<IEnumerable<Question>> GetByRubricAsync(int rubricId)
        {
            return await _dbSet
                .Where(q => q.RubricId == rubricId)
                .Include(q => q.BloomLevel)
                .Include(q => q.Rubric)
                .ToListAsync();
        }

        public async Task<IEnumerable<Question>> GetActiveQuestionsAsync()
        {
            return await _dbSet
                .Where(q => q.IsActive)
                .Include(q => q.BloomLevel)
                .Include(q => q.Rubric)
                .OrderBy(q => q.DisplayOrder)
                .ToListAsync();
        }

        public async Task<IEnumerable<Question>> GetQuestionsByContextAsync(string context)
        {
            return await _dbSet
                .Where(q => q.Context == context && q.IsActive)
                .Include(q => q.BloomLevel)
                .Include(q => q.Rubric)
                .ToListAsync();
        }

        public override async Task<IEnumerable<Question>> GetAllAsync()
        {
            return await _dbSet
                .Include(q => q.BloomLevel)
                .Include(q => q.Rubric)
                .ToListAsync();
        }
    }
}
