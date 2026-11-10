using AIVES.BLL.Services.Operations;
using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIVES.BLL.Services.Recordings;

public sealed class RecordingOptions
{
    public const string SectionName = "Recordings";

    /// <summary>
    /// Folder for the encrypted files. Every web instance must see the same folder (a shared volume in
    /// compose.yaml). Relative paths are resolved against the application's content root.
    /// </summary>
    public string Path { get; set; } = "App_Data/recordings";

    /// <summary>Largest accepted upload for one answer, and for one piece of a whole-viva recording.</summary>
    public long MaxBytes { get; set; } = 30 * 1024 * 1024;

    /// <summary>Largest total size of one candidate's whole-viva recording.</summary>
    public long MaxSessionBytes { get; set; } = 400L * 1024 * 1024;

    /// <summary>Most pieces one whole-viva recording may have (the browser sends one every 15 seconds).</summary>
    public int MaxSessionParts { get; set; } = 1000;

    /// <summary>How long after the slot ends the last pieces of a whole-viva recording are still accepted.</summary>
    public int SessionUploadGraceMinutes { get; set; } = 15;
}

/// <summary>A decrypted recording ready to stream to an authorised viewer.</summary>
public sealed record RecordingContent(byte[] Data, string ContentType, bool HasVideo);

/// <summary>
/// One continuous stretch of a whole-viva recording. A viva normally has one; reloading the page
/// during the viva starts another.
/// </summary>
public sealed record SessionSegmentDto(long Segment, DateTime FirstPartAtUtc, int Parts, long SizeBytes, bool HasVideo);

