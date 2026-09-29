using System.Diagnostics;
using Kronikol.Tracking;

namespace Kronikol.InternalFlow;

/// <summary>
/// Represents a segment of internal flow between two consecutive HTTP boundaries
/// (e.g. between a request arriving and the SUT calling a dependency).
/// </summary>
public record InternalFlowSegment(
    Guid RequestResponseId,
    RequestResponseType BoundaryType,
    string TestId,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    Activity[] Spans)
{
    /// <summary>
    /// Spans that started during this call and that a call of another test would take too, so neither holds them:
    /// nothing in them says whose they are (#87, <c>plans/SPAN_ATTRIBUTION_PLAN.md</c>). The popup says how many.
    /// </summary>
    internal int SpansLeftOut { get; init; }
}
