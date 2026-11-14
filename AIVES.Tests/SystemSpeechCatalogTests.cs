using AIVES.BLL.Services.Speech;

namespace AIVES.Tests;

public sealed class SystemSpeechCatalogTests
{
    [Fact]
    public void FemaleVoicesArePreferredWhenTheConfiguredVoiceIsNotExplicit()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var catalog = new SystemSpeechCatalog();
        foreach (var language in new[] { AIVES.DTO.AppLanguage.Vi, AIVES.DTO.AppLanguage.En })
        {
            var voices = catalog.Voices(language);
            if (voices.Any(voice => voice.IsFemale))
                Assert.True(catalog.SelectVoice(language, null)!.IsFemale);
        }
    }
}
