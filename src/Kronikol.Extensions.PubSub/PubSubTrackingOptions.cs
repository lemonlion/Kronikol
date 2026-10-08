using Kronikol.Constants;
namespace Kronikol.Extensions.PubSub;

/// <summary>
/// Configuration options for Google Cloud Pub/Sub test tracking.
/// </summary>
public record PubSubTrackingOptions
{
    public string ServiceName { get; set; } = "PubSub";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public PubSubTrackingVerbosity Verbosity { get; set; } = PubSubTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public PubSubTrackingVerbosity? SetupVerbosity { get; set; }
    public PubSubTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }

    /// <summary>
    /// When <c>true</c>, the publisher injects test identity into message attributes
    /// and the subscriber extracts them, establishing a <see cref="Kronikol.Tracking.TestIdentityScope"/>
    /// so that downstream tracking operations are attributed to the originating test.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool PropagateTestIdentity { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the subscriber stores a correlation entry in <see cref="Kronikol.Tracking.TestCorrelationStore"/>
    /// after extracting test identity from message attributes. This enables parallel-safe attribution
    /// for decoupled processing patterns where the processing thread loses access to the message attributes.
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoCorrelateOnConsume { get; set; } = true;
}