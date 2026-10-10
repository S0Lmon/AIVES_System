using AIVES.BLL.Services.Operations;
using AIVES.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class AuditModel(IAuditService audit) : PageModel
{
    public string? Action { get; private set; }
    public string? Actor { get; private set; }
    public IReadOnlyList<AuditEntryDto> Entries { get; private set; } = [];

    public async Task OnGetAsync(
        [FromQuery(Name = "action")] string? action,
        [FromQuery(Name = "actor")] string? actor,
        CancellationToken cancellationToken)
    {
        Action = action;
        Actor = actor;
        Entries = await audit.QueryAsync(new AuditQuery(Action: action, Actor: actor, Take: 500), cancellationToken);
    }
}
