using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIVES.BLL.Services.Exams;
using AIVES.DTO;
using AIVES.WebRazor.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIVES.WebRazor.Realtime;

public sealed class ExamExpiryService(
    IServiceScopeFactory scopes,
    IHubContext<AivesHub, IAivesClient> hub,
    TimeProvider clock,
    ILogger<ExamExpiryService> logger) : BackgroundService
{
    private readonly HashSet<int> _endedBroadcasted = new();
    private bool _seeded;

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
        var exams = scope.ServiceProvider.GetRequiredService<IExamService>();
        var now = clock.GetUtcNow().UtcDateTime;
        var actor = new ExamActor(string.Empty, true, null);
        var all = await exams.ListAsync(actor, cancellationToken);

        var toRemove = _endedBroadcasted.Where(id => all.All(exam => exam.Id != id)).ToList();
        foreach (var id in toRemove)
            _endedBroadcasted.Remove(id);

        var newlyEnded = all.Where(exam => exam.EndsAtUtc < now && !_endedBroadcasted.Contains(exam.Id)).ToList();
        foreach (var exam in newlyEnded)
            _endedBroadcasted.Add(exam.Id);
        // Exams that ended before this process started were announced long ago (or nobody was online).
        if (!_seeded)
        {
            _seeded = true;
            return;
        }
        foreach (var exam in newlyEnded)
        {
            try
            {
                var details = await exams.GetAsync(exam.Id, actor, cancellationToken);
                var change = new EntityChange(LiveEntities.Exam, LiveActions.Ended, exam.Id, exam.Title, string.Empty, "System", now);
                await hub.Clients.Group(AivesHub.StaffGroup).EntityChanged(change);
                var groups = ExamLive.Emails(details).Select(AivesHub.CandidateGroup).Distinct().ToList();
                if (groups.Count > 0)
                    await hub.Clients.Groups(groups).EntityChanged(change);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not broadcast exam.ended for exam {ExamId}", exam.Id);
            }
        }
    }
}
