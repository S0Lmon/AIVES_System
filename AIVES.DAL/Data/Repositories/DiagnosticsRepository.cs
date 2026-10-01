using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class DiagnosticsRepository(ApplicationDbContext context) : IDiagnosticsRepository
{
    public async Task<DatabaseDiagnosticsDto> GetDatabaseStatusAsync(CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        var serverName = string.IsNullOrWhiteSpace(connection.DataSource) ? "Chưa xác định" : connection.DataSource;
        var databaseName = string.IsNullOrWhiteSpace(connection.Database) ? "Chưa xác định" : connection.Database;

        try
        {
            var isReachable = await context.Database.CanConnectAsync(cancellationToken);
            if (!isReachable)
            {
                return new DatabaseDiagnosticsDto(false, serverName, databaseName, [], 0, 0, 0, 0, 0, "Không thể kết nối tới SQL Server.");
            }

            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return new DatabaseDiagnosticsDto(
                true,
                serverName,
                databaseName,
                pendingMigrations,
                await context.Users.CountAsync(cancellationToken),
                await context.Users.CountAsync(user => user.EmailConfirmed, cancellationToken),
                await context.Questions.CountAsync(cancellationToken),
                await context.Rubrics.CountAsync(cancellationToken),
                await context.BloomLevels.CountAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return new DatabaseDiagnosticsDto(false, serverName, databaseName, [], 0, 0, 0, 0, 0, ex.Message);
        }
    }
}