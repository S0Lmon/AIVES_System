using AIVES.BLL.Services.Operations;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class SettingsModel(ISystemSettingsService settings, IAuditService audit) : PageModel
{
    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public string? Message
    {
        get; private set;
    }

    private string? ActingEmail => User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

    public sealed class SettingsInput
    {
        public AppLanguage DefaultLanguage
        {
            get; set;
        }
        public bool EnableVietnamese
        {
            get; set;
        }
        public bool EnableEnglish
        {
            get; set;
        }

        [Range(0.6, 1.4)]
        public double SpeechRate
        {
            get; set;
        }

        public string? VietnameseVoice
        {
            get; set;
        }
        public string? EnglishVoice
        {
            get; set;
        }

        [Range(7, 3650)]
        public int RecordingRetentionDays
        {
            get; set;
        }

        public string? Error
        {
            get; set;
        }
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var speech = await settings.GetSpeechAsync(cancellationToken);
        Input = new SettingsInput
        {
            DefaultLanguage = speech.DefaultLanguage,
            EnableVietnamese = speech.EnabledLanguages.Contains(AppLanguage.Vi),
            EnableEnglish = speech.EnabledLanguages.Contains(AppLanguage.En),
            SpeechRate = speech.SpeechRate,
            VietnameseVoice = speech.VietnameseVoice,
            EnglishVoice = speech.EnglishVoice,
            RecordingRetentionDays = speech.RecordingRetentionDays
        };
        Message = TempData["SettingsMessage"] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var enabled = new List<AppLanguage>();
        if (Input.EnableVietnamese)
            enabled.Add(AppLanguage.Vi);
        if (Input.EnableEnglish)
            enabled.Add(AppLanguage.En);
        var speech = new SpeechSettingsDto(Input.DefaultLanguage, enabled, Input.SpeechRate,
            Input.VietnameseVoice, Input.EnglishVoice, Input.RecordingRetentionDays);
        try
        {
            await settings.SaveSpeechAsync(speech, cancellationToken);
            await audit.WriteAsync(new AuditEntryInput(AuditActions.SettingsChanged,
                User.FindFirstValue(ClaimTypes.NameIdentifier), ActingEmail,
                Details: $"default {speech.DefaultLanguage}, enabled {string.Join("/", enabled)}, rate {speech.SpeechRate:0.00}, voices '{speech.VietnameseVoice}'/'{speech.EnglishVoice}', retention {speech.RecordingRetentionDays} days"),
                cancellationToken);
            TempData["SettingsMessage"] = L10n.T("The settings were saved.");
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            Input.Error = ex.Message;
            return Page();
        }
    }
}
