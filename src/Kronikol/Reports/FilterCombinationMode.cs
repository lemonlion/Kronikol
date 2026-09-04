namespace Kronikol.Reports;

/// <summary>
/// How a multi-select filter (dependencies, categories) combines its selected chips. The
/// built-in default lives with each setting, not in this enum's ordering: the dependency
/// filter defaults to <see cref="And"/>, the category filter to <see cref="Or"/>.
/// </summary>
public enum FilterCombinationMode
{
    /// <summary>Show scenarios matching ALL selected chips.</summary>
    And,

    /// <summary>Show scenarios matching ANY selected chip.</summary>
    Or
}
