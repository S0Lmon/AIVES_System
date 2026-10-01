using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IDiagnosticsRepository
{
    Task<DatabaseDiagnosticsDto> GetDatabaseStatusAsync(CancellationToken cancellationToken = default);
}