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
    public string? ActivitySpanId { get; set; }
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
    /// Which capture path produced this entry — <c>wire</c> (a proxy/TCP tap that decoded the protocol) or
    /// <c>span</c> (an OpenTelemetry receiver). Optional; used by
    /// <c>Kronikol.Ingestion.InteractionMerger</c> to fold the two views of one call into a single arrow.
    /// </summary>
    public string? CapturedBy { get; set; }

    /// <summary>
    /// How long the call took, when the capturer measured it rather than leaving it to be inferred. Report
    /// generation normally derives duration from the request and response timestamps; a capturer that sends
    /// one record for a whole call — the NDJSON ingest contract allows it — has no second timestamp to
    /// derive from, and this is where its measurement lands. Set, it wins over the derived value.
    /// </summary>
    public double? DurationMs { get; set; }
};

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