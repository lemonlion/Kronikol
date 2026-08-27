using System.Globalization;
using System.Net;
using Kronikol.Constants;
using Kronikol.Ingestion;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Otlp;

/// <summary>How an <see cref="OtlpExportAttribute"/> value is typed on the wire.</summary>
public enum OtlpAttributeValueKind
{
    /// <summary>An OTLP <c>stringValue</c>.</summary>
    String = 0,

    /// <summary>An OTLP <c>intValue</c> (int64, encoded as a decimal string in OTLP/JSON).</summary>
    Int = 1,

    /// <summary>An OTLP <c>boolValue</c>.</summary>
    Bool = 2,
}

/// <summary>One span attribute to export: a key and a typed value (canonical text form).</summary>
public readonly record struct OtlpExportAttribute(string Key, string Value, OtlpAttributeValueKind Kind)
{
    /// <summary>A string attribute.</summary>
    public static OtlpExportAttribute Str(string key, string value) => new(key, value, OtlpAttributeValueKind.String);

    /// <summary>An int64 attribute.</summary>
    public static OtlpExportAttribute Int64(string key, long value) => new(key, value.ToString(CultureInfo.InvariantCulture), OtlpAttributeValueKind.Int);

    /// <summary>A boolean attribute.</summary>
    public static OtlpExportAttribute Boolean(string key, bool value) => new(key, value ? "true" : "false", OtlpAttributeValueKind.Bool);
}

/// <summary>
/// One Kronikol call ready to leave as an OpenTelemetry span: W3C identity, timing, kind, status and the
/// attributes of the mapping table. Produced by <see cref="OtlpSpanMapper"/>, encoded by
/// <see cref="OtlpJsonEncoder"/>.
/// </summary>
public sealed record OtlpExportSpan
{
    /// <summary>32 lowercase hex characters.</summary>
    public required string TraceId { get; init; }

    /// <summary>16 lowercase hex characters.</summary>
    public required string SpanId { get; init; }

    /// <summary>The span name — the Kronikol method label (<c>GET</c>, <c>Find ← Trial</c>).</summary>
    public required string Name { get; init; }

    /// <summary>The span kind: <see cref="OtlpSpanKind.Client"/>, or <see cref="OtlpSpanKind.Producer"/> for events.</summary>
    public OtlpSpanKind Kind { get; init; } = OtlpSpanKind.Client;

    /// <summary>Start time, nanoseconds since the Unix epoch.</summary>
    public ulong StartTimeUnixNano { get; init; }

    /// <summary>End time, nanoseconds since the Unix epoch.</summary>
    public ulong EndTimeUnixNano { get; init; }

    /// <summary>The <c>service.name</c> resource attribute — the calling participant (<c>CallerName</c>).</summary>
    public required string ResourceServiceName { get; init; }

    /// <summary>The span status.</summary>
    public OtlpStatusCode Status { get; init; }

    /// <summary>The span status message (set when a non-numeric failure status is exported).</summary>
    public string? StatusMessage { get; init; }

    /// <summary>The span attributes, in the encoder's deterministic order.</summary>
    public IReadOnlyList<OtlpExportAttribute> Attributes { get; init; } = [];

    /// <summary>The value of the attribute named <paramref name="key"/>, or null when absent.</summary>
    public string? Attribute(string key)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute.Key == key)
                return attribute.Value;
        }

        return null;
    }
}

/// <summary>What a batch mapping produced: the spans plus the bookkeeping the caller reports.</summary>
/// <param name="Spans">The spans to export, in input order.</param>
/// <param name="SkippedRecords">Records not exported: diagram markers, <c>TrackingIgnore</c> entries, and (unless opted in) span-sourced echoes.</param>
/// <param name="OrphanSpans">Spans exported without their other half (zero-duration, <c>kronikol.orphan = true</c>).</param>
public sealed record OtlpExportBatch(IReadOnlyList<OtlpExportSpan> Spans, int SkippedRecords, int OrphanSpans);

