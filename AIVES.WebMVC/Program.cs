using AIVES.WebMVC.Data;
using AIVES.WebMVC.Data.Repositories;
using AIVES.WebMVC.Models.Entities;
using AIVES.WebMVC.Services;
using AIVES.WebMVC.Services.Gemini;
using AIVES.WebMVC.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=AIVES;Trusted_Connection=True;TrustServerCertificate=True";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    options.User.RequireUniqueEmail = true;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    options.Cookie.Name = "AIVES.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });
}
builder.Services.AddControllersWithViews();
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IQuestionRepository, QuestionRepository>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IRubricService, RubricService>();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<IGeminiQuestionGenerator, GeminiQuestionGenerator>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.Configure<GmailSmtpOptions>(builder.Configuration.GetSection(GmailSmtpOptions.SectionName));
builder.Services.AddScoped<IAppEmailSender, GmailSmtpEmailSender>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
    configure.AddDebug();
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
    if (app.Environment.IsDevelopment())
    {
        await DevelopmentDataSeeder.SeedAsync(dbContext);

        var testEmail = builder.Configuration["Development:TestAccount:Email"];
        var testPassword = builder.Configuration["Development:TestAccount:Password"];
        var testDisplayName = builder.Configuration["Development:TestAccount:DisplayName"] ?? "AIVES Tester";

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
        }
    }

    if (builder.Configuration.GetValue<bool>("DemoAccount:Enabled"))
    {
        var demoEmail = builder.Configuration["DemoAccount:Email"];
        var demoPassword = builder.Configuration["DemoAccount:Password"];
        var demoDisplayName = builder.Configuration["DemoAccount:DisplayName"] ?? "AIVES Demo";

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

            if (builder.Configuration.GetValue<bool>("DemoAccount:ResetPasswordOnStartup")
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
    }
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
