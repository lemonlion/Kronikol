using System.Text.Json;

namespace Kronikol.Tests.InternalFlow;

/// <summary>
/// A segment as its popup shows it, read from a report's segment map by the popup script's rule: <c>sameAs</c> to the
/// segment that holds its flow, then, from 4.0.1, that segment's places in the table its <c>table</c> names (a map
/// written before holds each flow inline). A fact that reads a segment's diagram or flame chart out of a written map
/// reads it through here, since the segment itself no longer carries them.
/// </summary>
internal static class SegmentMapText
{
    public sealed record Shown(string? Title, string? Content, string? FlameData, string? Message);

    public static Shown Resolve(JsonElement map, string key) =>
        Resolve(k => map.TryGetProperty(k, out var value) ? value : null, key);

    public static Shown Resolve(IReadOnlyDictionary<string, JsonElement> map, string key) =>
        Resolve(k => map.TryGetValue(k, out var value) ? value : null, key);

    private static Shown Resolve(Func<string, JsonElement?> get, string key)
    {
        var segment = get(key) ?? throw new KeyNotFoundException($"no segment {key}");
        var flow = segment.TryGetProperty("sameAs", out var sameAs)
            ? get(sameAs.GetString()!) ?? throw new KeyNotFoundException($"{key} names {sameAs.GetString()}, which the map does not hold")
            : segment;

        string? content, flame;
        if (flow.TryGetProperty("table", out var tableKey))
        {
            var table = get(tableKey.GetString()!) ?? throw new KeyNotFoundException($"{key} names a table the map does not hold");
            content = table.GetProperty("contents")[flow.GetProperty("contentAt").GetInt32()].GetString();
            flame = flow.TryGetProperty("flameAt", out var at) ? table.GetProperty("flames")[at.GetInt32()].GetRawText() : null;
        }
        else
        {
            content = Text(flow, "content");
            flame = flow.TryGetProperty("flameData", out var inline) ? inline.GetRawText() : null;
        }

        return new Shown(Text(segment, "title"), content, flame, Text(segment, "message"));
    }

    /// <summary>A segment as <c>BuildSegmentData</c> made it, before the map was stored, in the same shape.</summary>
    public static Shown Built(JsonElement segment) =>
        new(Text(segment, "title"), Text(segment, "content"),
            segment.TryGetProperty("flameData", out var flame) ? flame.GetRawText() : null, Text(segment, "message"));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
