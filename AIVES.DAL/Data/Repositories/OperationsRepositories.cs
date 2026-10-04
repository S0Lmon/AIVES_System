using AIVES.DAL.Entities;
using AIVES.DTO;
using Microsoft.EntityFrameworkCore;
namespace AIVES.DAL.Data.Repositories;

/// <summary>A stored recording with what is needed to decide who may play it.</summary>
public sealed record RecordingInfo(int Id, int TurnId, int CandidateId, int ExamId, string ExamOwnerId, string ContentType, long SizeBytes, string StoragePath, bool HasVideo);

public interface IRecordingRepository
{
    /// <summary>The turn's candidate and exam, when the turn belongs to <paramref name="candidateId"/>.</summary>
    Task<(int ExamId, bool HasRecording, bool Answered)?> GetTurnAsync(int candidateId, int turnId, CancellationToken cancellationToken = default);
    Task<int> AddAsync(int turnId, string contentType, long sizeBytes, bool hasVideo, string storagePath, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<RecordingInfo?> GetAsync(int recordingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecordingInfo>> ListOlderThanAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPathsForExamAsync(int examId, CancellationToken cancellationToken = default);
    Task DeleteAsync(IReadOnlyCollection<int> recordingIds, CancellationToken cancellationToken = default);
}

public sealed class RecordingRepository(ApplicationDbContext context) : IRecordingRepository
{
    public async Task<(int ExamId, bool HasRecording, bool Answered)?> GetTurnAsync(int candidateId, int turnId, CancellationToken cancellationToken = default)
    {
        var row = await context.ExamTurns.AsNoTracking()
            .Where(turn => turn.Id == turnId && turn.Attempt.ExamCandidateId == candidateId)
            .Select(turn => new { turn.Attempt.Candidate.ExamId, HasRecording = turn.Recording != null, Answered = turn.AnsweredAtUtc != null })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.ExamId, row.HasRecording, row.Answered);
    }

    public async Task<int> AddAsync(int turnId, string contentType, long sizeBytes, bool hasVideo, string storagePath, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var recording = new TurnRecording
        {
            ExamTurnId = turnId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            HasVideo = hasVideo,
            StoragePath = storagePath,
            CreatedAtUtc = nowUtc
        };
        context.TurnRecordings.Add(recording);
        await context.SaveChangesAsync(cancellationToken);
        return recording.Id;
    }

    public Task<RecordingInfo?> GetAsync(int recordingId, CancellationToken cancellationToken = default) =>
        Project(context.TurnRecordings.Where(recording => recording.Id == recordingId)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<RecordingInfo>> ListOlderThanAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default) =>
        await Project(context.TurnRecordings.Where(recording => recording.CreatedAtUtc < cutoffUtc).OrderBy(recording => recording.Id).Take(take))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListPathsForExamAsync(int examId, CancellationToken cancellationToken = default) =>
        await context.TurnRecordings.AsNoTracking()
            .Where(recording => recording.Turn.Attempt.Candidate.ExamId == examId)
            .Select(recording => recording.StoragePath)
            .ToListAsync(cancellationToken);

    public async Task DeleteAsync(IReadOnlyCollection<int> recordingIds, CancellationToken cancellationToken = default)
    {
        var rows = await context.TurnRecordings.Where(recording => recordingIds.Contains(recording.Id)).ToListAsync(cancellationToken);
        context.TurnRecordings.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<RecordingInfo> Project(IQueryable<TurnRecording> query) => query.AsNoTracking().Select(recording => new RecordingInfo(
        recording.Id,
        recording.ExamTurnId,
        recording.Turn.Attempt.ExamCandidateId,
        recording.Turn.Attempt.Candidate.ExamId,
        recording.Turn.Attempt.Candidate.Exam.CreatedById,
        recording.ContentType,
        recording.SizeBytes,
        recording.StoragePath,
        recording.HasVideo));
}

public interface IAuditRepository
{
    Task AddAsync(AuditEntryInput entry, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditRepository(ApplicationDbContext context) : IAuditRepository
{
    public async Task AddAsync(AuditEntryInput entry, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        context.AuditEntries.Add(new AuditEntry
        {
            AtUtc = nowUtc,
            Action = entry.Action,
            ActorId = entry.ActorId,
            ActorEmail = entry.ActorEmail,
            ExamId = entry.ExamId,
            CandidateId = entry.CandidateId,
            Details = entry.Details is { Length: > 4000 } ? entry.Details[..4000] : entry.Details
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var entries = context.AuditEntries.AsNoTracking();
        if (query.ExamId is { } examId)
            entries = entries.Where(entry => entry.ExamId == examId);
        if (query.CandidateId is { } candidateId)
            entries = entries.Where(entry => entry.CandidateId == candidateId);
        if (!string.IsNullOrWhiteSpace(query.Action))
            entries = entries.Where(entry => entry.Action.StartsWith(query.Action));
        if (!string.IsNullOrWhiteSpace(query.Actor))
            entries = entries.Where(entry => entry.ActorEmail != null && entry.ActorEmail.Contains(query.Actor));
        return await entries.OrderByDescending(entry => entry.Id)
            .Take(Math.Clamp(query.Take, 1, 1000))
            .Select(entry => new AuditEntryDto(entry.Id, entry.AtUtc, entry.ActorEmail, entry.Action, entry.ExamId, entry.CandidateId, entry.Details))
            .ToListAsync(cancellationToken);
    }
}

public interface ISettingsRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SetAsync(IReadOnlyDictionary<string, string> values, DateTime nowUtc, CancellationToken cancellationToken = default);
}

public sealed class SettingsRepository(ApplicationDbContext context) : ISettingsRepository
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await context.SystemSettings.AsNoTracking().ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

    public async Task SetAsync(IReadOnlyDictionary<string, string> values, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var keys = values.Keys.ToList();
        var existing = await context.SystemSettings.Where(setting => keys.Contains(setting.Key)).ToDictionaryAsync(setting => setting.Key, cancellationToken);
        foreach (var (key, value) in values)
        {
            if (!existing.TryGetValue(key, out var setting))
            {
                setting = new SystemSetting { Key = key };
                context.SystemSettings.Add(setting);
            }
            setting.Value = value;
            setting.ModifiedAtUtc = nowUtc;
        }
        await context.SaveChangesAsync(cancellationToken);
    }
}

public interface IGlossaryRepository
{
    Task<IReadOnlyList<GlossaryTermDto>> ListAsync(int subjectId, CancellationToken cancellationToken = default);
    Task<GlossaryTermDto> AddAsync(int subjectId, string term, IReadOnlyList<string> spokenForms, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class GlossaryRepository(ApplicationDbContext context) : IGlossaryRepository
{
    public async Task<IReadOnlyList<GlossaryTermDto>> ListAsync(int subjectId, CancellationToken cancellationToken = default) =>
        (await context.GlossaryTerms.AsNoTracking().Where(term => term.SubjectId == subjectId).OrderBy(term => term.Term).ToListAsync(cancellationToken))
            .Select(ToDto).ToList();

    public async Task<GlossaryTermDto> AddAsync(int subjectId, string term, IReadOnlyList<string> spokenForms, CancellationToken cancellationToken = default)
    {
        var existing = await context.GlossaryTerms.FirstOrDefaultAsync(item => item.SubjectId == subjectId && item.Term == term, cancellationToken);
        if (existing is null)
        {
            existing = new GlossaryTerm { SubjectId = subjectId, Term = term };
            context.GlossaryTerms.Add(existing);
        }
        existing.SpokenForms = string.Join(';', spokenForms);
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(existing);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var term = await context.GlossaryTerms.FindAsync([id], cancellationToken);
        if (term is null)
            return;
        context.GlossaryTerms.Remove(term);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static GlossaryTermDto ToDto(GlossaryTerm term) => new(term.Id, term.SubjectId, term.Term,
        term.SpokenForms.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

public interface ISubjectAssignmentRepository
{
    Task<IReadOnlyList<SubjectAssignmentDto>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Subjects with at least one assigned lecturer; the others stay open to every lecturer.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetRestrictionsAsync(CancellationToken cancellationToken = default);
    Task SetLecturersAsync(int subjectId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}

public sealed class SubjectAssignmentRepository(ApplicationDbContext context) : ISubjectAssignmentRepository
{
    public async Task<IReadOnlyList<SubjectAssignmentDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var subjects = await context.Subjects.AsNoTracking().OrderBy(subject => subject.Name).Select(subject => new { subject.Id, subject.Name }).ToListAsync(cancellationToken);
        var links = await context.SubjectLecturers.AsNoTracking()
            .Join(context.Users, link => link.UserId, user => user.Id, (link, user) => new { link.SubjectId, user.Id, user.Email, user.DisplayName })
            .ToListAsync(cancellationToken);
        return subjects.Select(subject => new SubjectAssignmentDto(subject.Id, subject.Name,
            links.Where(link => link.SubjectId == subject.Id)
                .Select(link => new UserRefDto(link.Id, link.Email ?? string.Empty, link.DisplayName))
                .OrderBy(user => user.Email)
                .ToList())).ToList();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetRestrictionsAsync(CancellationToken cancellationToken = default) =>
        (await context.SubjectLecturers.AsNoTracking().ToListAsync(cancellationToken))
            .GroupBy(link => link.SubjectId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<string>)group.Select(link => link.UserId).ToList());

    public async Task SetLecturersAsync(int subjectId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default)
    {
        var current = await context.SubjectLecturers.Where(link => link.SubjectId == subjectId).ToListAsync(cancellationToken);
        context.SubjectLecturers.RemoveRange(current.Where(link => !userIds.Contains(link.UserId)));
        foreach (var userId in userIds.Where(id => current.All(link => link.UserId != id)))
            context.SubjectLecturers.Add(new SubjectLecturer { SubjectId = subjectId, UserId = userId });
        await context.SaveChangesAsync(cancellationToken);
    }
}
