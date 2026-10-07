namespace AIVES.DTO;

/// <summary>
/// The exam time layout as one pure function of the settings (plan §6: the schedule is generated
/// from the start time, the duration per student, optional buffer time between students and
/// optional breaks after every N students). Lives next to the DTOs so the business layer can build
/// the schedule and the data layer can still fall back to the layout used before slots were stored
/// (<see cref="LegacySlotStart"/>/<see cref="LegacyEndsAt"/>, used for rows created before the
/// schedule columns existed and for fixtures that never set them).
/// </summary>
public static class ExamSchedule
{
    /// <summary>
    /// Start time of every candidate's slot, in sitting order: each slot begins after the previous
    /// one plus <paramref name="bufferMinutes"/>, and a break of <paramref name="breakMinutes"/>
    /// is inserted after every <paramref name="breakEveryCount"/> candidates when both are set.
    /// The returned list has exactly <paramref name="candidateCount"/> entries.
    /// </summary>
    public static IReadOnlyList<DateTime> Build(DateTime startsAtUtc, int slotMinutes, int candidateCount,
        int bufferMinutes = 0, int breakMinutes = 0, int breakEveryCount = 0)
    {
        if (candidateCount < 0)
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        var slots = new List<DateTime>(Math.Max(candidateCount, 0));
        var cursor = startsAtUtc;
        for (var i = 1; i <= candidateCount; i++)
        {
            slots.Add(cursor);
            cursor = cursor.AddMinutes(slotMinutes);
            if (i == candidateCount)
                break;
            if (breakEveryCount > 0 && breakMinutes > 0 && i % breakEveryCount == 0)
                cursor = cursor.AddMinutes(breakMinutes);
            cursor = cursor.AddMinutes(bufferMinutes);
        }
        return slots;
    }

    /// <summary>The end of the whole schedule: the last slot's end, without a trailing buffer or break.</summary>
    public static DateTime ScheduleEndsAt(DateTime startsAtUtc, int slotMinutes, int candidateCount,
        int bufferMinutes = 0, int breakMinutes = 0, int breakEveryCount = 0) =>
        candidateCount == 0 ? startsAtUtc : Build(startsAtUtc, slotMinutes, candidateCount, bufferMinutes, breakMinutes, breakEveryCount)[^1].AddMinutes(slotMinutes);

    /// <summary>The layout before slots were stored: slot N starts (N - 1) durations after the exam start.</summary>
    public static DateTime LegacySlotStart(DateTime examStartsAtUtc, int slotMinutes, int order) =>
        examStartsAtUtc.AddMinutes(slotMinutes * (order - 1));

    /// <summary>The end of that layout: start plus one slot per candidate.</summary>
    public static DateTime LegacyEndsAt(DateTime examStartsAtUtc, int slotMinutes, int candidateCount) =>
        examStartsAtUtc.AddMinutes(slotMinutes * candidateCount);
}