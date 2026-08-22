namespace Kronikol.Extensions.Otlp;

/// <summary>The OpenTelemetry span kind (the numbers are the OTLP enum values).</summary>
public enum OtlpSpanKind
{
    /// <summary>Unspecified — treated as <see cref="Internal"/>.</summary>
    Unspecified = 0,

    /// <summary>Internal operation, no remote peer.</summary>
    Internal = 1,

    /// <summary>Inbound (server) side of a remote call.</summary>
    Server = 2,

    /// <summary>Outbound (client) side of a remote call — the kind Kronikol renders as an arrow.</summary>
    Client = 3,

    /// <summary>Message producer.</summary>
    Producer = 4,

    /// <summary>Message consumer.</summary>
    Consumer = 5,
}

/// <summary>The OpenTelemetry span status code.</summary>
public enum OtlpStatusCode
{
    /// <summary>No status was set.</summary>
    Unset = 0,

    /// <summary>Explicitly marked successful.</summary>
    Ok = 1,

    /// <summary>The operation failed.</summary>
    Error = 2,
}

/// <summary>
/// One span from an OTLP export, flattened to the shape <see cref="SpanToInteractionMapper"/> needs:
/// identity, timing, kind, status and <em>string</em> attributes (span attributes and the resource
/// attributes of the batch it arrived in). Produced by <see cref="OtlpTraceReader"/> from either the
/// protobuf or the JSON encoding, so the mapper never sees a wire format.
/// </summary>
/// <remarks>
/// Attribute values are rendered as text: strings verbatim, numbers/booleans in invariant form, arrays and
/// key-value lists as compact JSON, bytes as base64. That is exactly what a diagram note can show, and it
/// keeps the model free of a protobuf dependency.
/// </remarks>
public sealed record OtlpSpan
{
    /// <summary>An empty, ordinal attribute bag.</summary>
    public static readonly IReadOnlyDictionary<string, string> NoAttributes = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>32 lowercase hex characters (the W3C trace id). Browser-driven suites mint this as the test id.</summary>
    public string TraceId { get; init; } = "";

    /// <summary>16 lowercase hex characters.</summary>
    public string SpanId { get; init; } = "";

    /// <summary>16 lowercase hex characters, or null at the root of a trace.</summary>
    public string? ParentSpanId { get; init; }

    /// <summary>The span name (e.g. <c>Trial.find</c>, <c>GET</c>, <c>hgetall</c>).</summary>
    public string Name { get; init; } = "";

    /// <summary>The span kind.</summary>
    public OtlpSpanKind Kind { get; init; }

    /// <summary>Start time, nanoseconds since the Unix epoch.</summary>
    public ulong StartTimeUnixNano { get; init; }

    /// <summary>End time, nanoseconds since the Unix epoch.</summary>
    public ulong EndTimeUnixNano { get; init; }

    /// <summary>Span attributes, flattened to text.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = NoAttributes;

    /// <summary>Resource attributes of the batch this span arrived in (<c>service.name</c> lives here).</summary>
    public IReadOnlyDictionary<string, string> ResourceAttributes { get; init; } = NoAttributes;

    /// <summary>The span status code.</summary>
    public OtlpStatusCode StatusCode { get; init; }

    /// <summary>The span status message (shown on the response arrow when the status is an error).</summary>
    public string? StatusMessage { get; init; }

    /// <summary>Name of the instrumentation scope that produced the span (diagnostics only).</summary>
    public string? ScopeName { get; init; }

    /// <summary>The producing service — <c>service.name</c> from the resource attributes.</summary>
    public string? ServiceName => ResourceAttributes.TryGetValue("service.name", out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>Start time as a <see cref="DateTimeOffset"/> (100 ns resolution — the CLR tick).</summary>
    public DateTimeOffset StartTime => FromUnixNano(StartTimeUnixNano);

    /// <summary>End time as a <see cref="DateTimeOffset"/>; never earlier than <see cref="StartTime"/>.</summary>
    public DateTimeOffset EndTime => EndTimeUnixNano > StartTimeUnixNano ? FromUnixNano(EndTimeUnixNano) : StartTime;

    /// <summary>Span duration in milliseconds.</summary>
    public double DurationMs => (EndTime - StartTime).TotalMilliseconds;

    /// <summary>The first of <paramref name="names"/> present as a non-empty span attribute, else null.</summary>
    public string? Attribute(params string[] names)
    {
        foreach (var name in names)
        {
            if (Attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>Whether any of <paramref name="names"/> is present as a span attribute (even when empty).</summary>
    public bool HasAttribute(params string[] names) => names.Any(Attributes.ContainsKey);

    /// <summary>Converts nanoseconds since the Unix epoch to a <see cref="DateTimeOffset"/> (truncated to ticks).</summary>
    public static DateTimeOffset FromUnixNano(ulong nanos) => DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / 100));
}
