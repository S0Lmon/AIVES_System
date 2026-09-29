using Microsoft.AspNetCore.Identity;

namespace AIVES.WebMVC.Models.Entities;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
