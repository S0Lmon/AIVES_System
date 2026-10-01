namespace AIVES.DAL.Data;

public sealed class AdminAccessOptions
{
    public const string SectionName = "AdminAccess";

    public string[] Emails { get; set; } = [];
}