using System.Globalization;
using System.Net;
using Kronikol.CallGraph;
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

    /// <summary>The span kind, by OpenTelemetry's messaging table: see <see cref="OtlpSpanMapper.KindOf"/>.</summary>
    public OtlpSpanKind Kind { get; init; } = OtlpSpanKind.Client;

    /// <summary>Start time, nanoseconds since the Unix epoch.</summary>
    public ulong StartTimeUnixNano { get; init; }

    /// <summary>End time, nanoseconds since the Unix epoch.</summary>
    public ulong EndTimeUnixNano { get; init; }

    /// <summary>The <c>service.name</c> resource attribute: the calling participant (<c>CallerName</c>), and on a delivery the consumer.</summary>
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
/// <para><strong>The verdict is the one attribute that does not come from the record.</strong>
/// <c>kronikol.test.result</c> is emitted only when <see cref="OtlpExportOptions.TestResult"/> answers
/// for the span's test id, because a captured call has no outcome at capture time. It is therefore
/// present on a batch export from a host holding the finished run, and absent from a streaming one.</para>
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
        var kind = KindOf(primary);
        // A CONSUMER span is the consumer's own, its peer the broker. TrackConsumeEvent, Kafka and MassTransit log a delivery as the
        // broker's arrow into the consumer, so its caller is the broker: exported as it stands, the broker would read as consuming
        // from the consumer.
        var (self, peer) = kind == OtlpSpanKind.Consumer && ReadingOf(primary).FromTheBroker
            ? (primary.ServiceName, primary.CallerName)
            : (primary.CallerName, primary.ServiceName);
        var attributes = new List<OtlpExportAttribute>(17)
        {
            OtlpExportAttribute.Str("url.full", primary.Uri.ToString()),
        };

        // Only on the web: through NDJSON a label that reads as a verb is an HttpMethod on any scheme (a Cosmos DB Delete, a SQL DELETE).
        if (primary.Method.Value is HttpMethod httpMethod && IsOnTheWeb(primary))
            attributes.Add(OtlpExportAttribute.Str("http.request.method", httpMethod.Method));
        if (statusText is not null)
            attributes.Add(OtlpExportAttribute.Int64("http.response.status_code", long.Parse(statusText, CultureInfo.InvariantCulture)));
        if (DbSystemFor(primary.DependencyCategory) is { } dbSystem)
            attributes.Add(OtlpExportAttribute.Str("db.system.name", dbSystem));
        AddMessagingAttributes(attributes, primary);
        attributes.Add(OtlpExportAttribute.Str("peer.service", peer));
        attributes.Add(OtlpExportAttribute.Str("kronikol.test.id", primary.TestId));
        if (!string.IsNullOrEmpty(primary.TestName))
            attributes.Add(OtlpExportAttribute.Str("kronikol.test.name", primary.TestName));
        // Beside the identity it qualifies. Unlike every other kronikol.* attribute this one does not
        // come from the record - see OtlpExportOptions.TestResult for why it cannot.
        if (options.TestResult?.Invoke(primary.TestId) is { Length: > 0 } testResult)
            attributes.Add(OtlpExportAttribute.Str("kronikol.test.result", testResult));
        if (primary.Phase != TestPhase.Unknown)
            attributes.Add(OtlpExportAttribute.Str("kronikol.phase", primary.Phase.ToString()));
        if (!string.IsNullOrEmpty(primary.DependencyCategory))
            attributes.Add(OtlpExportAttribute.Str("kronikol.dependency.category", primary.DependencyCategory));
        if (!string.IsNullOrEmpty(primary.CapturedBy))
            attributes.Add(OtlpExportAttribute.Str("kronikol.captured.by", primary.CapturedBy));
        // What answered the call in its service's place (#190), from either half: a capture may mark only the response.
        if ((primary.TestDouble ?? response?.TestDouble) is { Length: > 0 } testDouble)
            attributes.Add(OtlpExportAttribute.Str("kronikol.double", testDouble));

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
            Kind = kind,
            StartTimeUnixNano = startNano,
            EndTimeUnixNano = endNano,
            ResourceServiceName = self,
            Status = status,
            StatusMessage = statusMessage,
            Attributes = attributes,
        };
    }

    /// <summary>
    /// The span kind of a record, by OpenTelemetry's tables (semantic conventions v1.44.0): the exporter's reading of the record, which
    /// keeps the <see cref="RequestResponseLog.MetaType"/> it was written with. In order:
    /// <list type="number">
    /// <item>Not an <see cref="RequestResponseMetaType.Event"/>: <c>CLIENT</c>.</item>
    /// <item>A statement-shaped category written as an event (a consumer's own, a capture from before the database trackers moved):
    /// <c>CLIENT</c>, since a database span "SHOULD be CLIENT", and it carries no <c>messaging.*</c>.</item>
    /// <item>An HTTP verb on a web address, the request itself (SQS, SNS, Storage Queues and EventBridge at Raw): <c>CLIENT</c>, since an
    /// HTTP span "MUST be CLIENT", and the label cannot separate a send from a receive there.</item>
    /// <item><see cref="ReadingOf"/>, of how the record was logged: a delivery <c>CONSUMER</c>; a pull, a settlement or a management
    /// call <c>CLIENT</c>. A send, or a record that says nothing of itself, is <c>PRODUCER</c> only "if the context of the Send span is
    /// used as creation context, otherwise CLIENT": only Kafka's tracker puts one on the message
    /// (<c>KafkaProduceTracking.ToSend</c>), so Kafka's produce is <c>PRODUCER</c> and every other send <c>CLIENT</c>.</item>
    /// </list>
    /// </summary>
    internal static OtlpSpanKind KindOf(RequestResponseLog log)
    {
        if (log.MetaType != RequestResponseMetaType.Event)
            return OtlpSpanKind.Client;
        if (DependencyCategories.IsStatementShaped(log.DependencyCategory))
            return OtlpSpanKind.Client;
        if (IsHttpRequest(log))
            return OtlpSpanKind.Client;

        return ReadingOf(log).Verdict switch
        {
            MessageLabelKind.Delivers => OtlpSpanKind.Consumer,
            MessageLabelKind.Receives or MessageLabelKind.Neither => OtlpSpanKind.Client,
            _ => IsKafkasProduce(log) ? OtlpSpanKind.Producer : OtlpSpanKind.Client,
        };
    }

    private static bool IsKafkasProduce(RequestResponseLog log)
    {
        var scheme = MessageTable.Scheme(log.Uri.ToString());
        return !log.LabelIsFree && scheme == "kafka" && MessageTable.Read(scheme, log.Method.Value?.ToString()) == MessageLabelKind.Publishes;
    }

    /// <summary>
    /// What a record is, and whether it was logged as the broker's arrow into the consumer, from what says so, in order:
    /// <list type="number">
    /// <item><see cref="RequestResponseLog.NoteOnRight"/>, which <c>TrackConsumeEvent</c> sets, as a hand call of <c>TrackMessageRequest</c> may: a delivery from the broker, on any
    /// scheme and under any label.</item>
    /// <item><see cref="RequestResponseLog.LabelIsFree"/>: the rest of <c>MessageTracker</c>. Its label is its author's words, so it
    /// says nothing, whatever it reads.</item>
    /// <item>The row of the record's scheme, for what a tracker writes. Kafka and MassTransit log a delivery from the broker, Pub/Sub's
    /// subscriber and Event Hubs' <c>Process</c> from the consumer.</item>
    /// <item>Only where neither mark can exist, a record read back from NDJSON or one a span made: the label's first word. A delivery
    /// read so is the service's own by the ingest contract (<c>serviceName</c> is the receiver), unless a span saw it.</item>
    /// </list>
    /// A span's own record is drawn from the service that sent the span, and a wire capturer's from the client of the connection, so
    /// neither is the broker's arrow, whatever the span's kind.
    /// </summary>
    private static (MessageLabelKind Verdict, bool FromTheBroker) ReadingOf(RequestResponseLog log)
    {
        if (log.NoteOnRight)
            return (MessageLabelKind.Delivers, true);
        if (log.LabelIsFree)
            return (MessageLabelKind.Unknown, false);

        var uri = log.Uri.ToString();
        var label = log.Method.Value?.ToString();
        var fromASpan = string.Equals(log.CapturedBy, InteractionMerger.SpanSource, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(log.CapturedBy, InteractionMerger.MergedSource, StringComparison.OrdinalIgnoreCase);
        var drawnFromItsCaller = fromASpan || string.Equals(log.CapturedBy, InteractionMerger.WireSource, StringComparison.OrdinalIgnoreCase);

        var row = MessageTable.ReadTracked(uri, label);
        if (row != MessageLabelKind.Unknown)
            return (row, row == MessageLabelKind.Delivers && !drawnFromItsCaller && !MessageTable.DeliveryIsFromTheConsumer(MessageTable.Scheme(uri)));

        if (!log.ReadFromRecord && !fromASpan)
            return (MessageLabelKind.Unknown, false);

        var word = MessageTable.ReadWord(label);
        return (word, word == MessageLabelKind.Delivers && !drawnFromItsCaller);
    }

    private static bool IsOnTheWeb(RequestResponseLog log) => MessageTable.Scheme(log.Uri.ToString()) is "http" or "https";

    private static bool IsHttpRequest(RequestResponseLog log) => log.Method.Value is HttpMethod && IsOnTheWeb(log);

    /// <summary>
    /// The attributes the messaging conventions require of a span, or none on a call on no broker's address: the span's kind says
    /// nothing to a reader without <c>messaging.system</c>, and the tap drops a <c>PRODUCER</c> that has none. Names are v1.44.0's
    /// well-known values; a broker with none (<c>storagequeue</c>, <c>masstransit</c>, <c>eventbridge</c>, a consumer's own scheme)
    /// keeps a name of its own, which the conventions allow.
    /// </summary>
    private static void AddMessagingAttributes(List<OtlpExportAttribute> attributes, RequestResponseLog log)
    {
        var uri = log.Uri.ToString();
        var scheme = MessageTable.Scheme(uri);
        if (scheme is null || IsHttpRequest(log) || DependencyCategories.IsStatementShaped(log.DependencyCategory)
            || (log.MetaType != RequestResponseMetaType.Event && !MessageTable.HasScheme(scheme)))
            return;

        var verdict = ReadingOf(log).Verdict;
        var type = verdict switch
        {
            MessageLabelKind.Publishes => "send",
            MessageLabelKind.Receives => "receive",
            MessageLabelKind.Delivers => "process",
            MessageLabelKind.Unknown when log.MetaType == RequestResponseMetaType.Event => "send",
            _ => null,
        };
        attributes.Add(OtlpExportAttribute.Str("messaging.system", scheme switch
        {
            // The second name of each is the one a span from the tap comes back with, its system name as a scheme.
            "pubsub" or "gcp-pubsub" => "gcp_pubsub",
            "sqs" or "aws-sqs" => "aws_sqs",
            "sns" or "aws-sns" => "aws.sns",
            // A consumer's own message on a web address: the scheme names no messaging system.
            "http" or "https" => "other",
            _ => scheme,
        }));
        attributes.Add(OtlpExportAttribute.Str("messaging.operation.name", OperationNameOf(log, verdict, type)));
        if (type is not null)
            attributes.Add(OtlpExportAttribute.Str("messaging.operation.type", type));
        // A span's own record is {system}://{broker}/{destination}, wherever that scheme's tracker puts its destination.
        var fromASpan = string.Equals(log.CapturedBy, InteractionMerger.SpanSource, StringComparison.OrdinalIgnoreCase);
        if ((fromASpan ? MessageTable.LastSegment(uri) : MessageTable.Destination(uri)) is { } destination)
            attributes.Add(OtlpExportAttribute.Str("messaging.destination.name", destination));
    }

    // The label's first word where the label names an operation; a free-text label ("Kafka") or an arrow ("← OrderPlaced") does not.
    private static string OperationNameOf(RequestResponseLog log, MessageLabelKind verdict, string? type)
    {
        var label = (log.Method.Value?.ToString() ?? "").Trim();
        var end = label.IndexOfAny([' ', '(', '[', '\t']);
        var word = end < 0 ? label : label[..end];
        // MassTransit's "Consume Fault OrderPlaced" and Kafka's "Send Offsets": an operation of two words, as each enum names it.
        var second = end < 0 ? "" : label[(end + 1)..].Split(' ', 2)[0];
        if (second is "Fault" or "Offsets")
            word += second;
        var named = !log.LabelIsFree && verdict != MessageLabelKind.Unknown;
        return named && word.Length > 0 && char.IsLetter(word[0]) ? word : type ?? (word.Length > 0 ? word : "call");
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
    /// semconv rule), as is the string form of a failure (<c>Error</c>, <c>Failed</c>, <c>Timeout</c>…), and a call that
    /// threw, whose status is the exception's type behind a bang (<c>!HttpRequestException</c>, 3.18.0), with the log's
    /// <c>Error</c> as the message when it has one (from 4.3.0; it was exported Unset).
    /// Returns the numeric text (for <c>http.response.status_code</c>) and the status message to export.
    /// </summary>
    internal static (OtlpStatusCode Status, string? NumericText, string? Message) StatusOf(RequestResponseLog log)
    {
        switch (log.StatusCode?.Value)
        {
            case null:
                return (OtlpStatusCode.Unset, null, null);
            case HttpStatusCode code:
                return (int)code >= 400
                    ? (OtlpStatusCode.Error, ((int)code).ToString(CultureInfo.InvariantCulture), ServiceMessage(log))
                    : (OtlpStatusCode.Unset, ((int)code).ToString(CultureInfo.InvariantCulture), null);
            case var value:
            {
                var text = value.ToString() ?? "";
                if (text.StartsWith('!'))
                    return (OtlpStatusCode.Error, null, string.IsNullOrWhiteSpace(log.Error) ? text : log.Error);
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                    return numeric >= 400
                        ? (OtlpStatusCode.Error, numeric.ToString(CultureInfo.InvariantCulture), ServiceMessage(log))
                        : (OtlpStatusCode.Unset, numeric.ToString(CultureInfo.InvariantCulture), null);
                // A failure's message is its reason where it has one, not its status word (plans/FAILED_CALLS_PLAN.md F21), and a
                // reason beside a word the list does not name is a failure, as every reader of a call's outcome reads it (§5.4).
                if (ServiceMessage(log) is { } reason)
                    return (OtlpStatusCode.Error, null, reason);
                return IsFailureText(text) ? (OtlpStatusCode.Error, null, text) : (OtlpStatusCode.Unset, null, null);
            }
        }
    }

    /// <summary>Why a call that answered with a failure failed, when its capturer read the service's own words (a MongoDB errmsg).</summary>
    private static string? ServiceMessage(RequestResponseLog log) => string.IsNullOrWhiteSpace(log.Error) ? null : log.Error;

    private static bool IsFailureText(string status) =>
        status.Trim().ToLowerInvariant() is "error" or "failed" or "failure" or "fault" or "timeout" or "exception";

    /// <summary>
    /// The reverse of <see cref="SpanToInteractionMapper"/>'s category mapping: the <c>db.system.name</c>
    /// to export for a <see cref="DependencyCategories"/> value, or null when the category is not a
    /// database (the attribute is omitted). Every value round-trips: importing what this exports yields
    /// the original category (generic <c>Database</c>/<c>SQL</c> export as semconv <c>other_sql</c>). The names
    /// are the registry's well-known values at semantic conventions v1.44.0, which "MUST be used" where one
    /// applies; BigQuery and Bigtable have none, so each keeps a name of its own, which the registry allows.
    /// </summary>
    internal static string? DbSystemFor(string? dependencyCategory) => dependencyCategory switch
    {
        DependencyCategories.Redis => "redis",
        DependencyCategories.MongoDB => "mongodb",
        DependencyCategories.BigQuery => "bigquery",
        DependencyCategories.PostgreSQL => "postgresql",
        DependencyCategories.MySQL => "mysql",
        DependencyCategories.SqlServer => "microsoft.sql_server",
        DependencyCategories.SQLite => "sqlite",
        DependencyCategories.Oracle => "oracle.db",
        DependencyCategories.Elasticsearch => "elasticsearch",
        DependencyCategories.CosmosDB => "azure.cosmosdb",
        DependencyCategories.DynamoDB => "aws.dynamodb",
        DependencyCategories.Spanner => "gcp.spanner",
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
