using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Speech;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace AIVES.WebRazor.Pages.Admin;

public sealed class SettingsModel(
    ISystemSettingsService settings,
    IAuditService audit,
    ISystemSpeechCatalog speechCatalog,
    IEdgeTtsProvider edgeTts,
    IVoskSpeechRecognition speechRecognition,
    ITextToSpeech voice) : PageModel
{
    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public string? Message
    {
        get; private set;
    }

    public IReadOnlyList<TtsVoiceOption> VietnameseVoices { get; private set; } = [];
    public IReadOnlyList<TtsVoiceOption> EnglishVoices { get; private set; } = [];
    public bool EdgeTtsAvailable => edgeTts.IsAvailable;
    public bool VietnameseModelAvailable { get; private set; }
    public bool EnglishModelAvailable { get; private set; }

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
        LoadInstalledSpeechEngines();
        Message = TempData["SettingsMessage"] as string;
    }

    public async Task<IActionResult> OnGetTestVoiceAsync(AppLanguage language, string? voiceName, double? speechRate,
        CancellationToken cancellationToken)
    {
        if (language is not (AppLanguage.Vi or AppLanguage.En))
            return BadRequest(new { error = L10n.T("Choose Vietnamese or English.") });
        if (speechRate is { } rate && (!double.IsFinite(rate) || rate is < 0.6 or > 1.4))
            return BadRequest(new { error = L10n.T("Speaking rate must be between 0.6 and 1.4.") });
        if (!voice.IsAvailable(language, voiceName))
        {
            var message = EdgeTtsVoiceCatalog.TryResolve(voiceName, out _) && !edgeTts.IsAvailable
                ? "Edge TTS is not installed. Run scripts/install-edge-tts.ps1 on the application server."
                : "No server TTS voice is currently available for this selection.";
            return BadRequest(new { error = L10n.T(message) });
        }
        try
        {
            var speech = await settings.GetSpeechAsync(cancellationToken);
            var audio = await voice.SynthesizeAsync(
                language == AppLanguage.Vi ? "Xin chào, tôi là giám khảo ảo. Câu hỏi thứ nhất." : "Hello, I am the virtual examiner. Question one.",
                language, speechRate ?? speech.SpeechRate, voiceName, useCache: false, cancellationToken: cancellationToken);
            Response.Headers.CacheControl = "no-store";
            return File(audio.Data, audio.ContentType);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (TimeoutException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = ex.Message });
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            LoadInstalledSpeechEngines();
            return Page();
        }

        var enabled = new List<AppLanguage>();
        if (Input.EnableVietnamese)
            enabled.Add(AppLanguage.Vi);
        if (Input.EnableEnglish)
            enabled.Add(AppLanguage.En);
        var speech = new SpeechSettingsDto(Input.DefaultLanguage, enabled, Input.SpeechRate,
            Input.VietnameseVoice, Input.EnglishVoice, Input.RecordingRetentionDays);
        try
        {
            ValidateVoiceSelection(AppLanguage.Vi, Input.VietnameseVoice);
            ValidateVoiceSelection(AppLanguage.En, Input.EnglishVoice);
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
            LoadInstalledSpeechEngines();
            return Page();
        }
    }

    private void ValidateVoiceSelection(AppLanguage language, string? voiceName)
    {
        if (EdgeTtsVoiceCatalog.TryResolve(voiceName, out var edgeVoice))
        {
            if (edgeVoice.Language != language)
                throw new ArgumentException(L10n.Format("The selected voice does not match language {0}.", language));
            return;
        }
        if (!string.IsNullOrWhiteSpace(voiceName) && speechCatalog.SelectVoice(language, voiceName) is null)
            throw new ArgumentException(L10n.Format("The selected System.Speech voice is not installed for {0}.", language));
    }

    private void LoadInstalledSpeechEngines()
    {
        VietnameseVoices = BuildVoiceOptions(AppLanguage.Vi);
        EnglishVoices = BuildVoiceOptions(AppLanguage.En);
        VietnameseModelAvailable = speechRecognition.IsAvailable(AppLanguage.Vi);
        EnglishModelAvailable = speechRecognition.IsAvailable(AppLanguage.En);
    }

    private IReadOnlyList<TtsVoiceOption> BuildVoiceOptions(AppLanguage language)
    {
        var systemVoices = speechCatalog.Voices(language).Select(voice => new TtsVoiceOption(
            voice.Name,
            L10n.Format("{0} ({1}) — System.Speech — {2}", voice.Name, voice.Culture, L10n.T(voice.IsFemale ? "Female" : "Male"))));
        var edgeVoices = EdgeTtsVoiceCatalog.ForLanguage(language).Select(voice => new TtsVoiceOption(
            EdgeTtsVoiceCatalog.Selection(voice),
            L10n.Format("{0} — Edge TTS online — {1}", voice.Name, L10n.T(voice.IsFemale ? "Female" : "Male"))));
        return systemVoices.Concat(edgeVoices).ToList();
    }
}
