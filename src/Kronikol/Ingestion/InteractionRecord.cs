using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kronikol.Tracking;

namespace Kronikol.Ingestion;

/// <summary>
/// One line of the language-neutral NDJSON capture format: a single tracked request <em>or</em> response,
/// shaped exactly like the <c>httpInteraction</c> objects Kronikol already publishes in
/// <c>TestRunReport.json</c> (same property names), plus the attribution fields a capturer outside the
/// test process needs to supply (<see cref="TestId"/>, <see cref="TestName"/>) and a few optional extras.
/// Any capturer in any language that can write one JSON object per line can feed Kronikol via
/// <c>kronikol ingest</c> or <see cref="IngestPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Property names are camelCase on the wire. Required: <c>type</c>, <c>uri</c>, <c>serviceName</c>,
/// <c>callerName</c>, <c>testId</c>. A request and its response are paired by <c>requestResponseId</c>
/// (any string — a UUID is conventional; non-UUID values are hashed to a stable <see cref="Guid"/>).
/// <c>traceId</c> groups the calls of one chain (defaults to <c>requestResponseId</c> when absent).</para>
/// <para>Unknown properties are ignored, so capturers may add their own diagnostics; Kronikol's
/// diagram note shows <c>content</c> and <c>headers</c>, so fold anything you want rendered into those.</para>
/// </remarks>
public sealed record InteractionRecord
{
    /// <summary>The serializer options used on the wire: camelCase, nulls omitted, enums as strings, case-insensitive reads.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary><c>"Request"</c> or <c>"Response"</c>.</summary>
    [JsonPropertyName("type")] public required string Type { get; init; }

    /// <summary>HTTP method (<c>GET</c>, <c>POST</c>, …) or any custom operation label (<c>Query</c>, <c>generate [gemma]</c>).</summary>
    [JsonPropertyName("method")] public string? Method { get; init; }

    /// <summary>The full URI of the call (arrow label uses the path and query).</summary>
    [JsonPropertyName("uri")] public required string Uri { get; init; }

    /// <summary>The receiving participant.</summary>
    [JsonPropertyName("serviceName")] public required string ServiceName { get; init; }

    /// <summary>The calling participant.</summary>
    [JsonPropertyName("callerName")] public required string CallerName { get; init; }

    /// <summary>Request or response body (already decoded/capped by the capturer).</summary>
    [JsonPropertyName("content")] public string? Content { get; init; }

    /// <summary>Request or response headers. Secrets must be redacted by the capturer or by <see cref="RequestResponseLogger.Redaction"/> at ingest.</summary>
    [JsonPropertyName("headers")] public InteractionHeader[]? Headers { get; init; }

    /// <summary>Response status — an HTTP status number as text (<c>"200"</c>) or a custom label. Null on requests.</summary>
    [JsonPropertyName("statusCode")] public string? StatusCode { get; init; }

    /// <summary>Chain/trace correlation id shared by every hop of one call chain. Defaults to <see cref="RequestResponseId"/>.</summary>
    [JsonPropertyName("traceId")] public string? TraceId { get; init; }

    /// <summary>Pairs a request with its response. Required for a pair to render as one arrow.</summary>
    [JsonPropertyName("requestResponseId")] public string? RequestResponseId { get; init; }

