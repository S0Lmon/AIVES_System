using AIVES.DTO;
using System.Globalization;
using System.Speech.Synthesis;

namespace AIVES.BLL.Services.Speech;

public sealed record InstalledSpeechEngine(string Name, string Culture, bool IsFemale);

public interface ISystemSpeechCatalog
{
    IReadOnlyList<InstalledSpeechEngine> Voices(AppLanguage language);
    InstalledSpeechEngine? SelectVoice(AppLanguage language, string? name);
}

public sealed class SystemSpeechCatalog : ISystemSpeechCatalog
{
    public IReadOnlyList<InstalledSpeechEngine> Voices(AppLanguage language)
    {
        if (!OperatingSystem.IsWindows())
            return [];

        using var synthesizer = new SpeechSynthesizer();
        return synthesizer.GetInstalledVoices()
            .Where(voice => voice.Enabled && Matches(voice.VoiceInfo.Culture, language))
            .Select(voice => new InstalledSpeechEngine(
                voice.VoiceInfo.Name,
                voice.VoiceInfo.Culture.Name,
                voice.VoiceInfo.Gender == VoiceGender.Female))
            .OrderByDescending(voice => voice.IsFemale)
            .ThenBy(voice => voice.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public InstalledSpeechEngine? SelectVoice(AppLanguage language, string? name)
    {
        var voices = Voices(language);
        if (!string.IsNullOrWhiteSpace(name))
            return voices.FirstOrDefault(voice => string.Equals(voice.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        return voices.FirstOrDefault();
    }

    private static bool Matches(CultureInfo culture, AppLanguage language) =>
        culture.Name.StartsWith(language == AppLanguage.Vi ? "vi" : "en", StringComparison.OrdinalIgnoreCase);
}