/// <summary>
/// Turns captured <see cref="RequestResponseLog"/> pairs into <see cref="OtlpExportSpan"/>s — the pure
/// half of OTLP export (no I/O). One request/response pair becomes one span; the request supplies the
/// start time and request attributes, the response the end time and status.
/// </summary>
/// <remarks>
/// <para><strong>Identity preserves D4.</strong> A captured <c>ActivityTraceId</c>/<c>ActivitySpanId</c>
/// always wins, so exported spans land in the same distributed trace the observed system emitted. Only
/// when no Activity id was captured is an id derived — see <see cref="TraceIdStrategy"/> for why the
/// default groups by test rather than keeping the per-pair Guid.</para>
/// <para><strong>Flat traces.</strong> No <c>parentSpanId</c> is emitted: exported traces are a flat fan
/// of spans. Inferring parent/child from caller-name chains and interval nesting is a possible future
/// enhancement, not a bug.</para>
/// <para><strong>Echo suppression.</strong> Records captured from the backend's own telemetry
/// (<c>CapturedBy</c> = <c>span</c> or <c>wire + span</c>) are skipped unless
/// <see cref="OtlpExportOptions.IncludeSpanSourced"/> — re-exporting them would duplicate spans the
/// backend already stores. Diagram markers and <c>TrackingIgnore</c> records are always skipped.</para>
/// </remarks>
public static class OtlpSpanMapper
{
    /// <summary>
    /// Maps a full capture: pairs records by (<c>TraceId</c>, <c>RequestResponseId</c>) with opposite
    /// <c>Type</c> (order-independent), skips what must not be exported, and exports unmatched records as
    /// orphan spans — except a lone request with a measured <see cref="RequestResponseLog.DurationMs"/>,
    /// which is a complete one-record call (the NDJSON contract allows it) and becomes a normal span.
    /// </summary>
    /// <param name="logs">The captured records, in any order.</param>
    /// <param name="options">Mapping decisions (strategy, bodies, echo suppression).</param>
    /// <param name="exportTime">Stamped on spans whose records carry no timestamp at all (marked <c>kronikol.times.synthetic</c>).</param>
    public static OtlpExportBatch MapAll(IEnumerable<RequestResponseLog> logs, OtlpExportOptions options, DateTimeOffset exportTime)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(options);

        var spans = new List<OtlpExportSpan>();
        var skipped = 0;
        var orphans = 0;
        var pendingRequests = new Dictionary<(Guid, Guid), RequestResponseLog>();
        var pendingResponses = new Dictionary<(Guid, Guid), RequestResponseLog>();

        foreach (var log in logs)
        {
            if (ShouldSkip(log, options))
            {
                skipped++;
                continue;
            }

            var key = (log.TraceId, log.RequestResponseId);
            if (log.Type == RequestResponseType.Request)
            {
                if (pendingResponses.Remove(key, out var response))
                    spans.Add(Map(log, response, options, exportTime));
                else
                    pendingRequests[key] = log;
            }
            else
            {
                if (pendingRequests.Remove(key, out var request))
                    spans.Add(Map(request, log, options, exportTime));
                else
                    pendingResponses[key] = log;
            }
        }

        foreach (var request in pendingRequests.Values)
        {
            if (request.DurationMs is null)
                orphans++;
            spans.Add(Map(request, null, options, exportTime));
        }

        foreach (var response in pendingResponses.Values)
        {
            orphans++;
            spans.Add(Map(null, response, options, exportTime));
        }

