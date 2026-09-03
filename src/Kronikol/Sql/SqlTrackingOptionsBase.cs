using Kronikol.Constants;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Sql;

/// <summary>
/// Shared options base for all direct SQL database tracking extensions.
/// </summary>
public record SqlTrackingOptionsBase
{
    public string ServiceName { get; set; } = "Database";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }
    public SqlTrackingVerbosityLevel Verbosity { get; set; } = SqlTrackingVerbosityLevel.Detailed;
    public Func<(string Name, string Id)?>? CurrentTestInfoFetcher { get; set; }
    public IHttpContextAccessor? HttpContextAccessor { get; set; }
    public HashSet<UnifiedSqlOperation> ExcludedOperations { get; set; } = [];
    public bool LogParameters { get; set; }
    public bool LogSqlText { get; set; } = true;
    public SqlTrackingVerbosityLevel? SetupVerbosity { get; set; }
    public SqlTrackingVerbosityLevel? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// The DependencyCategory string used in <see cref="Tracking.RequestResponseLog.DependencyCategory"/>.
    /// Each database extension sets its own default (e.g. "PostgreSQL", "SqlServer", "MySQL").
    /// </summary>
    public string DependencyCategory { get; set; } = DependencyCategories.SQL;

    /// <summary>
    /// URI scheme used for diagram URIs (e.g. "postgresql", "sqlserver", "mysql", "sqlite", "oracle").
    /// </summary>
    public string UriScheme { get; set; } = "sql";

    /// <summary>
    /// Whether to include response content in diagrams. Default: <c>true</c>.
    /// When <c>true</c>, response arrows show payload data (row counts, column names, etc.) at all verbosity levels.
    /// When <c>false</c>, response arrows are empty (previous behaviour).
    /// </summary>
    public bool LogResponseContent { get; set; } = true;

    /// <summary>
    /// Maximum number of rows to include in response content.
    /// Default: 10. Set to 0 for row count only (overrides ResponseDetail for row data).
    /// Negative values are treated as 0.
    /// </summary>
    public int MaxResponseRows { get; set; } = 10;

    /// <summary>
    /// Maximum display length for individual cell values in response content.
    /// Values exceeding this length are truncated. Default: 500.
    /// </summary>
    public int MaxValueDisplayLength { get; set; } = 500;

    /// <summary>
    /// Level of detail for response content in diagram arrows.
    /// Default: <c>null</c> — the detail follows the effective verbosity: actual row data
    /// (<see cref="SqlResponseDetail.FullRows"/>) at Raw/Detailed, a count+columns summary
    /// (<see cref="SqlResponseDetail.RowCountAndColumns"/>) at Summarised.
    /// Set explicitly to pin a level regardless of verbosity.
    /// </summary>
    public SqlResponseDetail? ResponseDetail { get; set; }
}
