using Kronikol.Constants;
namespace Kronikol.Extensions.EventHubs;

/// <summary>
/// Configuration options for Azure Event Hubs test tracking.
/// </summary>
public record EventHubsTrackingOptions
{
    public string ServiceName { get; set; } = "EventHubs";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public EventHubsTrackingVerbosity Verbosity { get; set; } = EventHubsTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v6 (plans/V6_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public EventHubsTrackingVerbosity? SetupVerbosity { get; set; }
    public EventHubsTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when an event is sent or received inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// The <c>Add*TestTracking</c> registrations use the container's when this is null, and an accessor passed to the
    /// <see cref="EventHubsTracker"/> constructor takes precedence. When <see cref="PropagateTestIdentity"/> is on, a
    /// sent event carries the scenario the send is recorded under.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>
    /// When <c>true</c>, the producer injects test identity into EventData properties
    /// and the consumer extracts them, establishing a <see cref="Kronikol.Tracking.TestIdentityScope"/>
    /// so that downstream tracking operations are attributed to the originating test.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool PropagateTestIdentity { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the consumer stores a correlation entry in <see cref="Kronikol.Tracking.TestCorrelationStore"/>
    /// after extracting test identity from event properties. This enables parallel-safe attribution
    /// for decoupled processing patterns where the processing thread loses access to the event properties.
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoCorrelateOnConsume { get; set; } = true;
}