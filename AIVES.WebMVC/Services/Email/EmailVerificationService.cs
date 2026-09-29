using System.Security.Cryptography;
using System.Text;
using AIVES.WebMVC.Data;
using AIVES.WebMVC.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIVES.WebMVC.Services.Email;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private const int MaxFailedAttempts = 5;
    private readonly ApplicationDbContext _dbContext;
    private readonly IAppEmailSender _emailSender;

    public EmailVerificationService(ApplicationDbContext dbContext, IAppEmailSender emailSender)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
    }

    public async Task IssueAndSendAsync(string userId, string email, string displayName, CancellationToken cancellationToken = default)
    {
        var latestCreatedAt = await _dbContext.EmailVerificationCodes
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => (DateTime?)item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestCreatedAt.HasValue && latestCreatedAt.Value > DateTime.UtcNow.AddMinutes(-1))
            throw new InvalidOperationException("Vui lòng chờ một phút trước khi yêu cầu mã mới.");

        var activeCodes = await _dbContext.EmailVerificationCodes
            .Where(item => item.UserId == userId && !item.IsConsumed)
            .ToListAsync(cancellationToken);
        foreach (var activeCode in activeCodes)
            activeCode.IsConsumed = true;

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToHexString(saltBytes);
        _dbContext.EmailVerificationCodes.Add(new EmailVerificationCode
        {
            UserId = userId,
            Salt = salt,
            CodeHash = HashCode(code, salt),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _emailSender.SendVerificationCodeAsync(email, displayName, code, cancellationToken);
    }

    public async Task<bool> VerifyAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var verification = await _dbContext.EmailVerificationCodes
            .Where(item => item.UserId == userId && !item.IsConsumed && item.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (verification is null || verification.FailedAttempts >= MaxFailedAttempts)
            return false;

        var expected = Convert.FromHexString(verification.CodeHash);
        var supplied = Convert.FromHexString(HashCode(code.Trim(), verification.Salt));
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
        {
            verification.FailedAttempts++;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        verification.IsConsumed = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string HashCode(string code, string salt)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));
    }
}
