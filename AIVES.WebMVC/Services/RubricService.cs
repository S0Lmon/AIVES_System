using AIVES.WebMVC.Data.Repositories;
using AIVES.WebMVC.Models.Entities;

namespace AIVES.WebMVC.Services
{
    public class RubricService : IRubricService
    {
        private readonly IRepository<Rubric> _rubricRepository;
        private readonly IRepository<RubricCriterion> _criterionRepository;

        public RubricService(
            IRepository<Rubric> rubricRepository,
            IRepository<RubricCriterion> criterionRepository)
        {
            _rubricRepository = rubricRepository;
            _criterionRepository = criterionRepository;
        }

        public async Task<Rubric> GetRubricByIdAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid rubric ID", nameof(id));

            return await _rubricRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<Rubric>> GetAllRubricsAsync()
        {
            return await _rubricRepository.GetAllAsync();
        }

        public async Task<Rubric> CreateRubricAsync(Rubric rubric)
        {
            if (rubric == null || string.IsNullOrWhiteSpace(rubric.Name))
                throw new ArgumentException("Rubric name is required");

            rubric.CreatedDate = DateTime.UtcNow;
            rubric.ModifiedDate = DateTime.UtcNow;

            await _rubricRepository.AddAsync(rubric);
            await _rubricRepository.SaveChangesAsync();
            return rubric;
        }

        public async Task<Rubric> UpdateRubricAsync(Rubric rubric)
        {
            if (rubric?.Id <= 0 || string.IsNullOrWhiteSpace(rubric.Name))
                throw new ArgumentException("Invalid rubric data");

            var existing = await _rubricRepository.GetByIdAsync(rubric.Id);
            if (existing == null)
                throw new InvalidOperationException($"Rubric with ID {rubric.Id} not found");

            rubric.ModifiedDate = DateTime.UtcNow;
            await _rubricRepository.UpdateAsync(rubric);
            await _rubricRepository.SaveChangesAsync();
            return rubric;
        }

        public async Task DeleteRubricAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid rubric ID", nameof(id));

            var rubric = await _rubricRepository.GetByIdAsync(id);
            if (rubric == null)
                throw new InvalidOperationException($"Rubric with ID {id} not found");

            await _rubricRepository.DeleteAsync(rubric);
            await _rubricRepository.SaveChangesAsync();
        }
    }
}
