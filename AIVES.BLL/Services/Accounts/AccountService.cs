using AIVES.DAL.Data;
using AIVES.DTO;
using AIVES.DTO.Localization;
using AIVES.BLL.Services.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace AIVES.BLL.Services.Accounts;

public sealed class AccountService(IAccountStore store, IEmailVerificationService verification,
    IAppEmailSender emailSender, IConfiguration configuration, ILogger<AccountService> logger) : IAccountService
{
    public bool IsGoogleConfigured => !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);
    public bool IsEmailConfigured => emailSender.IsConfigured;
    public IReadOnlyList<string> AllowedEmailDomains { get; } = ReadAllowedEmailDomains(configuration);
    public Task<AccountResult> LoginAsync(string email, string password, bool rememberMe) => store.PasswordSignInAsync(email.Trim(), password, rememberMe);
    public Task<UserProfileDto?> GetProfileAsync(string userId) => store.GetProfileAsync(userId);
    public async Task<AccountResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        request = request with
        {
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim()
        };
        if (!IsAllowedEmailDomain(request.Email))
            return AccountResult.Failure(L10n.Format("Please use an email address ending in {0}.",
                string.Join(", ", AllowedEmailDomains.Select(domain => "@" + domain))));
        if (!IsEmailConfigured)
            return AccountResult.Failure(L10n.T("The verification code cannot be sent right now. Please try again later or contact an administrator."));
        var result = await store.CreateAsync(request);
        if (!result.Succeeded)
            return result;
        var user = result.User!;
        try
        {
            await verification.IssueAndSendAsync(user.Id, user.Email, user.DisplayName, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send registration verification email");
            await store.DeleteAsync(user.Id);
            return AccountResult.Failure(L10n.T("The verification code could not be sent. Please try again."));
        }
    }
    public async Task<AccountResult> VerifyEmailAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var user = await store.FindByEmailAsync(email.Trim());
        if (user is null || !await verification.VerifyAsync(user.Id, code, cancellationToken))
            return AccountResult.Failure(L10n.T("The code is incorrect, expired, or you exceeded the number of attempts."));
        await store.ConfirmEmailAsync(user.Id);
        await store.SignInAsync(user.Id);
        return AccountResult.Success(user with
        {
            EmailConfirmed = true
        });
    }
    public async Task ResendCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await store.FindByEmailAsync(email.Trim());
        if (user is not null && !user.EmailConfirmed && IsEmailConfigured)
            await verification.IssueAndSendAsync(user.Id, user.Email, user.DisplayName, cancellationToken);
    }
    public AuthenticationProperties ConfigureGoogleLogin(string? redirectUrl) => store.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
    public async Task<AccountResult> CompleteGoogleLoginAsync()
    {
        var info = await store.GetExternalLoginInfoAsync();
        if (info is null)
            return AccountResult.Failure(L10n.T("Could not read the Google sign in information."));
        var signInResult = await store.ExternalLoginSignInAsync(info.Provider, info.ProviderKey);
        if (signInResult.Succeeded)
            return AccountResult.Success();
        if (signInResult.IsLockedOut || signInResult.IsNotAllowed || signInResult.RequiresTwoFactor)
            return AccountResult.Failure(L10n.T("This account is locked out, not allowed to sign in, or needs additional verification."));
        var email = info.Email;
        if (string.IsNullOrWhiteSpace(email))
            return AccountResult.Failure(L10n.T("Google did not provide an email address."));
        var user = await store.FindByEmailAsync(email);
        if (user is null)
        {
            var created = await store.CreateAsync(new(email, string.Empty, info.DisplayName ?? email.Split('@')[0]), confirmed: true);
            if (!created.Succeeded)
                return created;
            user = created.User!;
        }
        await store.ConfirmEmailAsync(user.Id);
        var linked = await store.AddLoginAsync(user.Id, info);
        if (!linked.Succeeded)
            return linked;
        await store.SignInAsync(user.Id);
        return AccountResult.Success(user);
    }
    public Task LogoutAsync() => store.SignOutAsync();

    // Exact match on the part after the last "@", so "user@notgmail.com" or
    // "user@gmail.com.example.org" never pass for "gmail.com".
    private bool IsAllowedEmailDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at > 0 && AllowedEmailDomains.Contains(email[(at + 1)..], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registration:AllowedEmailDomains as an array (appsettings, or env vars ending in __0, __1)
    /// or one comma-separated value; falls back to gmail.com when nothing is configured.
    /// </summary>
    private static IReadOnlyList<string> ReadAllowedEmailDomains(IConfiguration configuration)
    {
        var section = configuration.GetSection("Registration:AllowedEmailDomains");
        var domains = section.GetChildren().Select(child => child.Value).Append(section.Value)
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(domain => domain.TrimStart('@').ToLowerInvariant())
            .Where(domain => domain.Length > 0)
            .Distinct()
            .ToList();
        return domains.Count > 0 ? domains : ["gmail.com"];
    }
}
