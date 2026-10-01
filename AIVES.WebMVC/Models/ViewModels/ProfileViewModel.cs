namespace AIVES.WebMVC.Models.ViewModels;

public sealed class ProfileViewModel
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public DateTime MemberSince { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}