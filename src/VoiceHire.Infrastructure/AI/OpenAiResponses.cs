using System.Net.Http.Json;
using System.Text.Json;

namespace VoiceHire.Infrastructure.AI;

/// <summary>Calls the Responses API with a strict JSON schema and returns the parsed output.</summary>
internal static class OpenAiResponses
{
    public static async Task<JsonDocument> GetStructuredAsync(
        HttpClient http, string model, string instructions, string input, string schemaName, object schema, CancellationToken ct)
    {
        var body = new
        {
            model,
            instructions,
            input,
            text = new { format = new { type = "json_schema", name = schemaName, strict = true, schema } }
        };

        using var response = await http.PostAsJsonAsync("responses", body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Responses request '{schemaName}' failed ({(int)response.StatusCode}): {json}");

        return JsonDocument.Parse(ExtractOutputText(json));
    }

    private static string ExtractOutputText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        foreach (var item in doc.RootElement.GetProperty("output").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "message") continue;
            foreach (var content in item.GetProperty("content").EnumerateArray())
            {
                switch (content.GetProperty("type").GetString())
                {
                    case "output_text": return content.GetProperty("text").GetString()!;
                    case "refusal": throw new InvalidOperationException($"Model refused: {content.GetProperty("refusal").GetString()}");
                }
            }
        }
        throw new InvalidOperationException("Response contained no output text.");
    }
}
