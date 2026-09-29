using AIVES.WebMVC.Models.Entities;

namespace AIVES.WebMVC.Services
{
    public interface IRubricService
    {
        Task<Rubric> GetRubricByIdAsync(int id);
        Task<IEnumerable<Rubric>> GetAllRubricsAsync();
        Task<Rubric> CreateRubricAsync(Rubric rubric);
        Task<Rubric> UpdateRubricAsync(Rubric rubric);
        Task DeleteRubricAsync(int id);
    }
}
