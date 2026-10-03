using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIVES.DAL.Data;

public static class AdminRoleSeeder
{
    /// <summary>Creates every role up front so registration and the Users page never meet a missing role.</summary>
    public static async Task EnsureRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
                continue;
            var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Could not create the {role} role: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
        }
    }

    /// <summary>Gives a seeded account the Lecturer role so it can use the question and rubric banks.</summary>
    public static async Task EnsureLecturerAsync(UserManager<ApplicationUser> userManager, ApplicationUser user)
    {
        if (await userManager.IsInRoleAsync(user, AppRoles.Lecturer))
            return;
        var result = await userManager.AddToRoleAsync(user, AppRoles.Lecturer);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not make {user.Email} a lecturer: {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }

    public static async Task SyncAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var adminEmails = configuration.GetSection(AdminAccessOptions.SectionName).Get<AdminAccessOptions>()?.Emails ?? [];
        var emails = adminEmails.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await EnsureRolesAsync(roleManager);

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