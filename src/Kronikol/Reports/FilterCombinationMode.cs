namespace Kronikol.Reports;

/// <summary>
/// How a multi-select filter (dependencies, tags) combines its selected chips. The built-in
/// default lives with each setting, not in this enum's ordering: since 5.0.0 the dependency
/// filter and the tag filter both default to <see cref="Or"/>.
/// </summary>
public enum FilterCombinationMode
{
    /// <summary>Show scenarios matching ALL selected chips.</summary>
    And,

    /// <summary>Show scenarios matching ANY selected chip.</summary>
    Or
}
