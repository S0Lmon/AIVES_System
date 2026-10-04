using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
namespace AIVES.DAL.Data;

public interface IAccountStore
{
    Task<UserDto?> FindByEmailAsync(string email);
    Task<UserProfileDto?> GetProfileAsync(string userId);
    Task<AccountResult> CreateAsync(RegisterRequest request, bool confirmed = false);
    Task DeleteAsync(string userId);
    Task ConfirmEmailAsync(string userId);
    Task<AccountResult> PasswordSignInAsync(string email, string password, bool rememberMe);
    Task SignInAsync(string userId);
    Task SignOutAsync();
    AuthenticationProperties ConfigureExternalAuthenticationProperties(string provider, string? redirectUrl);
    Task<ExternalLoginDto?> GetExternalLoginInfoAsync();
    Task<ExternalSignInResultDto> ExternalLoginSignInAsync(string provider, string key);
    Task<AccountResult> AddLoginAsync(string userId, ExternalLoginDto info);
    Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync();
    /// <summary>Makes <paramref name="role"/> the user's only assignable role and invalidates their sign-in cookie.</summary>
    Task<AccountResult> SetAssignableRoleAsync(string userId, string role);
    /// <summary>Disables (or re-enables) sign-in for an account and ends its existing sessions.</summary>
    Task<AccountResult> SetDisabledAsync(string userId, bool disabled);
}
