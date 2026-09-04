namespace Kronikol.Reports;

/// <summary>
/// Start state for the report's <c>Details</c> radio group (Expand / Collapse / Truncate),
/// which governs how note payloads render in browser-rendered sequence diagrams
/// (<see cref="PlantUmlRendering.BrowserJs"/> only). Default: <see cref="Truncated"/>.
/// </summary>
public enum ReportDetailsState
{
    /// <summary>Notes longer than the truncate-lines limit start truncated (the default).</summary>
    Truncated,

    /// <summary>Every note starts fully expanded.</summary>
    Expanded,

    /// <summary>Every note starts collapsed to its one-line preview.</summary>
    Collapsed
}
