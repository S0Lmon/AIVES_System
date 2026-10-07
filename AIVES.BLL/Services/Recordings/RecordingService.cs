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

    /// <summary>Largest accepted upload for one answer.</summary>
    public long MaxBytes { get; set; } = 30 * 1024 * 1024;
}

/// <summary>A decrypted recording ready to stream to an authorised viewer.</summary>
public sealed record RecordingContent(byte[] Data, string ContentType, bool HasVideo);

/// <summary>
/// Evidence recordings of each spoken answer. Files are encrypted with the application's Data
/// Protection keys (shared across instances and themselves encrypted at rest), so a copied volume
/// or backup alone does not expose students' voices. Only the exam's lecturer or an administrator
/// may play a recording, and every playback is written to the audit log. Recordings older than the
/// retention period are deleted by <see cref="PurgeExpiredAsync"/>.
/// </summary>
public interface IRecordingService
{
    Task SaveAsync(int candidateId, string email, int turnId, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<RecordingContent?> OpenAsync(int recordingId, ExamActor actor, CancellationToken cancellationToken = default);
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

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var retention = (await settings.GetSpeechAsync(cancellationToken)).RecordingRetentionDays;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retention);
        var purged = 0;
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
