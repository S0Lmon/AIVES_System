using AIVES.BLL.Services.Diagnostics;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class IndexModel(ISystemCheckService systemChecks, ILogger<IndexModel> logger) : PageModel
{
    public SystemCheckReportDto Report { get; private set; } =
        new("Unknown", DateTimeOffset.UtcNow,
            new DatabaseDiagnosticsDto(false, string.Empty, string.Empty, [], 0, 0, 0, 0, 0), []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            Report = await systemChecks.BuildReportAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while building the system check report");
        }
    }
}
