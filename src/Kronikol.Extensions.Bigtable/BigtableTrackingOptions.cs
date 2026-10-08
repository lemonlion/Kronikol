using Kronikol.Constants;
namespace Kronikol.Extensions.Bigtable;

/// <summary>
/// Configuration options for Google Cloud Bigtable test tracking.
/// </summary>
public record BigtableTrackingOptions
{
    public string ServiceName { get; set; } = "Bigtable";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public BigtableTrackingVerbosity Verbosity { get; set; } = BigtableTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public BigtableTrackingVerbosity? SetupVerbosity { get; set; }
    public BigtableTrackingVerbosity? ActionVerbosity { get; set; }
    public HashSet<BigtableOperation> ExcludedOperations { get; set; } = [];
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a call is tracked inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// <see cref="BigtableServiceCollectionExtensions.AddBigtableTestTracking"/> uses the container's when this is null,
    /// and an accessor passed to the <see cref="BigtableTracker"/> constructor takes precedence.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}