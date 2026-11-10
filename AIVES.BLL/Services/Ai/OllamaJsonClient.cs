using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// One call to Ollama's <c>POST /api/generate</c> that must answer with JSON matching a schema.
/// Ollama constrains the output to the schema (structured outputs), so even a small local model
/// returns something the shared prompt parsers can read.
/// </summary>
public static class OllamaJsonClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Returns the model's JSON text. Throws <see cref="InvalidOperationException"/> when Ollama fails.</summary>
    public static async Task<string> GenerateAsync(HttpClient httpClient, string model, string prompt, object geminiSchema,
        double temperature, int contextTokens, string? keepAlive, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["format"] = ToJsonSchema(geminiSchema),
            ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = temperature, ["num_ctx"] = contextTokens }
        };
        if (!string.IsNullOrWhiteSpace(keepAlive))
            body["keep_alive"] = keepAlive;

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/generate") { Content = JsonContent.Create(body, options: JsonOptions) };
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Ollama could not be reached: {ex.Message}", ex);
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode} for model '{model}'. Is the model pulled (ollama pull {model})?");
            try
            {
                using var envelope = JsonDocument.Parse(text);
                if (envelope.RootElement.TryGetProperty("response", out var output) && output.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(output.GetString()))
                    return output.GetString()!;
            }
            catch (JsonException)
            {
            }
            throw new InvalidOperationException("Ollama returned an empty or malformed response.");
        }
    }

    /// <summary>
    /// Gemini's response schema uses upper-case OpenAPI types ("OBJECT", "STRING") and <c>nullable</c>;
    /// Ollama expects JSON Schema ("object", "string", and a type list for nullable values).
    /// </summary>
    public static JsonNode ToJsonSchema(object geminiSchema)
    {
        var node = JsonSerializer.SerializeToNode(geminiSchema, JsonOptions) ?? new JsonObject();
        Convert(node);
        return node;
    }

    private static void Convert(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var nullable = obj["nullable"]?.GetValue<bool>() == true;
                obj.Remove("nullable");
                if (obj["type"] is JsonValue type && type.TryGetValue<string>(out var name))
                {
                    var lower = name.ToLowerInvariant();
                    obj["type"] = nullable ? new JsonArray(lower, "null") : lower;
                }
                foreach (var (_, child) in obj.ToList())
                    Convert(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    Convert(child);
                break;
        }
    }
}
