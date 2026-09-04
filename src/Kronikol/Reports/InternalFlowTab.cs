namespace Kronikol.Reports;

/// <summary>
/// Which tab an internal-flow Activity / Flame Chart toggle starts on (the per-step popup and the
/// whole-test-flow disclosure, where both views exist). Default: <see cref="Activity"/>.
/// </summary>
public enum InternalFlowTab
{
    /// <summary>The activity-diagram view (the default).</summary>
    Activity,

    /// <summary>The flame-chart view.</summary>
    FlameChart
}
