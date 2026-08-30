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

    // One handle for the whole command. Grep reads every distinct body — tens of thousands on a large
    // report — and opening the file per body was the second-largest cost after tier-0 JIT (QUERY_PERF_PLAN
    // §1.2: up to ~0.75 s per bulk command). Single-threaded per-command use, so a shared position is safe.
    private readonly FileStream _stream = PayloadReader.Open(index);

    /// <summary>The raw text of a body, or null when it cannot be read back.</summary>
    public string? Raw(string hash)
    {
        if (_raw.TryGetValue(hash, out var cached))
            return cached;
        var content = index.Bodies.TryGetValue(hash, out var entry) ? PayloadReader.Read(_stream, entry.First) : null;
        return _raw[hash] = content;
    }

    /// <summary>
    /// Any other slice of the report — a diagram, in practice — read on the same open handle. Uncached:
    /// each diagram is wanted once per command.
    /// </summary>
    public string? ReadSlice(Slice slice) => PayloadReader.Read(_stream, slice);

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
        _stream.Dispose();
    }
}
