using System.Security.Cryptography;
using System.Text;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;

namespace AIVES.BLL.Services.Email;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private const int MaxFailedAttempts = 5;
    private readonly IEmailVerificationRepository _repository;
    private readonly IAppEmailSender _emailSender;

    public EmailVerificationService(IEmailVerificationRepository repository, IAppEmailSender emailSender)
    {
        _repository = repository;
        _emailSender = emailSender;
    }

    public async Task IssueAndSendAsync(string userId, string email, string displayName, CancellationToken cancellationToken = default)
    {
        var latestCreatedAt = await _repository.GetLatestCreatedAtAsync(userId, cancellationToken);
        if (latestCreatedAt.HasValue && latestCreatedAt.Value > DateTime.UtcNow.AddMinutes(-1))
            throw new InvalidOperationException(L10n.T("Please wait a minute before requesting a new code."));


        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToHexString(saltBytes);
        await _repository.ReplaceActiveAsync(new EmailVerificationCodeDto
        {
            UserId = userId,
            Salt = salt,
            CodeHash = HashCode(code, salt),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        }, cancellationToken);
        await _emailSender.SendVerificationCodeAsync(email, displayName, code, cancellationToken);
    }

    public async Task<bool> VerifyAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var verification = await _repository.GetActiveAsync(userId, DateTime.UtcNow, cancellationToken);

        if (verification is null || verification.FailedAttempts >= MaxFailedAttempts)
            return false;

        var previousAttempts = verification.FailedAttempts;
        var expected = Convert.FromHexString(verification.CodeHash);
        var supplied = Convert.FromHexString(HashCode(code.Trim(), verification.Salt));
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
        {
            verification.FailedAttempts++;
            await _repository.TryUpdateAsync(verification, previousAttempts, cancellationToken);
            return false;
        }

        verification.IsConsumed = true;
        return await _repository.TryUpdateAsync(verification, previousAttempts, cancellationToken);
    }

    private static string HashCode(string code, string salt)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));
    }
}
