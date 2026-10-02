using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
namespace AIVES.BLL.Services.Accounts;

public interface IAccountService
{
    bool IsGoogleConfigured
    {
        get;
    }
    bool IsEmailConfigured
    {
        get;
    }
    /// <summary>Email domains (without "@") that may register with a password.</summary>
    IReadOnlyList<string> AllowedEmailDomains
    {
        get;
    }
    Task<AccountResult> LoginAsync(string email, string password, bool rememberMe);
    Task<UserProfileDto?> GetProfileAsync(string userId);
    Task<AccountResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<AccountResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default);
    Task ResendCodeAsync(string email, CancellationToken cancellationToken = default);
    AuthenticationProperties ConfigureGoogleLogin(string? redirectUrl);
    Task<AccountResult> CompleteGoogleLoginAsync();
    Task LogoutAsync();
}
