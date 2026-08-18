using System.Text.Json;
using System.Text.Json.Nodes;

namespace RaceHunter.Infrastructure.Security;

public static class SensitiveDataRedactor
{
    public const string Replacement = "[REDACTED]";
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Cookie", "Set-Cookie", "X-Api-Key", "X-Demo-Control-Key"
    };

    public static IReadOnlyDictionary<string, string> RedactHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) =>
        headers.ToDictionary(
            item => item.Key,
            item => SensitiveHeaders.Contains(item.Key) ? Replacement : string.Join(',', item.Value),
            StringComparer.OrdinalIgnoreCase);

    public static string RedactJson(string json, IEnumerable<string> sensitiveJsonPaths)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException) { return Replacement; }
        if (root is null) return "null";
        foreach (var path in sensitiveJsonPaths)
        {
            var segments = path.Trim().TrimStart('$').TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            JsonNode? current = root;
            for (var index = 0; index < segments.Length - 1 && current is JsonObject currentObject; index++)
                current = currentObject[segments[index]];
            if (segments.Length > 0 && current is JsonObject parent && parent.ContainsKey(segments[^1]))
                parent[segments[^1]] = Replacement;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
