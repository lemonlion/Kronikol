using System.Text.Json;

namespace Kronikol.Query;

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
    private readonly HashSet<string> _readBeforeFootnote = new(StringComparer.Ordinal);

    /// <summary>
    /// How many distinct bodies <see cref="Json"/> read as the document before a capture footnote: a capped batch or
    /// rows body, of which only what was recorded could be read.
    /// </summary>
    public int CappedReads => _readBeforeFootnote.Count;

    // One handle for the whole command. Grep reads every distinct body — tens of thousands on a large
    // report — and opening the file per body was the second-largest cost after tier-0 JIT (QUERY_PERF_PLAN
    // §1.2: up to ~0.75 s per bulk command). Single-threaded per-command use, so a shared position is safe.
    private readonly Stream _stream = PayloadReader.Open(index);

    /// <summary>The raw text of a body, or null when it cannot be read back.</summary>
    public string? Raw(string hash)
    {
        if (_raw.TryGetValue(hash, out var cached))
            return cached;
        string? content = null;
        if (index.Bodies.TryGetValue(hash, out var entry))
        {
            index.BodyReads++;
            content = PayloadReader.Read(_stream, entry.First);
        }
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

        // Not JSON is remembered as null, so the miss is as cheap as the hit.
        if (Raw(hash) is not { } content)
            return _parsed[hash] = null;
        var document = Parse(content);
        if (document is null && Tracking.CaptureFootnote.DocumentBefore(content) is { } before && (document = Parse(before)) is not null)
            _readBeforeFootnote.Add(hash);
        return _parsed[hash] = document;
    }

    /// <summary>
    /// A body as a JSON document, or null when it is not JSON. A capped body is a complete document and the capture's
    /// footnote after it (#168 F8): the page reads past the footnote, and every reader of a body here does the same.
    /// </summary>
    internal static JsonDocument? ParseJson(string body) =>
        Parse(body) ?? (Tracking.CaptureFootnote.DocumentBefore(body) is { } before ? Parse(before) : null);

    private static JsonDocument? Parse(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a capture capped this body: cut short (<c>…truncated</c>, or the RESP decoder's <c>…[bulk string
    /// truncated: …]</c>), or a document followed by its footnote. The markers the page's note formatter takes off.
    /// </summary>
    public static bool CappedAtCapture(string body) =>
        body.Contains("…truncated (", StringComparison.Ordinal)
        || body.Contains("…[bulk string truncated: ", StringComparison.Ordinal)
        || Tracking.CaptureFootnote.DocumentBefore(body) is not null;

    public void Dispose()
    {
        foreach (var document in _parsed.Values)
            document?.Dispose();
        _parsed.Clear();
        _stream.Dispose();
    }
}
