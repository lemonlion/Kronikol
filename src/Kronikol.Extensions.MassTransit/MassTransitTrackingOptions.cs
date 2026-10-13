using Kronikol.Constants;
namespace Kronikol.Extensions.MassTransit;

/// <summary>
/// Configuration options for MassTransit test tracking.
/// </summary>
public record MassTransitTrackingOptions
{
    public string ServiceName { get; set; } = "MassTransit";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public MassTransitTrackingVerbosity Verbosity { get; set; } = MassTransitTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public bool TrackSend { get; set; } = true;
    public bool TrackPublish { get; set; } = true;
    public bool TrackConsume { get; set; } = true;
    public bool LogMessageBody { get; set; } = true;
    public bool LogFaults { get; set; } = true;
    public MassTransitTrackingVerbosity? SetupVerbosity { get; set; }
    public MassTransitTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a message is sent, published or consumed inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// An accessor passed to the <see cref="MassTransitTracker"/> constructor takes precedence. When
    /// <see cref="PropagateTestIdentity"/> is on, a sent or published message carries the scenario its send is
    /// recorded under.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>
    /// When <c>true</c>, the send/publish observers inject test identity into MassTransit message headers
    /// and the consume observer extracts them, establishing a <see cref="Kronikol.Tracking.TestIdentityScope"/>
    /// so that downstream tracking operations are attributed to the originating test.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool PropagateTestIdentity { get; set; } = true;
}