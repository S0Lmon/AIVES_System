using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Speech;

/// <summary>
/// With VieNeu switched on, reads the main questions of candidates whose slot is near (or open) into
/// the voice cache, one at a time, so a slow voice model does not eat into answer time. Follow-up
/// questions are written during the viva and are read when they are asked.
/// </summary>
public sealed class QuestionAudioWorker(IServiceScopeFactory scopes, HybridTextToSpeech voice, VieNeuSpeechClient vieneu,
    IOptions<SpeechOptions> options, TimeProvider clock, ILogger<QuestionAudioWorker> logger) : BackgroundService
{
    private const int MaxQuestionsPerPass = 200;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting (and migrations run) before touching the database.
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (vieneu.IsEnabled)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Preparing question audio failed");
                }
            }
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <returns>How many questions the remote voice has ready.</returns>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<(string Text, string Language)> questions;
        using (var scope = scopes.CreateScope())
            questions = await scope.ServiceProvider.GetRequiredService<IInterviewRepository>()
                .ListUpcomingQuestionsAsync(now, now.AddMinutes(Math.Max(1, options.Value.PrepareMinutesAhead)), MaxQuestionsPerPass, cancellationToken);

        var ready = 0;
        foreach (var (text, language) in questions.DistinctBy(question => (question.Text.Trim(), question.Language)))
            if (await voice.PrepareAsync(text, language.ToLanguage(), cancellationToken))
                ready++;
        return ready;
    }
}
