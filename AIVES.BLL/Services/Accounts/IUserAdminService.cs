using AIVES.DTO;

namespace AIVES.BLL.Services.Accounts;

/// <summary>Account administration for the Users page.</summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserSummaryDto>> ListUsersAsync();

    /// <summary>
    /// Sets a user's role to Lecturer or Student. Admin is not assignable here, and an administrator
    /// cannot change their own role, so the page can never lock its last admin out.
    /// </summary>
    Task<AccountResult> SetRoleAsync(string userId, string role, string actingUserId, string? actingEmail = null);

    /// <summary>Disables or re-enables an account. Administrators and the acting user cannot be disabled here.</summary>
    Task<AccountResult> SetDisabledAsync(string userId, bool disabled, string actingUserId, string? actingEmail = null);
}
