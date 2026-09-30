using AIVES.DTO;

namespace AIVES.BLL.Services
{
    public interface IRubricService
    {
        Task<RubricDto?> GetRubricByIdAsync(int id);
        Task<IEnumerable<RubricDto>> GetAllRubricsAsync();
        Task<RubricDto> CreateRubricAsync(RubricDto rubric);
        Task<RubricDto> UpdateRubricAsync(RubricDto rubric);
        Task DeleteRubricAsync(int id);
    }
}
