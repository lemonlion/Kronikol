namespace Kronikol.Reports;

/// <summary>
/// Which view a parameterized group's example table starts on, where both exist.
/// Default: <see cref="Flat"/>.
/// </summary>
public enum ParameterTableView
{
    /// <summary>The flat view — original Gherkin Example columns as scalar values (the default).</summary>
    Flat,

    /// <summary>The grouped view — structured parameter columns with sub-tables and expandables.</summary>
    Grouped
}
