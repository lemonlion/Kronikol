using Kronikol.Constants;
namespace Kronikol.Extensions.Elasticsearch;

/// <summary>
/// Configuration options for Elasticsearch test tracking.
/// </summary>
public class ElasticsearchTrackingOptions
{
    public string ServiceName { get; set; } = "Elasticsearch";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public ElasticsearchTrackingVerbosity Verbosity { get; set; } = ElasticsearchTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public HashSet<ElasticsearchOperation> ExcludedOperations { get; set; } =
    [
        ElasticsearchOperation.ClusterHealth,
        ElasticsearchOperation.CatApis
    ];
    public ElasticsearchTrackingVerbosity? SetupVerbosity { get; set; }
    public ElasticsearchTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>
    /// Whether to include response body content in diagrams at Detailed verbosity.
    /// At Raw verbosity, the response body is always included regardless.
    /// Default: true.
    /// </summary>
    public bool LogResponseContent { get; set; } = true;
}