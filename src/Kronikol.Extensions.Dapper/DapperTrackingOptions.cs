using Kronikol.Constants;
namespace Kronikol;

/// <summary>
/// Configuration options for Dapper test tracking.
/// </summary>
public record DapperTrackingOptions
{
    public string ServiceName { get; set; } = "Database";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public DapperTrackingVerbosity Verbosity { get; set; } = DapperTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)?>? CurrentTestInfoFetcher { get; set; }
    public bool LogParameters { get; set; }
    public bool LogSqlText { get; set; } = true;
    public HashSet<DapperOperation> ExcludedOperations { get; set; } = [];
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public DapperTrackingVerbosity? SetupVerbosity { get; set; }
    public DapperTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>
    /// Whether to include response content in diagrams. Default: true.
    /// When false, response arrows are empty (previous behaviour).
    /// </summary>
    public bool LogResponseContent { get; set; } = true;

    /// <summary>
    /// Maximum number of rows to include in response content. Default: 10.
    /// </summary>
    public int MaxResponseRows { get; set; } = 10;

    /// <summary>
    /// Maximum display length for individual cell values. Default: 500.
    /// </summary>
    public int MaxValueDisplayLength { get; set; } = 500;

    /// <summary>
    /// Level of detail for response content in diagram arrows.
    /// Default: <c>null</c> — the detail follows the effective verbosity: actual row data
    /// (FullRows) at Raw/Detailed, a count+columns summary at Summarised.
    /// Set explicitly to pin a level regardless of verbosity.
    /// </summary>
    public Sql.SqlResponseDetail? ResponseDetail { get; set; }
}