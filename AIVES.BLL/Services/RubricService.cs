using AIVES.DAL.Data.Repositories;
using AIVES.DTO;

namespace AIVES.BLL.Services
{
    public class RubricService : IRubricService
    {
        private readonly IRubricRepository _rubricRepository;


        public RubricService(
            IRubricRepository rubricRepository)
        {
            _rubricRepository = rubricRepository;

        }

        public async Task<RubricDto?> GetRubricByIdAsync(int id)
        {
            if (id <= 0)
                throw new ArgumentException("Invalid rubric ID", nameof(id));

            return await _rubricRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<RubricDto>> GetAllRubricsAsync()
        {
            return await _rubricRepository.GetAllAsync();
        }

        public async Task<RubricDto> CreateRubricAsync(RubricDto rubric)
        {
            ValidateRubric(rubric);

            rubric.CreatedDate = DateTime.UtcNow;
            rubric.ModifiedDate = DateTime.UtcNow;

            await _rubricRepository.AddAsync(rubric);
            return rubric;
        }

        public async Task<RubricDto> UpdateRubricAsync(RubricDto rubric)
        {
            ValidateRubric(rubric);
            if (rubric.Id <= 0)
                throw new ArgumentException("Invalid rubric data");

            var existing = await _rubricRepository.GetByIdAsync(rubric.Id);
            if (existing == null)
                throw new InvalidOperationException($"Rubric with ID {rubric.Id} not found");

            rubric.CreatedDate = existing.CreatedDate;
            rubric.ModifiedDate = DateTime.UtcNow;
            await _rubricRepository.UpdateAsync(rubric);
            await _rubricRepository.SaveChangesAsync();
            return rubric;
        }

        private static void ValidateRubric(RubricDto rubric)
        {
            if (rubric is null || string.IsNullOrWhiteSpace(rubric.Name) || rubric.Name.Length > 200
                || rubric.Description is null || rubric.Description.Length > 1000 || rubric.TotalPoints < 0)
                throw new ArgumentException("Invalid rubric data: name, description or total points");
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
