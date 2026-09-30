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
    public static RubricDto ToDto(Rubric r) => new() { Id = r.Id, Name = r.Name, Description = r.Description, TotalPoints = r.TotalPoints, CreatedDate = r.CreatedDate, ModifiedDate = r.ModifiedDate };
    public static BloomLevelDto ToDto(BloomLevel b) => new() { Id = b.Id, Name = b.Name, Description = b.Description, Order = b.Order };
}