        return new OtlpExportBatch(spans, skipped, orphans);
    }

    /// <summary>
    /// Maps one call. Pass both halves for a paired call; pass a single half for an orphan (zero-duration,
    /// <c>kronikol.orphan = true</c>) or a one-record call with its own <see cref="RequestResponseLog.DurationMs"/>.
    /// </summary>
    public static OtlpExportSpan Map(RequestResponseLog? request, RequestResponseLog? response, OtlpExportOptions options, DateTimeOffset exportTime)
    {
        ArgumentNullException.ThrowIfNull(options);
        var primary = request ?? response ?? throw new ArgumentNullException(nameof(request), "At least one of request/response is required.");

        // Never drop a record over a missing timestamp: borrow the other half's, else stamp export time.
        var start = request?.Timestamp ?? response?.Timestamp;
        var end = response?.Timestamp ?? request?.Timestamp;
        var synthetic = start is null && end is null;
        var startNano = ToUnixNano(start ?? exportTime);
        var endNano = ToUnixNano(end ?? exportTime);
        var isOrphan = request is null || response is null;
        if (request is not null && response is null && request.DurationMs is { } measured)
        {
            // A capturer that sends one record per call measured the duration itself — a complete span.
            isOrphan = false;
            endNano = startNano + (ulong)Math.Max(0, measured * 1_000_000);
        }
        else if (isOrphan)
        {
            endNano = startNano;
        }

        if (endNano < startNano)
            endNano = startNano;

        var (status, statusText, statusMessage) = StatusOf(response ?? primary);
        var attributes = new List<OtlpExportAttribute>(12)
        {
            OtlpExportAttribute.Str("url.full", primary.Uri.ToString()),
        };

        if (primary.Method.Value is HttpMethod httpMethod)
            attributes.Add(OtlpExportAttribute.Str("http.request.method", httpMethod.Method));
        if (statusText is not null)
            attributes.Add(OtlpExportAttribute.Int64("http.response.status_code", long.Parse(statusText, CultureInfo.InvariantCulture)));
        if (DbSystemFor(primary.DependencyCategory) is { } dbSystem)
            attributes.Add(OtlpExportAttribute.Str("db.system.name", dbSystem));
        attributes.Add(OtlpExportAttribute.Str("peer.service", primary.ServiceName));
        attributes.Add(OtlpExportAttribute.Str("kronikol.test.id", primary.TestId));
        if (!string.IsNullOrEmpty(primary.TestName))
            attributes.Add(OtlpExportAttribute.Str("kronikol.test.name", primary.TestName));
        if (primary.Phase != TestPhase.Unknown)
            attributes.Add(OtlpExportAttribute.Str("kronikol.phase", primary.Phase.ToString()));
        if (!string.IsNullOrEmpty(primary.DependencyCategory))
            attributes.Add(OtlpExportAttribute.Str("kronikol.dependency.category", primary.DependencyCategory));
        if (!string.IsNullOrEmpty(primary.CapturedBy))
            attributes.Add(OtlpExportAttribute.Str("kronikol.captured.by", primary.CapturedBy));

        if (options.IncludeBodies)
        {
            if (Cap(request?.Content, options.BodyAttributeCapBytes) is { } requestBody)
                attributes.Add(OtlpExportAttribute.Str("kronikol.request.body", requestBody));
            if (Cap(response?.Content, options.BodyAttributeCapBytes) is { } responseBody)
                attributes.Add(OtlpExportAttribute.Str("kronikol.response.body", responseBody));
        }

        if (isOrphan)
            attributes.Add(OtlpExportAttribute.Boolean("kronikol.orphan", true));
        if (synthetic)
            attributes.Add(OtlpExportAttribute.Boolean("kronikol.times.synthetic", true));

        return new OtlpExportSpan
        {
            TraceId = TraceIdFor(primary, options),
            SpanId = SpanIdFor(primary),
            Name = primary.Method.Value?.ToString() ?? "CALL",
            Kind = primary.MetaType == RequestResponseMetaType.Event ? OtlpSpanKind.Producer : OtlpSpanKind.Client,
            StartTimeUnixNano = startNano,
            EndTimeUnixNano = endNano,
            ResourceServiceName = primary.CallerName,
            Status = status,
            StatusMessage = statusMessage,
            Attributes = attributes,
        };
    }

    /// <summary>
    /// Whether a record must not be exported: diagram markers and <c>TrackingIgnore</c> entries always;
    /// span-sourced echoes (<c>CapturedBy</c> = <c>span</c> or <c>wire + span</c>) unless
    /// <see cref="OtlpExportOptions.IncludeSpanSourced"/>.
    /// </summary>
    public static bool ShouldSkip(RequestResponseLog log, OtlpExportOptions options)
    {
        if (log.IsDiagramMarker || log.TrackingIgnore)
            return true;
        if (options.IncludeSpanSourced || log.CapturedBy is null)
            return false;
        return log.CapturedBy.Equals(InteractionMerger.SpanSource, StringComparison.OrdinalIgnoreCase)
               || log.CapturedBy.Equals(InteractionMerger.MergedSource, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The exported trace id: the captured <c>ActivityTraceId</c> when present (D4 — real ids win), else
    /// derived per <see cref="OtlpExportOptions.TraceIdStrategy"/>.
    /// </summary>
    internal static string TraceIdFor(RequestResponseLog log, OtlpExportOptions options)
    {
        if (NormalisedHex(log.ActivityTraceId, 32) is { } captured)
            return captured;

        if (options.TraceIdStrategy == TraceIdStrategy.PerPair && log.TraceId != Guid.Empty)
            return log.TraceId.ToString("N");

        // Same recipe as InteractionRecord.ToGuid: a 32-hex test id (a browser-minted W3C trace id)
        // maps to itself, anything else hashes deterministically.
        return InteractionRecord.ToGuid(log.TestId).ToString("N");
    }

    /// <summary>The exported span id: the captured <c>ActivitySpanId</c> when present, else the first 16 hex of the pair id.</summary>
    internal static string SpanIdFor(RequestResponseLog log) =>
        NormalisedHex(log.ActivitySpanId, 16) ?? log.RequestResponseId.ToString("N")[..16];

    private static string? NormalisedHex(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length != length || !trimmed.All(Uri.IsHexDigit) || trimmed.All(c => c == '0'))
            return null;
        return trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// The span status for a record's <c>StatusCode</c>: numeric ≥ 400 is an error (the client-span
    /// semconv rule), as is the string form of a failure (<c>Error</c>, <c>Failed</c>, <c>Timeout</c>…).
    /// Returns the numeric text (for <c>http.response.status_code</c>) and the status message to export.
    /// </summary>
    internal static (OtlpStatusCode Status, string? NumericText, string? Message) StatusOf(RequestResponseLog log)
    {
        switch (log.StatusCode?.Value)
        {
            case null:
                return (OtlpStatusCode.Unset, null, null);
            case HttpStatusCode code:
                return ((int)code >= 400 ? OtlpStatusCode.Error : OtlpStatusCode.Unset,
                    ((int)code).ToString(CultureInfo.InvariantCulture), null);
            case var value:
            {
                var text = value.ToString() ?? "";
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                    return (numeric >= 400 ? OtlpStatusCode.Error : OtlpStatusCode.Unset,
                        numeric.ToString(CultureInfo.InvariantCulture), null);
                return IsFailureText(text) ? (OtlpStatusCode.Error, null, text) : (OtlpStatusCode.Unset, null, null);
            }
        }
    }

    private static bool IsFailureText(string status) =>
        status.Trim().ToLowerInvariant() is "error" or "failed" or "failure" or "fault" or "timeout" or "exception";

    /// <summary>
    /// The reverse of <see cref="SpanToInteractionMapper"/>'s category mapping: the <c>db.system.name</c>
    /// to export for a <see cref="DependencyCategories"/> value, or null when the category is not a
    /// database (the attribute is omitted). Every value round-trips: importing what this exports yields
    /// the original category (generic <c>Database</c>/<c>SQL</c> export as semconv <c>other_sql</c>).
    /// </summary>
    internal static string? DbSystemFor(string? dependencyCategory) => dependencyCategory switch
    {
        DependencyCategories.Redis => "redis",
        DependencyCategories.MongoDB => "mongodb",
        DependencyCategories.BigQuery => "bigquery",
        DependencyCategories.PostgreSQL => "postgresql",
        DependencyCategories.MySQL => "mysql",
        DependencyCategories.SqlServer => "mssql",
        DependencyCategories.SQLite => "sqlite",
        DependencyCategories.Oracle => "oracle",
        DependencyCategories.Elasticsearch => "elasticsearch",
        DependencyCategories.CosmosDB => "cosmosdb",
        DependencyCategories.DynamoDB => "dynamodb",
        DependencyCategories.Spanner => "spanner",
        DependencyCategories.Bigtable => "bigtable",
        DependencyCategories.ClickHouse => "clickhouse",
        DependencyCategories.Database or DependencyCategories.SQL => "other_sql",
        _ => null,
    };

    private static ulong ToUnixNano(DateTimeOffset instant)
    {
        var ticks = (instant - DateTimeOffset.UnixEpoch).Ticks;
        return ticks <= 0 ? 0 : (ulong)ticks * 100;
    }

    private static string? Cap(string? value, int cap)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.Length <= cap)
            return value;
        return $"{value[..cap]}\n\n…truncated ({value.Length} chars total)";
    }
}
