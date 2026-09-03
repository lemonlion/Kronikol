using Kronikol.Tracking;

namespace Kronikol.Sql;

/// <summary>
/// Controls the level of detail included in SQL response content for diagram arrows.
/// </summary>
public enum SqlResponseDetail
{
    /// <summary>Row count only (e.g. "3 rows")</summary>
    RowCountOnly,

    /// <summary>Row count + column names (e.g. "3 rows [Name, Preference, CreatedAt]")</summary>
    RowCountAndColumns,

    /// <summary>Full row data up to MaxResponseRows (JSON representation)</summary>
    FullRows
}

/// <summary>
/// Resolves the effective response detail: an explicitly configured value wins; with none, the
/// detail follows the effective verbosity — actual row data at Raw/Detailed (matching the
/// HTTP-level integrations, which show real response payloads), a count+columns summary at
/// Summarised (matching how request content is summarised at that level).
/// </summary>
public static class SqlResponseDetailResolver
{
    public static SqlResponseDetail Resolve(SqlResponseDetail? configured, bool effectiveVerbosityIsSummarised)
        => configured ?? (effectiveVerbosityIsSummarised
            ? SqlResponseDetail.RowCountAndColumns
            : SqlResponseDetail.FullRows);

    /// <summary>Resolve from options, applying any phase verbosity overrides in effect.</summary>
    public static SqlResponseDetail Resolve(SqlTrackingOptionsBase options)
    {
        var effectiveVerbosity = PhaseConfiguration.GetEffectiveVerbosity(
            options.Verbosity, options.SetupVerbosity, options.ActionVerbosity);
        return Resolve(options.ResponseDetail, effectiveVerbosity == SqlTrackingVerbosityLevel.Summarised);
    }
}
