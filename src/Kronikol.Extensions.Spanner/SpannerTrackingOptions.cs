using Kronikol.Constants;
namespace Kronikol.Extensions.Spanner;

/// <summary>
/// Configuration options for Google Cloud Spanner test tracking.
/// </summary>
public record SpannerTrackingOptions
{
    public string ServiceName { get; set; } = "Spanner";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public SpannerTrackingVerbosity Verbosity { get; set; } = SpannerTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public bool LogSqlText { get; set; } = true;
    [Obsolete("Raw verbosity now always includes parameters. This property is no longer used.")]
    public bool LogParameters { get; set; }
    public HashSet<SpannerOperation> ExcludedOperations { get; set; } = [];
    public SpannerTrackingVerbosity? SetupVerbosity { get; set; }
    public SpannerTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// Whether to include response content in diagrams. Default: true.
    /// When false, response arrows are empty (previous behaviour).
    /// Response arrows show payload data (row counts, column names, etc.) at all verbosity levels.
    /// </summary>
    public bool LogResponseContent { get; set; } = true;

    /// <summary>
    /// Maximum number of rows to include in response content.
    /// Default: 10. Set to 0 for row count only (overrides ResponseDetail for row data).
    /// Negative values are treated as 0.
    /// </summary>
    public int MaxResponseRows { get; set; } = 10;

    /// <summary>
    /// Level of detail for response content in diagram arrows.
    /// Default: <c>null</c> — the detail follows the effective verbosity: actual row data
    /// (FullRows) at Raw/Detailed, a count+columns summary at Summarised.
    /// Set explicitly to pin a level regardless of verbosity.
    /// </summary>
    public SpannerResponseDetail? ResponseDetail { get; set; }

    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a statement runs inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// <see cref="SpannerServiceCollectionExtensions.AddSpannerTestTracking"/> uses the container's when this is null, and
    /// an accessor passed to a constructor, or to the <c>WithTestTracking</c> overload that takes one, takes precedence.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>The effective response detail, applying any phase verbosity overrides in effect.</summary>
    internal SpannerResponseDetail ResolveResponseDetail()
    {
        var effectiveVerbosity = Tracking.PhaseConfiguration.GetEffectiveVerbosity(
            Verbosity, SetupVerbosity, ActionVerbosity);
        return ResponseDetail ?? (effectiveVerbosity == SpannerTrackingVerbosity.Summarised
            ? SpannerResponseDetail.RowCountAndColumns
            : SpannerResponseDetail.FullRows);
    }
}