using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

public sealed class EmailVerificationRepository(ApplicationDbContext context) : IEmailVerificationRepository
{
    public Task<DateTime?> GetLatestCreatedAtAsync(string userId, CancellationToken ct) => context.EmailVerificationCodes.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAtUtc).Select(x => (DateTime?)x.CreatedAtUtc).FirstOrDefaultAsync(ct);
    public async Task ReplaceActiveAsync(EmailVerificationCodeDto dto, CancellationToken ct)
    {
        var active = await context.EmailVerificationCodes.Where(x => x.UserId == dto.UserId && !x.IsConsumed).ToListAsync(ct);
        foreach (var code in active)
            code.IsConsumed = true;
        context.EmailVerificationCodes.Add(new EmailVerificationCode { UserId = dto.UserId, Salt = dto.Salt, CodeHash = dto.CodeHash, ExpiresAtUtc = dto.ExpiresAtUtc, CreatedAtUtc = dto.CreatedAtUtc });
        await context.SaveChangesAsync(ct);
    }
    public async Task<EmailVerificationCodeDto?> GetActiveAsync(string userId, DateTime now, CancellationToken ct)
    {
        var x = await context.EmailVerificationCodes.AsNoTracking().Where(x => x.UserId == userId && !x.IsConsumed && x.ExpiresAtUtc > now).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        return x is null ? null : new EmailVerificationCodeDto { Id = x.Id, UserId = x.UserId, Salt = x.Salt, CodeHash = x.CodeHash, ExpiresAtUtc = x.ExpiresAtUtc, CreatedAtUtc = x.CreatedAtUtc, FailedAttempts = x.FailedAttempts, IsConsumed = x.IsConsumed };
    }
    public async Task<bool> TryUpdateAsync(EmailVerificationCodeDto dto, int expectedAttempts, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var active = context.EmailVerificationCodes
            .Where(x => x.Id == dto.Id && !x.IsConsumed && x.ExpiresAtUtc > now && x.FailedAttempts < 5);
        var updated = dto.IsConsumed
            ? await active.Where(x => x.FailedAttempts == expectedAttempts)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsConsumed, true), ct)
            : await active.ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), ct);
        return updated == 1;
    }
}
