using AIVES.DAL.Entities;
using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

internal static class DtoMapping
{
    public static QuestionDto ToDto(Question q) => new() { Id = q.Id, Content = q.Content, Context = q.Context, BloomLevelId = q.BloomLevelId, RubricId = q.RubricId, ExpectedAnswer = q.ExpectedAnswer, DisplayOrder = q.DisplayOrder, IsActive = q.IsActive, CreatedDate = q.CreatedDate, ModifiedDate = q.ModifiedDate, BloomLevelName = q.BloomLevel?.Name, RubricName = q.Rubric?.Name };
    public static void Apply(QuestionDto q, Question e)
    {
        e.Content = q.Content;
        e.Context = q.Context ?? string.Empty;
        e.BloomLevelId = q.BloomLevelId;
        e.RubricId = q.RubricId;
        e.ExpectedAnswer = q.ExpectedAnswer ?? string.Empty;
        e.DisplayOrder = q.DisplayOrder;
        e.IsActive = q.IsActive;
        e.CreatedDate = q.CreatedDate;
        e.ModifiedDate = q.ModifiedDate;
    }
    public static RubricDto ToDto(Rubric r)
    {
        var dto = new RubricDto { Id = r.Id, Name = r.Name, Description = r.Description, TotalPoints = r.TotalPoints, CreatedDate = r.CreatedDate, ModifiedDate = r.ModifiedDate };
        foreach (var level in r.Levels)
        {
            var levelDto = new RubricLevelDto { Id = level.Id, RubricId = level.RubricId, Name = level.Name, Points = level.Points, Description = level.Description, Order = level.Order };
            foreach (var cell in level.Cells)
                levelDto.Cells.Add(new RubricCriterionLevelDto { Id = cell.Id, RubricCriterionId = cell.RubricCriterionId, RubricLevelId = cell.RubricLevelId, Descriptor = cell.Descriptor, Points = cell.Points });
            dto.Levels.Add(levelDto);
        }

        foreach (var criterion in r.Criteria)
        {
            var criterionDto = new RubricCriterionDto { Id = criterion.Id, RubricId = criterion.RubricId, Criterion = criterion.Criterion, MaxPoints = criterion.MaxPoints, Description = criterion.Description, Order = criterion.Order };
            foreach (var cell in criterion.Levels)
                criterionDto.Levels.Add(new RubricCriterionLevelDto { Id = cell.Id, RubricCriterionId = cell.RubricCriterionId, RubricLevelId = cell.RubricLevelId, Descriptor = cell.Descriptor, Points = cell.Points });
            dto.Criteria.Add(criterionDto);
        }

        dto.Levels = dto.Levels.OrderBy(level => level.Order).ToList();
        dto.Criteria = dto.Criteria.OrderBy(criterion => criterion.Order).ToList();
        return dto;
    }
    public static BloomLevelDto ToDto(BloomLevel b) => new() { Id = b.Id, Name = b.Name, Description = b.Description, Order = b.Order };
}
