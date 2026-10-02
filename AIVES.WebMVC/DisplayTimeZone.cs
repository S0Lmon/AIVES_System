namespace AIVES.WebMVC;

/// <summary>
/// The time zone exam times are entered and shown in (Exams:TimeZone, default Asia/Ho_Chi_Minh).
/// Times are stored in UTC; the server itself may run in UTC (Docker), so the zone is explicit.
/// </summary>
public sealed class DisplayTimeZone
{
    public const string DefaultZoneId = "Asia/Ho_Chi_Minh";

    public DisplayTimeZone(IConfiguration configuration)
    {
        var id = configuration["Exams:TimeZone"];
        Zone = string.IsNullOrWhiteSpace(id) ? Load(DefaultZoneId) : Load(id.Trim());
    }

    public TimeZoneInfo Zone { get; }

    /// <summary>For example "UTC+07:00", shown next to time inputs.</summary>
    public string OffsetLabel
    {
        get
        {
            var offset = Zone.BaseUtcOffset;
            return $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}";
        }
    }

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    private static TimeZoneInfo Load(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException && id == DefaultZoneId)
        {
            // Slim container images can ship without tzdata. Vietnam keeps UTC+7 all year, so a fixed
            // zone is exact for the default; any other configured zone must exist on the host.
            return TimeZoneInfo.CreateCustomTimeZone(DefaultZoneId, TimeSpan.FromHours(7), "Indochina Time", "Indochina Time");
        }
    }
}
