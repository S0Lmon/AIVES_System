using AIVES.BLL;
using AIVES.WebRazor;
using AIVES.WebRazor.Realtime;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAives(builder.Configuration);
builder.Services.AddRazorPresentation(builder.Configuration);
var app = builder.Build();

// Deployments that run several web instances apply pending migrations once up front.
var migrateOnly = args.Contains("--migrate-only");
if (migrateOnly || builder.Configuration.GetValue("Database:MigrateOnStartup", true))
    await app.Services.InitializeAivesAsync(builder.Configuration, app.Environment.IsDevelopment());
if (migrateOnly)
    return;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseRequestLocalization(app.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value);
if (!builder.Configuration.GetValue<bool>("ReverseProxy:TerminatesHttps"))
    app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorPages().WithStaticAssets();
app.MapHub<AivesHub>(AivesHub.Path);
app.MapHub<InterviewHub>(InterviewHub.Path);

app.Run();

// Expose the entry point to the HTTP integration test host.
public partial class Program
{
}
