using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIVES.WebRazor.Realtime;

/// <summary>
/// Times come back from SQL Server with an unspecified kind but are stored in UTC. Writing them
/// with a trailing "Z" stops the browser from reading them as local time and skewing the timer.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));

    /// <summary>The same rules for JSON embedded in a page.</summary>
    public static readonly JsonSerializerOptions PageOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() }
    };
}
