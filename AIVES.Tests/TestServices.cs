using AIVES.BLL.Services.Operations;
using AIVES.BLL.Services.Recordings;
using AIVES.DAL.Data;
using AIVES.DAL.Data.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AIVES.Tests;

/// <summary>Real operation services over an in-memory database, for service-level tests.</summary>
internal static class TestServices
{
    public static AuditService Audit(ApplicationDbContext db, TimeProvider clock) => new(new AuditRepository(db), clock, NullLogger<AuditService>.Instance);

    public static SystemSettingsService Settings(ApplicationDbContext db, TimeProvider clock) =>
        new(new SettingsRepository(db), new MemoryCache(new MemoryCacheOptions()), clock);

    public static GlossaryService Glossary(ApplicationDbContext db) => new(new GlossaryRepository(db));

    public static SubjectAccessService SubjectAccess(ApplicationDbContext db) => new(new SubjectAssignmentRepository(db));

    public static RecordingService Recordings(ApplicationDbContext db, TimeProvider clock, IRecordingStore? store = null) => new(
        new RecordingRepository(db),
        new InterviewRepository(db),
        store ?? new MemoryRecordingStore(),
        new EphemeralDataProtectionProvider(),
        Settings(db, clock),
        Audit(db, clock),
        Options.Create(new RecordingOptions()),
        clock,
        NullLogger<RecordingService>.Instance);
}

internal sealed class MemoryRecordingStore : IRecordingStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task WriteAsync(string relativePath, byte[] data, CancellationToken cancellationToken = default)
    {
        Files[relativePath] = data;
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(Files.TryGetValue(relativePath, out var data) ? data : null);

    public void Delete(string relativePath) => Files.Remove(relativePath);
}