    /// <summary>When the request was sent / the response received (ISO-8601). Used for ordering and loop durations.</summary>
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; init; }

    /// <summary>The scenario this call belongs to — must equal <c>Scenario.Id</c> byte-for-byte. For browser-driven E2E runs this is conveniently the test's W3C trace id.</summary>
    [JsonPropertyName("testId")] public required string TestId { get; init; }

    /// <summary>Display name of the test (cosmetic; the tests file wins when both are present).</summary>
    [JsonPropertyName("testName")] public string? TestName { get; init; }

    /// <summary>A <c>Kronikol.Constants.DependencyCategories</c> value (<c>BigQuery</c>, <c>AI</c>, …) for participant shape/colour. Null = plain HTTP service.</summary>
    [JsonPropertyName("dependencyCategory")] public string? DependencyCategory { get; init; }

    /// <summary>Dependency category of the caller, when the caller itself is a dependency (rare).</summary>
    [JsonPropertyName("callerDependencyCategory")] public string? CallerDependencyCategory { get; init; }

    /// <summary><c>Setup</c>, <c>Action</c> or <c>Unknown</c>.</summary>
    [JsonPropertyName("phase")] public string? Phase { get; init; }

    /// <summary><c>Default</c> (request/response) or <c>Event</c> (fire-and-forget styling).</summary>
    [JsonPropertyName("metaType")] public string? MetaType { get; init; }

    /// <summary>W3C trace id of the distributed trace this call belongs to (cross-link to Tempo/Jaeger).</summary>
    [JsonPropertyName("activityTraceId")] public string? ActivityTraceId { get; init; }

    /// <summary>W3C span id of this call.</summary>
    [JsonPropertyName("activitySpanId")] public string? ActivitySpanId { get; init; }

    /// <summary>When true the entry is stored but excluded from diagrams (mirrors the <c>test-tracking-ignore</c> header).</summary>
    [JsonPropertyName("trackingIgnore")] public bool? TrackingIgnore { get; init; }

    /// <summary>Serialises this record as one NDJSON line (no trailing newline).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Parses one NDJSON line.</summary>
    public static InteractionRecord FromJson(string json) =>
        JsonSerializer.Deserialize<InteractionRecord>(json, JsonOptions)
        ?? throw new JsonException("The line did not contain an interaction object.");

    /// <summary>Maps a tracked log entry to its wire representation.</summary>
    public static InteractionRecord FromLog(RequestResponseLog log) => new()
    {
        Type = log.Type.ToString(),
        Method = log.Method.Value?.ToString(),
        Uri = log.Uri.ToString(),
        ServiceName = log.ServiceName,
        CallerName = log.CallerName,
        Content = log.Content,
        Headers = log.Headers.Length == 0 ? null : log.Headers.Select(h => new InteractionHeader(h.Key, h.Value)).ToArray(),
        StatusCode = log.StatusCode?.Value switch
        {
            null => null,
            HttpStatusCode code => ((int)code).ToString(),
            var other => other.ToString(),
        },
        TraceId = log.TraceId.ToString(),
        RequestResponseId = log.RequestResponseId.ToString(),
        Timestamp = log.Timestamp,
        TestId = log.TestId,
        TestName = log.TestName,
        DependencyCategory = log.DependencyCategory,
        CallerDependencyCategory = log.CallerDependencyCategory,
        Phase = log.Phase == TestPhase.Unknown ? null : log.Phase.ToString(),
        MetaType = log.MetaType == RequestResponseMetaType.Default ? null : log.MetaType.ToString(),
        ActivityTraceId = log.ActivityTraceId,
        ActivitySpanId = log.ActivitySpanId,
        TrackingIgnore = log.TrackingIgnore ? true : null,
    };

    /// <summary>
    /// Maps this record to a <see cref="RequestResponseLog"/> ready for <see cref="RequestResponseLogger.Log"/>.
    /// <paramref name="testNameOverride"/> replaces <see cref="TestName"/> (used by the ingest pipeline
    /// to apply the name from the tests file consistently across every hop of one test).
    /// </summary>
    public RequestResponseLog ToLog(string? testNameOverride = null)
    {
        var type = string.Equals(Type, "Response", StringComparison.OrdinalIgnoreCase)
            ? RequestResponseType.Response
            : RequestResponseType.Request;

        var requestResponseId = ToGuid(RequestResponseId ?? $"{TestId}|{Uri}|{Timestamp:O}");
        var traceId = string.IsNullOrEmpty(TraceId) ? requestResponseId : ToGuid(TraceId);

        OneOf<HttpStatusCode, string>? status = null;
        if (!string.IsNullOrWhiteSpace(StatusCode))
        {
            status = int.TryParse(StatusCode, out var numeric)
                ? (HttpStatusCode)numeric
                : StatusCode;
        }

        var phase = Enum.TryParse<TestPhase>(Phase, ignoreCase: true, out var parsedPhase) ? parsedPhase : TestPhase.Unknown;
        var metaType = Enum.TryParse<RequestResponseMetaType>(MetaType, ignoreCase: true, out var parsedMeta) ? parsedMeta : RequestResponseMetaType.Default;

        var uri = System.Uri.TryCreate(Uri, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri("http://unknown" + (Uri.StartsWith('/') ? Uri : "/" + Uri));

        return new RequestResponseLog(
            testNameOverride ?? TestName ?? TestIdentityScope.UnknownTestName,
            TestId,
            ParseMethod(Method),
            Content,
            uri,
            Headers is null ? [] : Headers.Select(h => (h.Key, h.Value)).ToArray(),
            ServiceName,
            CallerName,
            type,
            traceId,
            requestResponseId,
            TrackingIgnore ?? false,
            status,
            metaType,
            DependencyCategory,
            CallerDependencyCategory)
        {
            Timestamp = Timestamp,
            Phase = phase,
            ActivityTraceId = ActivityTraceId,
            ActivitySpanId = ActivitySpanId,
        };
    }

    /// <summary>
    /// Builds the Request/Response pair for one synchronous call — the shape every capturer needs most.
    /// Both records share <paramref name="requestResponseId"/> (and <paramref name="traceId"/>, which
    /// defaults to it).
    /// </summary>
    public static (InteractionRecord Request, InteractionRecord Response) Pair(
        string testId,
        string? testName,
        string method,
        string uri,
        string serviceName,
        string callerName,
        string? requestContent = null,
        string? responseContent = null,
        string? statusCode = null,
        InteractionHeader[]? requestHeaders = null,
        InteractionHeader[]? responseHeaders = null,
        DateTimeOffset? requestTimestamp = null,
        DateTimeOffset? responseTimestamp = null,
        string? requestResponseId = null,
        string? traceId = null,
        string? dependencyCategory = null,
        string? phase = null,
        string? activityTraceId = null,
        string? activitySpanId = null)
    {
        requestResponseId ??= Guid.NewGuid().ToString();
        traceId ??= requestResponseId;
        var request = new InteractionRecord
        {
            Type = "Request",
            Method = method,
            Uri = uri,
            ServiceName = serviceName,
            CallerName = callerName,
            Content = requestContent,
            Headers = requestHeaders,
            TraceId = traceId,
            RequestResponseId = requestResponseId,
            Timestamp = requestTimestamp,
            TestId = testId,
            TestName = testName,
            DependencyCategory = dependencyCategory,
            Phase = phase,
            ActivityTraceId = activityTraceId,
            ActivitySpanId = activitySpanId,
        };
        var response = request with
        {
            Type = "Response",
            Content = responseContent,
            Headers = responseHeaders,
            StatusCode = statusCode,
            Timestamp = responseTimestamp ?? requestTimestamp,
        };
        return (request, response);
    }

    /// <summary>Parses a method label: well-known HTTP verbs become <see cref="HttpMethod"/>, anything else stays a custom string.</summary>
    public static OneOf<HttpMethod, string> ParseMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return "CALL";

        var trimmed = method.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "PATCH" => HttpMethod.Patch,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            "TRACE" => HttpMethod.Trace,
            "CONNECT" => HttpMethod.Connect,
            _ => trimmed,
        };
    }

    /// <summary>Converts any id string to a <see cref="Guid"/>: parsed when it already is one (any format, including 32-hex W3C trace ids), otherwise a stable hash of the text.</summary>
    public static Guid ToGuid(string id)
    {
        if (Guid.TryParse(id, out var guid))
            return guid;
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(id));
        return new Guid(bytes);
    }
}

/// <summary>A header name/value pair on the wire (<c>{ "key": "...", "value": "..." }</c>).</summary>
public sealed record InteractionHeader(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string? Value);
