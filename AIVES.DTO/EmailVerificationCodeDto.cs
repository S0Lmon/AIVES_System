namespace AIVES.DTO;

public sealed class EmailVerificationCodeDto
{
    public long Id
    {
        get; set;
    }
    public string UserId { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc
    {
        get; set;
    }
    public int FailedAttempts
    {
        get; set;
    }
    public bool IsConsumed
    {
        get; set;
    }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
