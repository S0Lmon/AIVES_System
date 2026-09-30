namespace AIVES.BLL.Services.Email;

public interface IAppEmailSender
{
    bool IsConfigured
    {
        get;
    }
    Task SendVerificationCodeAsync(string recipientEmail, string displayName, string code, CancellationToken cancellationToken = default);
}
