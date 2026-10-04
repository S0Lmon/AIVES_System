using AIVES.DAL.Entities;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
namespace AIVES.DAL.Data;

public sealed class IdentityAccountStore(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, RoleManager<IdentityRole> roles) : IAccountStore
{
    private static UserDto ToDto(ApplicationUser user) => new(user.Id, user.Email!, user.DisplayName, user.EmailConfirmed);
    private async Task<ApplicationUser> GetAsync(string id) => await users.FindByIdAsync(id) ?? throw new InvalidOperationException(L10n.T("The account was not found."));
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
    public async Task<UserProfileDto?> GetProfileAsync(string userId)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || user.Email is null)
            return null;

        return new UserProfileDto(user.Id, user.Email, user.DisplayName, user.EmailConfirmed, user.CreatedAtUtc, [.. await users.GetRolesAsync(user)]);
    }
    public async Task<AccountResult> CreateAsync(RegisterRequest request, bool confirmed = false)
    {
        var user = new ApplicationUser { UserName = request.Email, Email = request.Email, DisplayName = request.DisplayName, EmailConfirmed = confirmed };
        var result = confirmed ? await users.CreateAsync(user) : await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return new(false, result.Errors.Select(e => e.Description).ToList());
        // Every new account, password or Google, starts with the least privileged role.
        await EnsureRoleExistsAsync(AppRoles.Default);
        EnsureSuccess(await users.AddToRoleAsync(user, AppRoles.Default));
        return AccountResult.Success(ToDto(user));
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
            return AccountResult.Failure(L10n.T("The email or password is incorrect, or the account has not been verified."));
        var result = await signIn.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        return result.Succeeded ? AccountResult.Success(ToDto(user)) : AccountResult.Failure(result.IsLockedOut ? L10n.T("The account is temporarily locked after too many failed sign in attempts.") : L10n.T("The email or password is incorrect."));
    }
    public async Task SignInAsync(string userId)
    {
        var user = await GetAsync(userId);
        if (!await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user) || await users.GetTwoFactorEnabledAsync(user))
            throw new InvalidOperationException(L10n.T("This account is not allowed to sign in or requires additional verification."));
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
    public async Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync()
    {
        var all = users.Users.OrderBy(user => user.CreatedAtUtc).ToList();
        var summaries = new List<UserSummaryDto>(all.Count);
        foreach (var user in all)
            summaries.Add(new(user.Id, user.Email ?? string.Empty, user.DisplayName, user.EmailConfirmed, user.CreatedAtUtc, [.. await users.GetRolesAsync(user)],
                user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow.AddYears(50)));
        return summaries;
    }
    public async Task<AccountResult> SetAssignableRoleAsync(string userId, string role)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
            return AccountResult.Failure(L10n.T("The account was not found."));
        await EnsureRoleExistsAsync(role);
        var current = await users.GetRolesAsync(user);
        var stale = current.Where(name => AppRoles.Assignable.Contains(name) && name != role).ToList();
        if (stale.Count > 0)
            EnsureSuccess(await users.RemoveFromRolesAsync(user, stale));
        if (!current.Contains(role))
            EnsureSuccess(await users.AddToRoleAsync(user, role));
        // A new stamp makes the cookie validator rebuild (or reject) the user's existing sign-ins.
        EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        return AccountResult.Success(ToDto(user));
    }
    public async Task<AccountResult> SetDisabledAsync(string userId, bool disabled)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null)
            return AccountResult.Failure(L10n.T("The account was not found."));
        // A lockout ending far in the future is how Identity disables an account; sign-in checks it.
        EnsureSuccess(await users.SetLockoutEnabledAsync(user, true));
        EnsureSuccess(await users.SetLockoutEndDateAsync(user, disabled ? DateTimeOffset.MaxValue : null));
        if (!disabled)
            EnsureSuccess(await users.ResetAccessFailedCountAsync(user));
        EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        return AccountResult.Success(ToDto(user));
    }
    private async Task EnsureRoleExistsAsync(string role)
    {
        if (!await roles.RoleExistsAsync(role))
            EnsureSuccess(await roles.CreateAsync(new IdentityRole(role)));
    }
}
