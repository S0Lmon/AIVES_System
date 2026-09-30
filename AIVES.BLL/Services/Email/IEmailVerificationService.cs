namespace AIVES.BLL.Services.Email;

public interface IEmailVerificationService
{
    Task IssueAndSendAsync(string userId, string email, string displayName, CancellationToken cancellationToken = default);
    Task<bool> VerifyAsync(string userId, string code, CancellationToken cancellationToken = default);
}
