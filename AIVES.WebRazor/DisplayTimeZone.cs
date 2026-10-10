namespace AIVES.WebRazor;

public sealed class DisplayTimeZone
{
    public const string DefaultZoneId = "Asia/Ho_Chi_Minh";

    public DisplayTimeZone(IConfiguration configuration)
    {
        var id = configuration["Exams:TimeZone"];
        Zone = string.IsNullOrWhiteSpace(id) ? Load(DefaultZoneId) : Load(id.Trim());
    }

    public TimeZoneInfo Zone { get; }

    public string OffsetLabel
    {
        get
        {
            var offset = Zone.BaseUtcOffset;
            return $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm}";
        }
    }

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    private static TimeZoneInfo Load(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when ((ex is TimeZoneNotFoundException or InvalidTimeZoneException) && id == DefaultZoneId)
        {
            return TimeZoneInfo.CreateCustomTimeZone(DefaultZoneId, TimeSpan.FromHours(7), "Indochina Time", "Indochina Time");
        }
    }
}
