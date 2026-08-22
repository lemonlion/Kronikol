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

    /// <summary>
    /// What this record is. Absent/<c>interaction</c> = a request or response (the default).
    /// <c>ui</c> = a user action on the system (a browser click, navigation, keystroke…): renders as a single
    /// one-way arrow from <see cref="CallerName"/> (an actor, normally "User") to <see cref="ServiceName"/>
    /// labelled <see cref="Method"/>, with <see cref="Content"/> as its note; <see cref="DurationMs"/> is the
    /// span during which the calls it caused happened (they nest under it in call-tree order).
    /// <c>step</c> / <c>assertion</c> = diagram markers — a step delimiter bar or a ✓/✗ assertion note at
    /// <see cref="Timestamp"/> (see <see cref="Text"/>, <see cref="Keyword"/>, <see cref="Passed"/>, <see cref="Message"/>).
    /// The tests NDJSON (<see cref="TestRunRecord"/>) is the usual source of markers; this lets an interaction
    /// capturer emit them too.
    /// </summary>
    [JsonPropertyName("kind")] public string? Kind { get; init; }

    /// <summary>How long this record's activity lasted (ms). For <c>ui</c> records: the interval the action owns — calls starting inside it nest under it.</summary>
    [JsonPropertyName("durationMs")] public double? DurationMs { get; init; }

    /// <summary>Markers: the step text or the assertion text.</summary>
    [JsonPropertyName("text")] public string? Text { get; init; }

    /// <summary>Step markers: optional Given/When/Then keyword shown before the text.</summary>
    [JsonPropertyName("keyword")] public string? Keyword { get; init; }

    /// <summary>Assertion markers: whether it passed (default <c>true</c>).</summary>
    [JsonPropertyName("passed")] public bool? Passed { get; init; }

    /// <summary>Assertion markers: the failure message shown under a failed assertion.</summary>
    [JsonPropertyName("message")] public string? Message { get; init; }

    /// <summary>Record kinds (<see cref="Kind"/>).</summary>
    public static class Kinds
    {
        public const string Interaction = "interaction";
        public const string Ui = "ui";
        public const string Step = "step";
        public const string Assertion = "assertion";
    }

    /// <summary><see cref="Kind"/> is <c>ui</c>.</summary>
    [JsonIgnore] public bool IsUserAction => string.Equals(Kind, Kinds.Ui, StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="Kind"/> is <c>step</c> or <c>assertion</c>: a zero-length diagram marker, never a request.</summary>
    [JsonIgnore] public bool IsMarker =>
        string.Equals(Kind, Kinds.Step, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Kind, Kinds.Assertion, StringComparison.OrdinalIgnoreCase);

    /// <summary>W3C trace id of the distributed trace this call belongs to (cross-link to Tempo/Jaeger).</summary>
    [JsonPropertyName("activityTraceId")] public string? ActivityTraceId { get; init; }

    /// <summary>W3C span id of this call.</summary>
    [JsonPropertyName("activitySpanId")] public string? ActivitySpanId { get; init; }

    /// <summary>When true the entry is stored but excluded from diagrams (mirrors the <c>test-tracking-ignore</c> header).</summary>
    [JsonPropertyName("trackingIgnore")] public bool? TrackingIgnore { get; init; }

    /// <summary>
    /// Which capture path produced this record — <c>wire</c> (a proxy/TCP tap that decoded the protocol),
    /// <c>span</c> (an OpenTelemetry receiver), or <c>wire + span</c> once
    /// <see cref="InteractionMerger"/> has folded both views of the same call together. Optional: the merge
    /// falls back to inferring the source from the presence of a span id and a body.
    /// </summary>
    [JsonPropertyName("capturedBy")] public string? CapturedBy { get; init; }

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
        CapturedBy = log.CapturedBy,
        Kind = log.IsUserAction ? Kinds.Ui : null,
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
            // A user action's caller is a person: default it to the User category so it renders as an actor.
            CallerDependencyCategory ?? (IsUserAction ? Constants.DependencyCategories.User : null))
        {
            Timestamp = Timestamp,
            Phase = phase,
            ActivityTraceId = ActivityTraceId,
            ActivitySpanId = ActivitySpanId,
            IsUserAction = IsUserAction,
            CapturedBy = CapturedBy,
        };
    }

    /// <summary>
    /// Maps this record to the log entries it stands for: one <see cref="RequestResponseLog"/> for requests,
    /// responses and user actions; for <c>step</c> / <c>assertion</c> markers, the override pair that injects
    /// the delimiter bar or assertion note into the sequence diagram (the same PlantUML Kronikol's step and
    /// assertion tracking emit, so the report's Show/Hide Steps and Show/Hide Assertions toggles apply).
    /// </summary>
    public IEnumerable<RequestResponseLog> ToLogs(string? testNameOverride = null)
    {
        if (!IsMarker)
        {
            yield return ToLog(testNameOverride);
            yield break;
        }

        var plantUml = string.Equals(Kind, Kinds.Step, StringComparison.OrdinalIgnoreCase)
            ? StepDelimiterPlantUml(Keyword, Text)
            : AssertionNotePlantUml(Text, Passed ?? true, Message);
        var name = testNameOverride ?? TestName ?? TestIdentityScope.UnknownTestName;

        yield return OverrideLog(name, TestId, isStart: true, plantUml);
        yield return OverrideLog(name, TestId, isStart: false, null);
    }

    /// <summary>The step delimiter bar Kronikol's step tracking draws: <c>hnote across &lt;&lt;stepDelimiter&gt;&gt;</c>.</summary>
    public static string StepDelimiterPlantUml(string? keyword, string? text)
    {
        // With a keyword the bar already starts with a capital ("Given the mock is armed") and the
        // author's casing after it is meaningful; without one the text is all the reader sees.
        var label = string.IsNullOrWhiteSpace(keyword)
            ? Reports.StepText.CapitaliseIfEnabled(text) ?? "step"
            : $"{keyword} {text}";
        return $"hnote across <<stepDelimiter>> #black:<color:white>{EscapeNoteLine(label)}";
    }

    /// <summary>The assertion note Kronikol's assertion tracking draws: green ✓ / red ✗ <c>hnote across &lt;&lt;assertionNote&gt;&gt;</c>.</summary>
    public static string AssertionNotePlantUml(string? text, bool passed, string? message)
    {
        var color = passed ? Track.PassColor : Track.FailColor;
        var symbol = passed ? Track.PassSymbol : Track.FailSymbol;
        var body = $"{symbol} {EscapeNoteLine(Reports.StepText.CapitaliseIfEnabled(text) ?? "assertion")}";
        if (!passed && !string.IsNullOrWhiteSpace(message))
            body += "\n" + EscapeNoteLine(message!);
        return $"hnote across <<assertionNote>> {color}\n{body}\nend note";
    }

    private static string EscapeNoteLine(string text) =>
        text.Replace("\r", string.Empty).Replace("\n", "\\n").Trim();

    private RequestResponseLog OverrideLog(string testName, string testId, bool isStart, string? plantUml) =>
        new(testName, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            IsOverrideStart = isStart,
            IsOverrideEnd = !isStart,
            PlantUml = plantUml is null ? null : $"\n{plantUml}\n\n",
            Timestamp = Timestamp,
        };

    /// <summary>A user action (<c>kind: ui</c>): one arrow from <paramref name="callerName"/> (an actor) to <paramref name="serviceName"/>.</summary>
    public static InteractionRecord UserAction(
        string testId, string label, string? pageUrl, DateTimeOffset timestamp, double? durationMs = null,
        string? detail = null, string callerName = "User", string serviceName = "web", string? testName = null) => new()
    {
        Kind = Kinds.Ui,
        Type = "Request",
        Method = label,
        Uri = pageUrl ?? "http://unknown/",
        ServiceName = serviceName,
        CallerName = callerName,
        CallerDependencyCategory = Constants.DependencyCategories.User,
        Content = detail,
        Timestamp = timestamp,
        DurationMs = durationMs,
        TestId = testId,
        TestName = testName,
        RequestResponseId = Guid.NewGuid().ToString("N"),
    };

    /// <summary>A step delimiter marker (<c>kind: step</c>).</summary>
    public static InteractionRecord StepMarker(string testId, string text, DateTimeOffset timestamp, string? keyword = null, string? testName = null) => new()
    {
        Kind = Kinds.Step,
        Type = "Request",
        Uri = "http://override.com/",
        ServiceName = "",
        CallerName = "",
        Text = text,
        Keyword = keyword,
        Timestamp = timestamp,
        TestId = testId,
        TestName = testName,
    };

    /// <summary>An assertion note marker (<c>kind: assertion</c>).</summary>
    public static InteractionRecord AssertionMarker(string testId, string text, bool passed, DateTimeOffset timestamp, string? message = null, string? testName = null) => new()
    {
        Kind = Kinds.Assertion,
        Type = "Request",
        Uri = "http://override.com/",
        ServiceName = "",
        CallerName = "",
        Text = text,
        Passed = passed,
        Message = message,
        Timestamp = timestamp,
        TestId = testId,
        TestName = testName,
    };

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
