using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIVES.DAL.Data;

public static class AdminRoleSeeder
{
    public static async Task SyncAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var adminEmails = configuration.GetSection(AdminAccessOptions.SectionName).Get<AdminAccessOptions>()?.Emails ?? [];
        var emails = adminEmails.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (emails.Count == 0)
            return;

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(AdminRoles.RoleName))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole(AdminRoles.RoleName));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Could not create the admin role: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
        }

        foreach (var email in emails)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("Admin access is configured for {Email} but no account exists for that address", email);
                continue;
            }

            if (!await userManager.IsInRoleAsync(user, AdminRoles.RoleName))
            {
                var assignResult = await userManager.AddToRoleAsync(user, AdminRoles.RoleName);
                if (!assignResult.Succeeded)
                    throw new InvalidOperationException($"Could not grant admin access to {email}: {string.Join("; ", assignResult.Errors.Select(error => error.Description))}");
            }
        }
    }
}