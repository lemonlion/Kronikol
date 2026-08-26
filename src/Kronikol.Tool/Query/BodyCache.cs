using System.Text.Json;

namespace Kronikol.Tool.Query;

/// <summary>
/// Parses each distinct body once per command, however many interactions carried it. Grep established the
/// rule — evaluate once per distinct content — and the cache makes it automatic for every command that
/// reads bodies in bulk (<c>values</c>, <c>--where</c>, <c>diff</c>, numeric <c>grep</c>). Dispose at
/// command end; every <see cref="JsonElement"/> handed out stays valid until then.
/// </summary>
internal sealed class BodyCache(ReportIndex index) : IDisposable
{
    private readonly Dictionary<string, string?> _raw = [];
    private readonly Dictionary<string, JsonDocument?> _parsed = [];

    /// <summary>The raw text of a body, or null when it cannot be read back.</summary>
    public string? Raw(string hash)
    {
        if (_raw.TryGetValue(hash, out var cached))
            return cached;
        var content = index.Bodies.TryGetValue(hash, out var entry) ? PayloadReader.Read(index, entry.First) : null;
        return _raw[hash] = content;
    }

    /// <summary>The parsed document of a body, or null when it is absent or not JSON.</summary>
    public JsonDocument? Json(string hash)
    {
        if (_parsed.TryGetValue(hash, out var cached))
            return cached;

        JsonDocument? document = null;
        if (Raw(hash) is { } content)
        {
            try
            {
                document = JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                // Not JSON — remembered as null so the miss is as cheap as the hit.
            }
        }
        return _parsed[hash] = document;
    }

    public void Dispose()
    {
        foreach (var document in _parsed.Values)
            document?.Dispose();
        _parsed.Clear();
    }
}
