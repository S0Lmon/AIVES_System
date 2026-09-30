using AIVES.DTO;
namespace AIVES.DAL.Data.Repositories;

public interface IEmailVerificationRepository
{
    Task<DateTime?> GetLatestCreatedAtAsync(string userId, CancellationToken ct);
    Task ReplaceActiveAsync(EmailVerificationCodeDto dto, CancellationToken ct);
    Task<EmailVerificationCodeDto?> GetActiveAsync(string userId, DateTime now, CancellationToken ct);
    Task<bool> TryUpdateAsync(EmailVerificationCodeDto dto, int expectedAttempts, CancellationToken ct);
}
