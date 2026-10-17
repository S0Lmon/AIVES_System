using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIVES.BLL.Services.Realtime;

public sealed class ExamExpiryService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<ExamExpiryService> logger) : BackgroundService
{
    private readonly HashSet<int> _endedBroadcasted = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MarkEndedExams(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Exam expiry check failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task MarkEndedExams(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var exams = services.GetRequiredService<IExamService>();
        var liveUpdates = services.GetRequiredService<ILiveUpdates>();
        var now = clock.GetUtcNow().UtcDateTime;
        var actor = new ExamActor(string.Empty, true, null);
        var all = await exams.ListAsync(actor, cancellationToken);

        var toRemove = _endedBroadcasted.Where(id => all.All(exam => exam.Id != id)).ToList();
        foreach (var id in toRemove)
            _endedBroadcasted.Remove(id);

        foreach (var exam in all.Where(exam => exam.EndsAtUtc < now && !_endedBroadcasted.Contains(exam.Id)))
        {
            _endedBroadcasted.Add(exam.Id);
            await liveUpdates.EntityChangedAsync(LiveEntities.Exam, LiveActions.Ended, exam.Id, exam.Title, string.Empty, "System");
        }
    }
}
