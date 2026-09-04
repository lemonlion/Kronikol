namespace Kronikol.Reports;

/// <summary>
/// Which diagram-type tab a scenario's diagram section starts on. A requested tab only wins
/// where that view exists for the scenario; otherwise the built-in fallback order applies
/// (sequence, then activity). Default: <see cref="Sequence"/>.
/// </summary>
public enum DiagramTabKind
{
    /// <summary>The sequence-diagram view (the default; activity when a scenario has no sequence view).</summary>
    Sequence,

    /// <summary>The whole-test-flow activity-diagram view.</summary>
    Activity,

    /// <summary>The whole-test-flow flame-chart view.</summary>
    FlameChart
}