/// <summary>
/// Evidence recordings: each spoken answer on its own, and the whole viva as one continuous
/// recording (examiner's voice, candidate's microphone and camera when enabled). Files are encrypted with the application's Data
/// Protection keys (shared across instances and themselves encrypted at rest), so a copied volume
/// or backup alone does not expose students' voices. Only the exam's lecturer or an administrator
/// may play a recording, and every playback is written to the audit log. Recordings older than the
/// retention period are deleted by <see cref="PurgeExpiredAsync"/>.
/// </summary>
public interface IRecordingService
{
    Task SaveAsync(int candidateId, string email, int turnId, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<RecordingContent?> OpenAsync(int recordingId, ExamActor actor, CancellationToken cancellationToken = default);

    /// <summary>Stores one piece of the whole-viva recording; a piece uploaded twice is stored once.</summary>
    Task SaveSessionPartAsync(int candidateId, string email, long segment, int partIndex, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>The whole-viva recording's segments, empty when there is none or <paramref name="actor"/> may not play it.</summary>
    Task<IReadOnlyList<SessionSegmentDto>> ListSessionAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// One segment of the whole-viva recording as a single playable file. Logged like any playback; pass
    /// <paramref name="logPlayback"/> false for the follow-up range requests a player makes while seeking.
    /// </summary>
    Task<RecordingContent?> OpenSessionAsync(int candidateId, long segment, ExamActor actor, bool logPlayback = true, CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
    Task DeleteForExamAsync(int examId, CancellationToken cancellationToken = default);
}

public sealed class RecordingService(
    IRecordingRepository recordings,
    IInterviewRepository interviews,
    IRecordingStore store,
    IDataProtectionProvider protection,
    ISystemSettingsService settings,
    IAuditService audit,
    IOptions<RecordingOptions> options,
    TimeProvider clock,
    ILogger<RecordingService> logger) : IRecordingService
{
    private static readonly string[] AllowedTypes = ["audio/webm", "audio/ogg", "audio/mp4", "audio/mpeg", "audio/wav", "video/webm", "video/mp4"];
    private IDataProtector Protector => protection.CreateProtector("AIVES.Recordings.v1");

    public async Task SaveAsync(int candidateId, string email, int turnId, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var context = await interviews.GetContextAsync(candidateId, cancellationToken);
        if (context is null || string.IsNullOrWhiteSpace(email) || !string.Equals(context.CandidateEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException(L10n.T("This exam slot was not found."));
        if (context.Recording == RecordingMode.None || context.RecordingConsentAtUtc is null)
            throw new InvalidOperationException(L10n.T("This viva is not being recorded."));

        var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(mediaType))
            throw new ArgumentException(L10n.T("Unsupported recording format."));
        var hasVideo = mediaType.StartsWith("video/", StringComparison.Ordinal);
        if (hasVideo && context.Recording != RecordingMode.AudioVideo)
            throw new ArgumentException(L10n.T("Unsupported recording format."));

        var turn = await recordings.GetTurnAsync(candidateId, turnId, cancellationToken)
            ?? throw new KeyNotFoundException(L10n.T("This exam slot was not found."));
        if (!turn.Answered)
            throw new InvalidOperationException(L10n.T("Submit the answer before uploading its recording."));
        // One recording per answer, uploaded once the answer has been submitted.
        if (turn.HasRecording)
            return;

        using var buffer = new MemoryStream();
        await CopyLimitedAsync(content, buffer, options.Value.MaxBytes, cancellationToken);
        if (buffer.Length == 0)
            throw new ArgumentException(L10n.T("The recording is empty."));

        var relativePath = $"{turn.ExamId}/{candidateId}/{turnId}-{Guid.NewGuid():N}.bin";
        await store.WriteAsync(relativePath, Protector.Protect(buffer.ToArray()), cancellationToken);
        try
        {
            await recordings.AddAsync(turnId, mediaType, buffer.Length, hasVideo, relativePath, clock.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch
        {
            // A concurrent upload for the same turn won the unique index; do not leave an orphan file.
            store.Delete(relativePath);
            throw;
        }
        logger.LogInformation("Stored a {Size} byte recording for turn {TurnId}", buffer.Length, turnId);
    }

    public async Task<RecordingContent?> OpenAsync(int recordingId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var recording = await recordings.GetAsync(recordingId, cancellationToken);
        if (recording is null || !(actor.IsAdmin || recording.ExamOwnerId == actor.UserId))
            return null;
        var encrypted = await store.ReadAsync(recording.StoragePath, cancellationToken);
        if (encrypted is null)
            return null;
        await audit.WriteAsync(new AuditEntryInput(AuditActions.RecordingViewed, actor.UserId, actor.Email, recording.ExamId, recording.CandidateId,
            $"Recording {recording.Id} of turn {recording.TurnId}"), cancellationToken);
        return new RecordingContent(Protector.Unprotect(encrypted), recording.ContentType, recording.HasVideo);
    }

    public async Task SaveSessionPartAsync(int candidateId, string email, long segment, int partIndex, Stream content, string contentType,
        CancellationToken cancellationToken = default)
    {
        var context = await interviews.GetContextAsync(candidateId, cancellationToken);
        if (context is null || string.IsNullOrWhiteSpace(email) || !string.Equals(context.CandidateEmail, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new KeyNotFoundException(L10n.T("This exam slot was not found."));
        if (context.Recording == RecordingMode.None || context.RecordingConsentAtUtc is null || context.AttemptId is not { } attemptId)
            throw new InvalidOperationException(L10n.T("This viva is not being recorded."));
        // The final piece arrives just after the viva ends; nothing is accepted long after the slot.
        if (clock.GetUtcNow().UtcDateTime > context.SlotEndsAtUtc.AddMinutes(Math.Max(0, options.Value.SessionUploadGraceMinutes)))
            throw new InvalidOperationException(L10n.T("This viva is not being recorded."));
        if (segment <= 0 || partIndex < 0 || partIndex >= options.Value.MaxSessionParts)
            throw new ArgumentException(L10n.T("Unsupported recording format."));
        var (mediaType, hasVideo) = CheckType(contentType, context.Recording);

        if (await recordings.HasSessionPartAsync(attemptId, segment, partIndex, cancellationToken))
            return;
        var (count, bytes) = await recordings.GetSessionUsageAsync(attemptId, cancellationToken);
        if (count >= options.Value.MaxSessionParts)
            throw new ArgumentException(L10n.T("The recording is too large."));

        using var buffer = new MemoryStream();
        await CopyLimitedAsync(content, buffer, Math.Min(options.Value.MaxBytes, Math.Max(0, options.Value.MaxSessionBytes - bytes)), cancellationToken);
        if (buffer.Length == 0)
            throw new ArgumentException(L10n.T("The recording is empty."));

        var relativePath = $"{context.ExamId}/{candidateId}/session-{segment}-{partIndex:D5}-{Guid.NewGuid():N}.bin";
        await store.WriteAsync(relativePath, Protector.Protect(buffer.ToArray()), cancellationToken);
        bool added;
        try
        {
            added = await recordings.AddSessionPartAsync(attemptId, segment, partIndex, mediaType, buffer.Length, hasVideo, relativePath,
                clock.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch
        {
            store.Delete(relativePath);
            throw;
        }
        if (!added)
        {
            store.Delete(relativePath);
            return;
        }
        if (partIndex == 0)
            await audit.WriteAsync(new AuditEntryInput(AuditActions.RecordingStored, null, context.CandidateEmail, context.ExamId, candidateId,
                $"Whole-viva recording started (segment {segment}, {(hasVideo ? "audio and video" : "audio")})"), cancellationToken);
        logger.LogInformation("Stored whole-viva piece {Segment}/{Part} ({Size} bytes) for candidate slot {CandidateId}", segment, partIndex, buffer.Length, candidateId);
    }

    public async Task<IReadOnlyList<SessionSegmentDto>> ListSessionAsync(int candidateId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        var session = await recordings.GetSessionAsync(candidateId, cancellationToken);
        if (session is null || !(actor.IsAdmin || session.ExamOwnerId == actor.UserId))
            return [];
        return session.Parts
            .GroupBy(part => part.Segment)
            .OrderBy(group => group.Key)
            .Select(group => new SessionSegmentDto(group.Key, group.Min(part => part.CreatedAtUtc), group.Count(), group.Sum(part => part.SizeBytes),
                group.Any(part => part.HasVideo)))
            .ToList();
    }

    public async Task<RecordingContent?> OpenSessionAsync(int candidateId, long segment, ExamActor actor, bool logPlayback = true, CancellationToken cancellationToken = default)
    {
        var session = await recordings.GetSessionAsync(candidateId, cancellationToken);
        if (session is null || !(actor.IsAdmin || session.ExamOwnerId == actor.UserId))
            return null;
        var parts = session.Parts.Where(part => part.Segment == segment).OrderBy(part => part.PartIndex).ToList();
        if (parts.Count == 0)
            return null;
        // Pieces from one MediaRecorder are consecutive chunks of one stream: joined in order they play as one file.
        using var joined = new MemoryStream();
        foreach (var part in parts)
        {
            var encrypted = await store.ReadAsync(part.StoragePath, cancellationToken);
            if (encrypted is null)
                continue;
            var data = Protector.Unprotect(encrypted);
            await joined.WriteAsync(data, cancellationToken);
        }
        if (joined.Length == 0)
            return null;
        var missing = parts[^1].PartIndex + 1 - parts.Count;
        if (logPlayback)
            await audit.WriteAsync(new AuditEntryInput(AuditActions.RecordingViewed, actor.UserId, actor.Email, session.ExamId, candidateId,
            $"Whole-viva recording, segment {segment} ({parts.Count} piece(s){(missing > 0 ? $", {missing} missing" : string.Empty)})"), cancellationToken);
        return new RecordingContent(joined.ToArray(), parts[0].ContentType, parts.Any(part => part.HasVideo));
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var retention = (await settings.GetSpeechAsync(cancellationToken)).RecordingRetentionDays;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retention);
        var purged = await PurgeSessionsAsync(cutoff, retention, cancellationToken);
        while (true)
        {
            var batch = await recordings.ListOlderThanAsync(cutoff, 200, cancellationToken);
            if (batch.Count == 0)
                break;
            foreach (var recording in batch)
                store.Delete(recording.StoragePath);
            await recordings.DeleteAsync(batch.Select(recording => recording.Id).ToList(), cancellationToken);
            foreach (var exam in batch.GroupBy(recording => recording.ExamId))
                await audit.WriteAsync(new AuditEntryInput(AuditActions.RecordingPurged, null, "system", exam.Key, null,
                    $"{exam.Count()} recording(s) older than {retention} days"), cancellationToken);
            purged += batch.Count;
        }
        return purged;
    }

    private async Task<int> PurgeSessionsAsync(DateTime cutoff, int retention, CancellationToken cancellationToken)
    {
        var purged = 0;
        while (true)
        {
            var batch = await recordings.ListSessionPartsOlderThanAsync(cutoff, 200, cancellationToken);
            if (batch.Count == 0)
                break;
            foreach (var part in batch)
                store.Delete(part.StoragePath);
            await recordings.DeleteSessionPartsAsync(batch.Select(part => part.Id).ToList(), cancellationToken);
            foreach (var exam in batch.GroupBy(part => part.ExamId))
                await audit.WriteAsync(new AuditEntryInput(AuditActions.RecordingPurged, null, "system", exam.Key, null,
                    $"{exam.Count()} whole-viva recording piece(s) older than {retention} days"), cancellationToken);
            purged += batch.Count;
        }
        return purged;
    }

    private static (string MediaType, bool HasVideo) CheckType(string contentType, RecordingMode mode)
    {
        var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (!AllowedTypes.Contains(mediaType))
            throw new ArgumentException(L10n.T("Unsupported recording format."));
        var hasVideo = mediaType.StartsWith("video/", StringComparison.Ordinal);
        if (hasVideo && mode != RecordingMode.AudioVideo)
            throw new ArgumentException(L10n.T("Unsupported recording format."));
        return (mediaType, hasVideo);
    }

    public async Task DeleteForExamAsync(int examId, CancellationToken cancellationToken = default)
    {
        foreach (var path in await recordings.ListPathsForExamAsync(examId, cancellationToken))
            store.Delete(path);
    }

    private static async Task CopyLimitedAsync(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (target.Length + read > maxBytes)
                throw new ArgumentException(L10n.T("The recording is too large."));
            await target.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }
}

/// <summary>Where encrypted recordings live. A folder on disk by default.</summary>
public interface IRecordingStore
{
    Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default);
    void Delete(string relativePath);
}

public sealed class FileRecordingStore(string root) : IRecordingStore
{
    public async Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken = default)
    {
        var path = Resolve(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, data, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    public async Task<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = Resolve(relativePath);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    public void Delete(string relativePath)
    {
        var path = Resolve(relativePath);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Keeps every path inside the root, whatever is stored in the database.</summary>
    private string Resolve(string relativePath)
    {
        var fullRoot = System.IO.Path.GetFullPath(root);
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, relativePath));
        if (!path.StartsWith(fullRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Recording path escapes the store.");
        return path;
    }
}
