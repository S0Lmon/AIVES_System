namespace AIVES.DTO;

public sealed record UserDto(string Id, string Email, string DisplayName, bool EmailConfirmed);
public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record ExternalLoginDto(string Provider, string ProviderKey, string? Email, string? DisplayName);
public sealed record AccountResult(bool Succeeded, IReadOnlyList<string> Errors, UserDto? User = null)
{
    public static AccountResult Success(UserDto? user = null) => new(true, [], user);
    public static AccountResult Failure(params string[] errors) => new(false, errors);
}

public sealed record ExternalSignInResultDto(bool Succeeded, bool IsLockedOut = false, bool IsNotAllowed = false, bool RequiresTwoFactor = false);
