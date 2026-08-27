using Kronikol.Tracking;

namespace Kronikol.Extensions.Otlp;

/// <summary>How exported spans that carry no captured W3C trace id are grouped into traces.</summary>
public enum TraceIdStrategy
{
    /// <summary>
    /// Derive the trace id deterministically from the record's <c>TestId</c>
    /// (<see cref="Kronikol.Ingestion.InteractionRecord.ToGuid"/> recipe), so one test renders as one
    /// trace in the backend — the Kronikol mental model. The default. Without this,
    /// <c>RequestResponseLogger.LogPair</c> and the handlers mint a fresh <c>TraceId</c> Guid per
    /// pair, which would flood the backend with thousands of single-span traces.
    /// </summary>
    PerTest = 0,

    /// <summary>Keep the raw per-pair <c>TraceId</c> Guid: every request/response pair is its own trace.</summary>
    PerPair = 1,
}

/// <summary>
/// The schema for one OTLP export destination: where to POST, what to include, and — for the streaming
/// <see cref="OtlpExportSink"/> — the bounded-queue/batching discipline. Used by
/// <see cref="OtlpSpanMapper"/> (mapping decisions), <see cref="OtlpExporter"/> (batch push) and
/// <see cref="OtlpExportSink"/> (live streaming).
/// </summary>
/// <remarks>
/// <para><strong>Non-interference invariant.</strong> The exporter this configures is a standalone
/// <c>HttpClient</c> POSTing OTLP/JSON to a URL. It never touches the observed system's
/// <c>TracerProviderBuilder</c>, never registers processors and never flips <c>Activity.Recorded</c> —
/// exporting Kronikol's captures can never change what the system under test emits.</para>
/// <para><strong>Redaction.</strong> Batch export from the in-process store is already redacted
/// (<see cref="RequestResponseLogger.Redaction"/> ran in <c>Log()</c>); a streaming sink fed by the taps
/// receives records the taps redacted before the sink. Only the CLI's NDJSON path has to redact itself
/// (<c>kronikol export</c> applies <see cref="CaptureRedaction"/> by default).</para>
/// </remarks>
public sealed class OtlpExportOptions
{
    /// <summary>The OTLP/HTTP traces endpoint to POST to, e.g. <c>http://localhost:4318/v1/traces</c>. Required.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Headers added to every export request (name → value, e.g. an auth token) — the exporting twin of
    /// <see cref="OtlpTapOptions.ExpectedHeaders"/>. Empty by default.
    /// </summary>
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Export request/response bodies as <c>kronikol.request.body</c> / <c>kronikol.response.body</c>
    /// span attributes. Default false: bodies are large and may carry data the backend should not hold.
    /// </summary>
    public bool IncludeBodies { get; set; }

    /// <summary>Cap applied to each body attribute when <see cref="IncludeBodies"/> is on (collectors enforce attribute limits). Default 8 KiB.</summary>
    public int BodyAttributeCapBytes { get; set; } = 8 * 1024;

    /// <summary>
    /// Also export records that <em>came from</em> the backend's own telemetry (captured by
    /// <see cref="OtlpTap"/>, <c>CapturedBy</c> = <c>span</c> or <c>wire + span</c>). Default false:
    /// re-exporting them duplicates spans the backend already stores (echo suppression).
    /// </summary>
    public bool IncludeSpanSourced { get; set; }

    /// <summary>How records with no captured W3C trace id group into traces. Default <see cref="TraceIdStrategy.PerTest"/>.</summary>
    public TraceIdStrategy TraceIdStrategy { get; set; } = TraceIdStrategy.PerTest;

    /// <summary>
    /// Streaming sink only: how many log entries may wait to be batched. The queue is bounded and the
    /// newest entry is dropped when it is full (D3: capture never blocks), counted in
    /// <see cref="OtlpExportSink.EntriesDropped"/>. Default 4096.
    /// </summary>
    public int QueueCapacity { get; set; } = 4096;

    /// <summary>Largest number of spans per POST; bigger exports are paged. Default 512.</summary>
    public int BatchMaxSpans { get; set; } = 512;

    /// <summary>Streaming sink only: how often a partial batch is flushed. Default 2 s.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Streaming sink only: how long a request waits for its response before it is exported as an orphan
    /// (a zero-duration span marked <c>kronikol.orphan = true</c>). Default 30 s.
    /// </summary>
    public TimeSpan PendingRequestTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Streaming sink only: how long <see cref="OtlpExportSink.DisposeAsync"/> lets the worker drain
    /// before cancelling the in-flight POST — deterministic test-end export even against a hung
    /// collector. Default 5 s.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gzip the POSTed payload (<c>Content-Encoding: gzip</c>). Default false.</summary>
    public bool Gzip { get; set; }

    /// <summary>Optional diagnostic log callback (batch failures, drops, sink start/stop).</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Optional human-readable name of this exporter for diagnostics. Default <c>otlp-export</c>.</summary>
    public string? Name { get; set; }

    internal string DisplayName => Name ?? "otlp-export";

    /// <summary>Throws when the options cannot run an exporter.</summary>
    public void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri)
            throw new ArgumentException("Endpoint must be an absolute URI such as http://localhost:4318/v1/traces.", nameof(Endpoint));
        if (Endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException("Endpoint must use http or https.", nameof(Endpoint));
        if (BodyAttributeCapBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(BodyAttributeCapBytes), "BodyAttributeCapBytes must be positive.");
        if (QueueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(QueueCapacity), "QueueCapacity must be positive.");
        if (BatchMaxSpans <= 0)
            throw new ArgumentOutOfRangeException(nameof(BatchMaxSpans), "BatchMaxSpans must be positive.");
        if (FlushInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(FlushInterval), "FlushInterval must be positive.");
        if (PendingRequestTtl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PendingRequestTtl), "PendingRequestTtl must be positive.");
        if (ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout), "ShutdownTimeout must be positive.");
    }
}
