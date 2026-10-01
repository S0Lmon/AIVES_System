using AIVES.DTO;

namespace AIVES.BLL.Services.Diagnostics;

public interface ISystemCheckService
{
    Task<SystemCheckReportDto> BuildReportAsync(CancellationToken cancellationToken = default);
}