using AIVES.DTO;

namespace AIVES.BLL.Services.Dashboard;

public interface IDashboardService
{
    Task<StudentDashboardDto> GetStudentDashboardAsync(string email, CancellationToken cancellationToken = default);
    Task<LecturerDashboardDto> GetLecturerDashboardAsync(ExamActor actor, CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
}