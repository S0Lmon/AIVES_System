using AIVES.DAL.Data.Repositories;
using AIVES.DTO;
using AIVES.DTO.Localization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace AIVES.BLL.Services.Operations;

/// <summary>Append-only log of actions that matter for transparency: grades, recordings, exams, accounts.</summary>
public interface IAuditService
{
    /// <summary>Writes an entry. Never throws: a failed audit write is logged but does not undo the action.</summary>
    Task WriteAsync(AuditEntryInput entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditService(IAuditRepository audit, TimeProvider clock, ILogger<AuditService> logger) : IAuditService
{
    public async Task WriteAsync(AuditEntryInput entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await audit.AddAsync(entry, clock.GetUtcNow().UtcDateTime, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not write audit entry {Action} for exam {ExamId}", entry.Action, entry.ExamId);
        }
    }

    public Task<IReadOnlyList<AuditEntryDto>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default) =>
        audit.QueryAsync(query, cancellationToken);
}

/// <summary>Speech and recording settings an administrator changes at run time.</summary>
public interface ISystemSettingsService
{
    Task<SpeechSettingsDto> GetSpeechAsync(CancellationToken cancellationToken = default);
    Task SaveSpeechAsync(SpeechSettingsDto settings, CancellationToken cancellationToken = default);
}

public sealed class SystemSettingsService(ISettingsRepository settings, IMemoryCache cache, TimeProvider clock) : ISystemSettingsService
{
    private const string CacheKey = "settings:speech";
    public const double MinRate = 0.6;
    public const double MaxRate = 1.4;
    public const int MinRetentionDays = 7;
    public const int MaxRetentionDays = 3650;

    public async Task<SpeechSettingsDto> GetSpeechAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out SpeechSettingsDto? cached) && cached is not null)
            return cached;
        var values = await settings.GetAllAsync(cancellationToken);
        var defaults = SpeechSettingsDto.Default;
        var enabled = values.TryGetValue("Speech:EnabledLanguages", out var list)
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(code => code.ToLanguage()).Distinct().ToList()
            : defaults.EnabledLanguages.ToList();
        if (enabled.Count == 0)
            enabled = defaults.EnabledLanguages.ToList();
        var defaultLanguage = values.TryGetValue("Speech:DefaultLanguage", out var language) ? language.ToLanguage() : defaults.DefaultLanguage;
        var result = new SpeechSettingsDto(
            enabled.Contains(defaultLanguage) ? defaultLanguage : enabled[0],
            enabled,
            values.TryGetValue("Speech:Rate", out var rate) && double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRate)
                ? Math.Clamp(parsedRate, MinRate, MaxRate) : defaults.SpeechRate,
            values.GetValueOrDefault("Speech:VoiceVi") is { Length: > 0 } vi ? vi : null,
            values.GetValueOrDefault("Speech:VoiceEn") is { Length: > 0 } en ? en : null,
            values.TryGetValue("Recording:RetentionDays", out var days) && int.TryParse(days, CultureInfo.InvariantCulture, out var parsedDays)
                ? Math.Clamp(parsedDays, MinRetentionDays, MaxRetentionDays) : defaults.RecordingRetentionDays);
        // Short cache: several web instances each read their own copy.
        cache.Set(CacheKey, result, TimeSpan.FromSeconds(30));
        return result;
    }

    public async Task SaveSpeechAsync(SpeechSettingsDto speech, CancellationToken cancellationToken = default)
    {
        if (speech.EnabledLanguages.Count == 0)
            throw new ArgumentException(L10n.T("Enable at least one interview language."));
        if (!speech.EnabledLanguages.Contains(speech.DefaultLanguage))
            throw new ArgumentException(L10n.T("The default language must be one of the enabled languages."));
        if (speech.SpeechRate is < MinRate or > MaxRate)
            throw new ArgumentException(L10n.Format("The speaking rate must be between {0} and {1}.", MinRate, MaxRate));
        if (speech.RecordingRetentionDays is < MinRetentionDays or > MaxRetentionDays)
            throw new ArgumentException(L10n.Format("Recordings must be kept between {0} and {1} days.", MinRetentionDays, MaxRetentionDays));

        await settings.SetAsync(new Dictionary<string, string>
        {
            ["Speech:DefaultLanguage"] = speech.DefaultLanguage.ToCultureCode(),
            ["Speech:EnabledLanguages"] = string.Join(',', speech.EnabledLanguages.Distinct().Select(item => item.ToCultureCode())),
            ["Speech:Rate"] = speech.SpeechRate.ToString("0.00", CultureInfo.InvariantCulture),
            ["Speech:VoiceVi"] = speech.VietnameseVoice?.Trim() ?? string.Empty,
            ["Speech:VoiceEn"] = speech.EnglishVoice?.Trim() ?? string.Empty,
            ["Recording:RetentionDays"] = speech.RecordingRetentionDays.ToString(CultureInfo.InvariantCulture)
        }, clock.GetUtcNow().UtcDateTime, cancellationToken);
        cache.Remove(CacheKey);
    }
}

