using AIVES.BLL.Services.Interview;
using AIVES.BLL.Services.Recordings;
using AIVES.DAL.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Grading;

/// <summary>
/// Background housekeeping on every web instance: closes interviews whose slot ended while the
/// candidate was away, asks the AI to grade finished interviews, and deletes recordings past their
/// retention period. Claims in the database keep instances from grading the same interview twice.
/// </summary>
public sealed class GradingWorker(IServiceScopeFactory scopes, IOptions<GradingOptions> options, TimeProvider clock, ILogger<GradingWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(6);
    private DateTime _nextPurgeUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
            return;
        // Let the app finish starting (and migrations run) before touching the database.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.Value.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Grading worker pass failed");
            }
            await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var now = clock.GetUtcNow().UtcDateTime;

        var interviewOptions = services.GetRequiredService<IOptions<InterviewOptions>>().Value;
        var overdue = await services.GetRequiredService<IInterviewRepository>()
            .ListOverdueAsync(now.AddSeconds(-Math.Max(0, interviewOptions.AnswerGraceSeconds) - 5), 20, cancellationToken);
        var interview = services.GetRequiredService<IInterviewService>();
        foreach (var (candidateId, email) in overdue)
            // Reading the state closes an interview whose slot is over.
            await interview.GetStateAsync(candidateId, email, cancellationToken);

        var grading = services.GetRequiredService<IGradingService>();
        for (var i = 0; i < 5 && await grading.ProcessNextAsync(cancellationToken); i++)
        {
        }

        if (now >= _nextPurgeUtc)
        {
            _nextPurgeUtc = now + PurgeInterval;
            var purged = await services.GetRequiredService<IRecordingService>().PurgeExpiredAsync(cancellationToken);
            if (purged > 0)
                logger.LogInformation("Deleted {Count} recordings past their retention period", purged);
        }
    }
}
