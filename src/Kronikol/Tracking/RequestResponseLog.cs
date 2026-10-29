using System.Net;

namespace Kronikol.Tracking;

/// <summary>
/// A single tracked interaction (request or response) captured during test execution.
/// Pairs of entries sharing the same <see cref="TraceId"/> and <see cref="RequestResponseId"/>
/// form a request/response pair that produces one arrow in sequence diagrams.
/// </summary>
public record RequestResponseLog(
    string TestName,
    string TestId,
    OneOf<HttpMethod, string> Method,
    string? Content,
    Uri Uri,
    (string Key, string? Value)[] Headers,
    string ServiceName,
    string CallerName,
    RequestResponseType Type,
    Guid TraceId,
    Guid RequestResponseId,
    bool TrackingIgnore,
    OneOf<HttpStatusCode, string>? StatusCode = null,
    RequestResponseMetaType MetaType = default,
    string? DependencyCategory = null,
    string? CallerDependencyCategory = null)
{
    /// <summary>
    /// Whether <see cref="ServiceName"/> is the HTTP handler's fallback for a remote host, <c>{host}:{port}</c>, which
    /// before #190 was <c>localhost:{port}</c>. History's rule 6 reads the call under that name, so a run can be compared
    /// with runs fingerprinted before the change (<see cref="Kronikol.History.InteractionShape"/>).
    /// Set by the handler alone and never written out.
    /// </summary>
    internal bool NamedByRemoteHostFallback { get; init; }

    /// <summary>
    /// The call as its tracker recorded it before #192 R1 changed how it records it, when that differs: a SQL Server
    /// batch opening with two SET statements, which the classifier read as neither a statement nor a table. History's
    /// rules 6 and 7 read its method and URI from here, and no call at all when the tracker did not record it then, so a
    /// run still compares with runs fingerprinted under them (<see cref="Kronikol.History.InteractionShape"/>). Null for
    /// every call recorded as before. Set by the trackers alone and never written out.
    /// </summary>
    internal RecordedBefore? RecordedBefore8 { get; init; }

    /// <summary>
    /// The call as its tracker recorded it before #125 changed how the BigQuery, Cloud Storage, Blob Storage and Storage
    /// Queues handlers name a call, when that differs: a call against an emulator or a gateway, once <c>Other</c>, or a
    /// call <c>Summarised</c> did not record. History's rules 6 to 8 read its method and URI from here, and no call at all
    /// when the tracker did not record it then; on a response, <see cref="RecordedBefore.Absent"/> says the tracker
    /// recorded no response then (a send that threw, #125's Q5). Null for every call recorded as before. Set by the
    /// trackers alone and never written out.
    /// </summary>
    internal RecordedBefore? RecordedBefore9 { get; init; }

    /// <summary>
    /// The call as its tracker recorded it before F26 (plans/CLIENT_FLOORS_PLAN.md) changed where an Oracle call is drawn,
    /// when that differs: the server and the service its data source names, where 9 and earlier recorded <c>localhost</c>
    /// and a database called <c>unknown</c>. History's rules 6 to 9 read its method and URI from here. Never
    /// <see cref="RecordedBefore.Absent"/>, as the change moved no call in or out. Null for every call recorded as before.
    /// Set by the trackers alone and never written out.
    /// </summary>
    internal RecordedBefore? RecordedBefore10 { get; init; }

    public bool NoteOnRight { get; set; }
    public bool IsOverrideStart { get; set; }
    public bool IsOverrideEnd { get; set; }
    public bool IsActionStart { get; set; }

    /// <summary>
    /// True for the control records that carry no interaction of their own: the override start/end pair
    /// that splices <see cref="PlantUml"/> into a sequence diagram (Gherkin step delimiters, assertion
    /// notes, custom fragments) and the Setup/Action boundary marker. They travel in the same log stream
    /// as real traffic, so every consumer that reports interactions has to skip them.
    /// </summary>
    public bool IsDiagramMarker => IsOverrideStart || IsOverrideEnd || IsActionStart;

    /// <summary>
    /// What a marker record stands for. Set by whoever emits the marker — the step collector, the
    /// assertion tracker, the tabular-input enumerator — because classifying at the source is exact,
    /// whereas recovering the same answer at the sink means pattern-matching PlantUML. Meaningless on a
    /// record that is not a marker.
    /// </summary>
    public DiagramMarkerKind MarkerKind { get; set; }

    public string? PlantUml { get; set; }
    public string[]? FocusFields { get; set; }
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>How the call got its scenario; null when the capture path predates the mark.</summary>
    public AttributionSource? AttributionSource { get; set; }

    /// <summary>
    /// When <see cref="AttributionSource"/> is <see cref="Tracking.AttributionSource.Expired"/>: the scenario
    /// whose context the call inherited after that scenario had ended.
    /// </summary>
    public string? ExpiredFromTestId { get; set; }

    /// <summary>
    /// Why the call failed, when its capturer knows. For a call that threw instead of answering: the exception's
    /// message chain (the outer message, then each inner one after "Caused by:"), with the exception's type standing
    /// where the status would, behind a bang (<c>!HttpRequestException</c>), so the fingerprint and the diagram name
    /// it; see <see cref="FailedSend"/>. For a call that answered with a failure its capturer can read, such as a
    /// MongoDB command error or write error or a Kafka delivery error: the service's own message, beside the failure
    /// status. Null on a request and on a call that succeeded.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// The W3C span id this record was logged under, lowercase hex: the span current when the tracker saw the call, which
    /// internal flow takes as the call's own span, so a tracker records the span of the call itself or none. On the
    /// response of a call <see cref="TestTrackingMessageHandler"/> sends over a socket, the span its transport started
    /// for the call, the one it sent in the call's <c>traceparent</c> (from 4.26.5). Null when the tracker records none,
    /// as most database and SDK trackers do.
    /// </summary>
    public string? ActivitySpanId { get; set; }

    /// <summary>
    /// The W3C trace id this record was logged under, lowercase hex, which internal flow and <c>kronikol query trace</c>
    /// join on. Null when the tracker records none: the call then takes the spans its scenario's traced calls could take
    /// that began in its time, and a client library's span begun a moment before it (from 4.26.7).
    /// </summary>
    public string? ActivityTraceId { get; set; }
    public TestPhase Phase { get; set; }

    /// <summary>
    /// Pre-computed rendering fields for the Setup phase. Populated when phase-specific
    /// verbosity overrides are configured and the phase is unknown at capture time.
    /// </summary>
    public PhaseVariant? SetupVariant { get; set; }

    /// <summary>
    /// Pre-computed rendering fields for the Action phase. Populated when phase-specific
    /// verbosity overrides are configured and the phase is unknown at capture time.
    /// </summary>
    public PhaseVariant? ActionVariant { get; set; }

    /// <summary>
    /// When greater than one, this entry stands for a run of that many consecutive identical calls
    /// that were collapsed into one (see <c>ReportConfigurationOptions.CollapseConsecutiveIdenticalCalls</c>).
    /// Set by the diagram pipeline, never by capturers.
    /// </summary>
    public int CollapsedCount { get; set; }

    /// <summary>Human-readable summary of a collapsed run (e.g. <c>12–48 ms</c>), rendered in the loop label.</summary>
    public string? CollapsedSummary { get; set; }

    /// <summary>
    /// This entry is a user action (a UI interaction such as "Click "Accept trial"") rather than a
    /// request: it renders as a single one-way arrow from the caller (an actor) to the service, with no
    /// response arrow. <see cref="Method"/> carries the label; <see cref="Content"/> the detail note.
    /// </summary>
    public bool IsUserAction { get; set; }

    /// <summary>
    /// Which capture path produced this entry, when the capturer says: <c>span</c> from Kronikol's OpenTelemetry
    /// receiver, <c>wire</c> from an external capturer that decoded the protocol, and <c>wire + span</c> once
    /// <c>Kronikol.Ingestion.InteractionMerger</c> has folded the two views of one call into a single arrow. Kronikol's own
    /// taps leave it unset; the merge works out which side a record is from its span id and its body.
    /// </summary>
    public string? CapturedBy { get; set; }

    /// <summary>
    /// How long the call took, when the capturer measured it rather than leaving it to be inferred. Report
    /// generation normally derives duration from the request and response timestamps; a capturer that sends
    /// one record for a whole call — the NDJSON ingest contract allows it — has no second timestamp to
    /// derive from, and this is where its measurement lands. Set, it wins over the derived value.
    /// </summary>
    public double? DurationMs { get; set; }

    /// <summary>
    /// What answered the call in its service's place, when the capturer saw a test double answer it: <c>MSW</c>,
    /// <c>WireMock</c>, a handler's type name (#190). Null is unknown, never real: a capturer sets it only when it saw
    /// the double, and a call it did not mark may have been answered by either. The tracking handler sets it to the
    /// type name of the handler at the end of its chain when that handler is no transport, a stub's or a mock library's.
    /// </summary>
    public string? TestDouble { get; set; }
};

