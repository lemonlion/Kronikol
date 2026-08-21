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
/// Categorises the interaction style of a tracked request/response pair.
/// </summary>
public enum RequestResponseMetaType
{
    /// <summary>A standard request/response exchange (HTTP call, database query, etc.).</summary>
    Default,

    /// <summary>A fire-and-forget event (e.g. message publish, event send).</summary>
    Event
}