namespace Kronikol.Reports.Merge;

/// <summary>
/// A precomputed, self-contained whole-test-flow rendering for a single scenario.
/// The HTML fragments inline their compressed PlantUML / flame-chart payloads (rendered with no
/// shared diagram-data map), so they can be serialized into a mergeable report and re-embedded
/// verbatim when a combined report is rendered — without access to the original <c>Activity</c> spans.
/// </summary>
public sealed record WholeTestFlowFragment(string ActivityHtml, string FlameHtml, int SpanCount);
