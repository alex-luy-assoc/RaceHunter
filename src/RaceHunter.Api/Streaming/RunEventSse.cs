using System.Text.Json;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Api.Streaming;

public static class RunEventSse
{
    public static long ResolveAfter(long? queryAfter, string? lastEventId)
    {
        var query = Math.Max(0, queryAfter ?? 0);
        return long.TryParse(lastEventId, out var acknowledged) ? Math.Max(query, Math.Max(0, acknowledged)) : query;
    }

    public static string Format(RunEvent item)
    {
        var payload = JsonSerializer.Serialize(new
        {
            cursor = item.Cursor,
            kind = item.Kind,
            message = item.Message,
            occurredAtUtc = item.OccurredAtUtc
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $"id: {item.Cursor}\nevent: {item.Kind}\ndata: {payload}\n\n";
    }
}