/// <summary>A call as its tracker recorded it before a change to how it records it: its method and URI, or no call at all.</summary>
internal sealed record RecordedBefore(OneOf<HttpMethod, string> Method, Uri Uri, bool Absent = false)
{
    /// <summary>A call the tracker did not record then.</summary>
    public static RecordedBefore NotRecorded { get; } = new("", new Uri("about:blank"), Absent: true);
}

/// <summary>
/// Pre-computed rendering fields for a specific test phase (Setup or Action).
/// Allows the renderer to select the correct verbosity variant without knowing
/// extension-specific verbosity enums.
/// </summary>
public record PhaseVariant(
    OneOf<HttpMethod, string> Method,
    Uri Uri,
    string? Content,
    (string Key, string? Value)[] Headers,
    bool Skip);

/// <summary>
/// Identifies whether a tracked HTTP message is a request or a response.
/// </summary>
public enum RequestResponseType
{
    /// <summary>An outgoing or incoming request.</summary>
    Request,

    /// <summary>A response to a request.</summary>
    Response
}

/// <summary>
/// What a diagram marker record stands for. The order matters only for readability — values are appended,
/// never renumbered, because they are exported by name in the data files.
/// </summary>
public enum DiagramMarkerKind
{
    /// <summary>
    /// A fragment injected by the caller through <c>DefaultTrackingDiagramOverride</c> with no more
    /// specific classification. The default, so an unclassified marker is never mistaken for a known one.
    /// </summary>
    Custom,

    /// <summary>The bar that opens a Gherkin step. Already structured in the report's <c>steps</c>.</summary>
    Step,

    /// <summary>A tracked assertion's note. Already structured as an assertion sub-step.</summary>
    Assertion,

    /// <summary>The band marking which row of a tabular input the following calls belong to.</summary>
    Row,

    /// <summary>The Setup/Action boundary.</summary>
    Phase,
}

/// <summary>
/// Categorises the interaction style of a tracked request/response pair.
/// </summary>
public enum RequestResponseMetaType
{
    /// <summary>A standard request/response exchange (HTTP call, database query, etc.).</summary>
    Default,

    /// <summary>A fire-and-forget event (e.g. message publish, event send).</summary>
    Event
}