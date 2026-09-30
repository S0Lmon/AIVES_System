using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
namespace AIVES.DAL.Data;

public interface IAccountStore
{
    Task<UserDto?> FindByEmailAsync(string email);
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
}
