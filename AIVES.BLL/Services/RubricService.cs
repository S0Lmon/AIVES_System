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
            NormaliseAndValidate(rubric);

            rubric.CreatedDate = DateTime.UtcNow;
            rubric.ModifiedDate = DateTime.UtcNow;

            await _rubricRepository.AddAsync(rubric);
            return rubric;
        }

        public async Task<RubricDto> UpdateRubricAsync(RubricDto rubric)
        {
            NormaliseAndValidate(rubric);
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

        /// <summary>
        /// Trims the incoming graph, drops blank rows and columns, keeps point totals consistent
        /// and refuses anything that is not a usable matrix. A rubric is a grid, so it needs at
        /// least one row and one column, and every cell must name the level it belongs to.
        /// </summary>
        private static void NormaliseAndValidate(RubricDto rubric)
        {
            if (rubric is null)
                throw new ArgumentException("Invalid rubric data: name, description or total points");

            if (string.IsNullOrWhiteSpace(rubric.Name) || rubric.Name.Trim().Length > 200)
                throw new ArgumentException("Invalid rubric data: name, description or total points");
            if (rubric.Description is null || rubric.Description.Length > 1000)
                throw new ArgumentException("Invalid rubric data: name, description or total points");

            rubric.Name = rubric.Name.Trim();
            rubric.Description = rubric.Description.Trim();

            rubric.Criteria ??= [];
            rubric.Levels ??= [];

            var levels = new List<RubricLevelDto>();
            var order = 0;
            foreach (var level in rubric.Levels)
            {
                if (level is null || string.IsNullOrWhiteSpace(level.Name))
                    continue;
                level.Name = level.Name.Trim();
                level.Description = (level.Description ?? string.Empty).Trim();
                if (level.Description.Length > 1000)
                    level.Description = level.Description[..1000];
                if (level.Points < 0)
                    throw new ArgumentException("Invalid rubric data: level points cannot be negative");
                level.Order = order++;
                levels.Add(level);
            }

            var levelKeys = levels.Select(level => level.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (levelKeys.Count != levels.Count)
                throw new ArgumentException("Invalid rubric data: level names must be unique");

            rubric.Levels = levels;

            var criteria = new List<RubricCriterionDto>();
            order = 0;
            foreach (var criterion in rubric.Criteria)
            {
                if (criterion is null || string.IsNullOrWhiteSpace(criterion.Criterion))
                    continue;
                criterion.Criterion = criterion.Criterion.Trim();
                criterion.Description = (criterion.Description ?? string.Empty).Trim();
                criterion.Order = order++;
                criteria.Add(criterion);
            }

            rubric.Criteria = criteria;

            if (rubric.Criteria.Count == 0)
                throw new ArgumentException("Invalid rubric data: add at least one criterion row");
            if (rubric.Levels.Count == 0)
                throw new ArgumentException("Invalid rubric data: add at least one level column");

            // A stored rubric knows its columns by identifier; a new one only has names, so a cell
            // is accepted when it names a column even before that column has an identifier.
            var columnsById = rubric.Levels.Where(level => level.Id > 0).ToDictionary(level => level.Id);
            var columnNames = rubric.Levels.Select(level => level.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var criterion in rubric.Criteria)
            {
                criterion.Levels ??= [];
                var kept = new List<RubricCriterionLevelDto>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in criterion.Levels)
                {
                    if (cell is null)
                        continue;

                    cell.LevelName = cell.LevelName?.Trim() ?? string.Empty;
                    var bound = cell.RubricLevelId > 0 && columnsById.ContainsKey(cell.RubricLevelId);
                    if (!bound && cell.LevelName.Length > 0 && columnNames.Contains(cell.LevelName))
                    {
                        bound = true;
                        cell.RubricLevelId = rubric.Levels
                            .First(level => string.Equals(level.Name, cell.LevelName, StringComparison.OrdinalIgnoreCase)).Id;
                    }

                    if (!bound)
                        throw new ArgumentException("Invalid rubric data: every cell must match a level column");

                    // A new column has no identifier yet, so fall back to its name to spot repeats.
                    var key = cell.RubricLevelId > 0 ? $"id:{cell.RubricLevelId}" : $"name:{cell.LevelName}";
                    if (!seen.Add(key))
                        throw new ArgumentException("Invalid rubric data: a criterion cannot repeat a level column");

                    cell.Descriptor = (cell.Descriptor ?? string.Empty).Trim();
                    if (cell.Descriptor.Length > 1000)
                        cell.Descriptor = cell.Descriptor[..1000];
                    if (cell.Points < 0)
                        throw new ArgumentException("Invalid rubric data: cell points cannot be negative");
                    kept.Add(cell);
                }

                criterion.Levels = kept;
                criterion.MaxPoints = kept.Count == 0 ? 0 : kept.Max(cell => cell.Points);
            }

            rubric.TotalPoints = rubric.Criteria.Sum(criterion => criterion.MaxPoints);
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