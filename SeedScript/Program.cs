using AIVES.DAL.Data;
using AIVES.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SeedScript;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            // Connection string from appsettings.Development.json or environment
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer("Server=localhost;Database=AIVES;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"));
            services.AddLogging();

            var serviceProvider = services.BuildServiceProvider();
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting seeding...");
            await DevelopmentDataSeeder.SeedAsync(dbContext);
            logger.LogInformation("Seeding completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
