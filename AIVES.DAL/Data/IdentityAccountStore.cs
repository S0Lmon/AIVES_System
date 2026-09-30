using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
namespace AIVES.DAL.Data;

public sealed class IdentityAccountStore(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : IAccountStore
{
    private static UserDto ToDto(ApplicationUser user) => new(user.Id, user.Email!, user.DisplayName, user.EmailConfirmed);
    private async Task<ApplicationUser> GetAsync(string id) => await users.FindByIdAsync(id) ?? throw new InvalidOperationException("Không tìm thấy tài khoản.");
    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
    public async Task<UserDto?> FindByEmailAsync(string email)
    {
        var user = await users.FindByEmailAsync(email);
        return user is null ? null : ToDto(user);
    }
    public async Task<AccountResult> CreateAsync(RegisterRequest request, bool confirmed = false)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName, EmailConfirmed = confirmed };
        var result = confirmed ? await users.CreateAsync(user) : await users.CreateAsync(user, request.Password);
        return result.Succeeded ? AccountResult.Success(ToDto(user)) : new(false, result.Errors.Select(e => e.Description).ToList());
    }
    public async Task DeleteAsync(string userId) => EnsureSuccess(await users.DeleteAsync(await GetAsync(userId)));
    public async Task ConfirmEmailAsync(string userId)
    {
        var user = await GetAsync(userId);
        user.EmailConfirmed = true;
        EnsureSuccess(await users.UpdateAsync(user));
    }
    public async Task<AccountResult> PasswordSignInAsync(string email, string password, bool rememberMe)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null || !user.EmailConfirmed)
            return AccountResult.Failure("Email hoặc mật khẩu không đúng, hoặc tài khoản chưa được xác minh.");
        var result = await signIn.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        return result.Succeeded ? AccountResult.Success(ToDto(user)) : AccountResult.Failure(result.IsLockedOut ? "Tài khoản tạm khóa do đăng nhập sai nhiều lần." : "Email hoặc mật khẩu không đúng.");
    }
    public async Task SignInAsync(string userId)
    {
        var user = await GetAsync(userId);
        if (!await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user) || await users.GetTwoFactorEnabledAsync(user))
            throw new InvalidOperationException("Tài khoản chưa được phép đăng nhập hoặc cần xác thực bổ sung.");
        await signIn.SignInAsync(user, isPersistent: false);
    }
    public Task SignOutAsync() => signIn.SignOutAsync();
    public AuthenticationProperties ConfigureExternalAuthenticationProperties(string provider, string? redirectUrl) => signIn.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
    public async Task<ExternalLoginDto?> GetExternalLoginInfoAsync()
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        return info is null ? null : new ExternalLoginDto(info.LoginProvider, info.ProviderKey,
            info.Principal.FindFirstValue(ClaimTypes.Email), info.Principal.FindFirstValue(ClaimTypes.Name));
    }
    public async Task<ExternalSignInResultDto> ExternalLoginSignInAsync(string provider, string key)
    {
        var result = await signIn.ExternalLoginSignInAsync(provider, key, isPersistent: false);
        return new(result.Succeeded, result.IsLockedOut, result.IsNotAllowed, result.RequiresTwoFactor);
    }
    public async Task<AccountResult> AddLoginAsync(string userId, ExternalLoginDto info)
    {
        var result = await users.AddLoginAsync(await GetAsync(userId), new UserLoginInfo(info.Provider, info.ProviderKey, info.Provider));
        return result.Succeeded ? AccountResult.Success() : new(false, result.Errors.Select(e => e.Code).ToList());
    }
}
