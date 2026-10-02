using System.Security.Cryptography.X509Certificates;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using AIVES.DAL.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace AIVES.DAL;

public static class DependencyInjection
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration, Action<IdentityOptions> configureIdentity)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? "Server=(localdb)\\MSSQLLocalDB;Database=AIVES;Trusted_Connection=True;TrustServerCertificate=True";
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
        services.AddIdentity<ApplicationUser, IdentityRole>(configureIdentity)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
        var dataProtection = services.AddDataProtection()
            .PersistKeysToDbContext<ApplicationDbContext>()
            .SetApplicationName("AIVES");
        // With a certificate configured, keys are encrypted before they reach the database, so a
        // leaked backup alone cannot forge auth cookies. A missing or unreadable file fails startup.
        var certificatePath = configuration["DataProtection:CertificatePath"];
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, configuration["DataProtection:CertificatePassword"]);
            // Linux has no certificate store lookup for decryption, so name the certificate explicitly too.
            dataProtection.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        }
        services.AddHealthChecks()
            .AddDbContextCheck<ApplicationDbContext>("database");

        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddScoped<IRubricRepository, RubricRepository>();
        services.AddScoped<IBloomLevelRepository, BloomLevelRepository>();
        services.AddScoped<IEmailVerificationRepository, EmailVerificationRepository>();
        services.AddScoped<IDiagnosticsRepository, DiagnosticsRepository>();
        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<ITopicRepository, TopicRepository>();
        services.AddScoped<IMaterialRepository, MaterialRepository>();
        services.AddScoped<IAccountStore, IdentityAccountStore>();
        return services;
    }
    public static async Task InitializeDataAccessAsync(this IServiceProvider services, IConfiguration configuration, bool isDevelopment)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminRoleSeeder));

        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.Migrate();
            await AdminRoleSeeder.EnsureRolesAsync(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>());
            await AdminRoleSeeder.AssignDefaultRoleToRolelessUsersAsync(dbContext, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
            if (isDevelopment)
            {
                await DevelopmentDataSeeder.SeedAsync(dbContext);

                var testEmail = configuration["Development:TestAccount:Email"];
                var testPassword = configuration["Development:TestAccount:Password"];
                var testDisplayName = configuration["Development:TestAccount:DisplayName"] ?? "AIVES Tester";

                if (!string.IsNullOrWhiteSpace(testEmail) && !string.IsNullOrWhiteSpace(testPassword))
                {
                    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                    var testUser = await userManager.FindByEmailAsync(testEmail);
                    if (testUser is null)
                    {
                        testUser = new ApplicationUser
                        {
                            UserName = testEmail,
                            Email = testEmail,
                            DisplayName = testDisplayName,
                            EmailConfirmed = true
                        };

                        var createResult = await userManager.CreateAsync(testUser, testPassword);
                        if (!createResult.Succeeded)
                        {
                            var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                            throw new InvalidOperationException($"Could not create the Development test account: {errors}");
                        }
                    }
                    await AdminRoleSeeder.EnsureLecturerAsync(userManager, testUser);
                }
            }

            if (configuration.GetValue<bool>("DemoAccount:Enabled"))
            {
                var demoEmail = configuration["DemoAccount:Email"];
                var demoPassword = configuration["DemoAccount:Password"];
                var demoDisplayName = configuration["DemoAccount:DisplayName"] ?? "AIVES Demo";

                if (string.IsNullOrWhiteSpace(demoEmail) || string.IsNullOrWhiteSpace(demoPassword))
                {
                    throw new InvalidOperationException(
                        "DemoAccount is enabled, but DemoAccount:Email or DemoAccount:Password is missing.");
                }

                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var demoUser = await userManager.FindByEmailAsync(demoEmail);
                if (demoUser is null)
                {
                    demoUser = new ApplicationUser
                    {
                        UserName = demoEmail,
                        Email = demoEmail,
                        DisplayName = demoDisplayName,
                        EmailConfirmed = true
                    };

                    var createResult = await userManager.CreateAsync(demoUser, demoPassword);
                    if (!createResult.Succeeded)
                    {
                        var errors = string.Join("; ", createResult.Errors.Select(error => error.Description));
                        throw new InvalidOperationException($"Could not create the demo account: {errors}");
                    }
                }
                else
                {
                    if (!demoUser.EmailConfirmed || demoUser.DisplayName != demoDisplayName)
                    {
                        demoUser.EmailConfirmed = true;
                        demoUser.DisplayName = demoDisplayName;
                        var updateResult = await userManager.UpdateAsync(demoUser);
                        if (!updateResult.Succeeded)
                        {
                            var errors = string.Join("; ", updateResult.Errors.Select(error => error.Description));
                            throw new InvalidOperationException($"Could not update the demo account: {errors}");
                        }
                    }

                    if (configuration.GetValue<bool>("DemoAccount:ResetPasswordOnStartup")
                        && !await userManager.CheckPasswordAsync(demoUser, demoPassword))
                    {
                        var resetToken = await userManager.GeneratePasswordResetTokenAsync(demoUser);
                        var resetResult = await userManager.ResetPasswordAsync(demoUser, resetToken, demoPassword);
                        if (!resetResult.Succeeded)
                        {
                            var errors = string.Join("; ", resetResult.Errors.Select(error => error.Description));
                            throw new InvalidOperationException($"Could not reset the demo account password: {errors}");
                        }
                    }
                }
                await AdminRoleSeeder.EnsureLecturerAsync(userManager, demoUser);
            }
        }

        await AdminRoleSeeder.SyncAsync(services, configuration, logger);
    }
}
