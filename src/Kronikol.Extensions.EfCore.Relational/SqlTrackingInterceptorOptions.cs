using Kronikol.Constants;
namespace Kronikol.Extensions.EfCore.Relational;

/// <summary>
/// Configuration options for the Entity Framework Core SQL tracking interceptor.
/// </summary>
public record SqlTrackingInterceptorOptions
{
    public string ServiceName { get; set; } = "Database";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public SqlTrackingVerbosity Verbosity { get; set; } = SqlTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: the SQL interceptor does not read it (<see cref="SqlTrackingInterceptorOptionsExtensions.WithTestInfoFrom"/>
    /// copies it from the HTTP handler's options, and nothing reads the copy). The HTTP handler's option of the same
    /// name drives its implicit start of the action phase, a step the interceptor does not have. Its removal is an open
    /// question for v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public SqlTrackingVerbosity? SetupVerbosity { get; set; }
    public SqlTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;

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

    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a command runs inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// <see cref="ServiceCollectionExtensions.AddSqlTestTracking"/> uses the container's when this is null,
    /// <see cref="SqlTrackingInterceptorOptionsExtensions.WithTestInfoFrom"/> copies the HTTP options' when this is null,
    /// and an accessor passed to the <see cref="SqlTrackingInterceptor"/> constructor takes precedence.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}