/// <summary>Per-subject vocabulary that improves recognition and grading of technical terms.</summary>
public interface IGlossaryService
{
    Task<IReadOnlyList<GlossaryTermDto>> ListAsync(int subjectId, CancellationToken cancellationToken = default);
    Task<GlossaryTermDto> AddAsync(int subjectId, string term, string? spokenForms, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class GlossaryService(IGlossaryRepository glossary) : IGlossaryService
{
    public Task<IReadOnlyList<GlossaryTermDto>> ListAsync(int subjectId, CancellationToken cancellationToken = default) =>
        glossary.ListAsync(subjectId, cancellationToken);

    public Task<GlossaryTermDto> AddAsync(int subjectId, string term, string? spokenForms, CancellationToken cancellationToken = default)
    {
        var name = (term ?? string.Empty).Trim();
        if (name.Length is < 2 or > 100)
            throw new ArgumentException(L10n.T("A term must be between 2 and 100 characters."));
        var forms = (spokenForms ?? string.Empty)
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(form => form.Length >= 2 && !form.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        if (string.Join(';', forms).Length > 1000)
            throw new ArgumentException(L10n.T("The spoken forms are too long."));
        return glossary.AddAsync(subjectId, name, forms, cancellationToken);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => glossary.DeleteAsync(id, cancellationToken);
}

/// <summary>
/// Which lecturers may run exams for which subjects. A subject without assigned lecturers stays
/// open to every lecturer, so existing installations keep working until an administrator assigns.
/// Administrators can use every subject.
/// </summary>
public interface ISubjectAccessService
{
    Task<IReadOnlyList<SubjectAssignmentDto>> ListAssignmentsAsync(CancellationToken cancellationToken = default);
    Task<bool> CanUseAsync(int subjectId, ExamActor actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> FilterAsync<T>(IReadOnlyList<T> subjects, Func<T, int> id, ExamActor actor, CancellationToken cancellationToken = default);
    Task SetLecturersAsync(int subjectId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);
}

public sealed class SubjectAccessService(ISubjectAssignmentRepository assignments) : ISubjectAccessService
{
    public Task<IReadOnlyList<SubjectAssignmentDto>> ListAssignmentsAsync(CancellationToken cancellationToken = default) =>
        assignments.ListAsync(cancellationToken);

    public async Task<bool> CanUseAsync(int subjectId, ExamActor actor, CancellationToken cancellationToken = default)
    {
        if (actor.IsAdmin)
            return true;
        var restrictions = await assignments.GetRestrictionsAsync(cancellationToken);
        return !restrictions.TryGetValue(subjectId, out var lecturers) || lecturers.Contains(actor.UserId);
    }

    public async Task<IReadOnlyList<T>> FilterAsync<T>(IReadOnlyList<T> subjects, Func<T, int> id, ExamActor actor, CancellationToken cancellationToken = default)
    {
        if (actor.IsAdmin)
            return subjects;
        var restrictions = await assignments.GetRestrictionsAsync(cancellationToken);
        return subjects.Where(subject => !restrictions.TryGetValue(id(subject), out var lecturers) || lecturers.Contains(actor.UserId)).ToList();
    }

    public Task SetLecturersAsync(int subjectId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default) =>
        assignments.SetLecturersAsync(subjectId, userIds.Distinct().ToList(), cancellationToken);
}
