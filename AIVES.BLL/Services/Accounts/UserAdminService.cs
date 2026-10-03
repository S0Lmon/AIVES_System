using AIVES.BLL.Services.Operations;
using AIVES.DAL.Data;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Accounts;

public sealed class UserAdminService(IAccountStore store, IAuditService audit, ILogger<UserAdminService> logger) : IUserAdminService
{
    public Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync() => store.ListUsersAsync();

    public async Task<AccountResult> SetRoleAsync(string userId, string role, string actingUserId, string? actingEmail = null)
    {
        if (!AppRoles.Assignable.Contains(role))
            return AccountResult.Failure(L10n.T("That role cannot be assigned here."));
        if (string.Equals(userId, actingUserId, StringComparison.Ordinal))
            return AccountResult.Failure(L10n.T("You cannot change your own role."));

        var result = await store.SetAssignableRoleAsync(userId, role);
        if (result.Succeeded)
        {
            logger.LogInformation("User {UserId} set the role of {TargetUserId} to {Role}", actingUserId, userId, role);
            await audit.WriteAsync(new AuditEntryInput(AuditActions.UserRoleChanged, actingUserId, actingEmail, Details: $"{result.User?.Email ?? userId} → {role}"));
        }
        return result;
    }

    public async Task<AccountResult> SetDisabledAsync(string userId, bool disabled, string actingUserId, string? actingEmail = null)
    {
        if (string.Equals(userId, actingUserId, StringComparison.Ordinal))
            return AccountResult.Failure(L10n.T("You cannot disable your own account."));
        var target = (await store.ListUsersAsync()).FirstOrDefault(user => user.Id == userId);
        if (target is null)
            return AccountResult.Failure(L10n.T("The account was not found."));
        if (disabled && target.Roles.Contains(AppRoles.Admin))
            return AccountResult.Failure(L10n.T("Administrator accounts cannot be disabled here."));

        var result = await store.SetDisabledAsync(userId, disabled);
        if (result.Succeeded)
        {
            logger.LogInformation("User {UserId} {Action} account {TargetUserId}", actingUserId, disabled ? "disabled" : "enabled", userId);
            await audit.WriteAsync(new AuditEntryInput(disabled ? AuditActions.UserLocked : AuditActions.UserUnlocked, actingUserId, actingEmail, Details: target.Email));
        }
        return result;
    }
}
