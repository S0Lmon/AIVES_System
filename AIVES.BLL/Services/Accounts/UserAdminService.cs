using AIVES.DAL.Data;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Accounts;

public sealed class UserAdminService(IAccountStore store, ILogger<UserAdminService> logger) : IUserAdminService
{
    public Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync() => store.ListUsersAsync();

    public async Task<AccountResult> SetRoleAsync(string userId, string role, string actingUserId)
    {
        if (!AppRoles.Assignable.Contains(role))
            return AccountResult.Failure(L10n.T("That role cannot be assigned here."));
        if (string.Equals(userId, actingUserId, StringComparison.Ordinal))
            return AccountResult.Failure(L10n.T("You cannot change your own role."));

        var result = await store.SetAssignableRoleAsync(userId, role);
        if (result.Succeeded)
            logger.LogInformation("User {UserId} set the role of {TargetUserId} to {Role}", actingUserId, userId, role);
        return result;
    }
}
