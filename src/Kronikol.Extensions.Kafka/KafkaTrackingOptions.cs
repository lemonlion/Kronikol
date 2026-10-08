using Kronikol.Constants;
namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Configuration options for Apache Kafka test tracking.
/// </summary>
public record KafkaTrackingOptions
{
    public string ServiceName { get; set; } = "Kafka";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public KafkaTrackingVerbosity Verbosity { get; set; } = KafkaTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public bool TrackProduce { get; set; } = true;
    public bool TrackConsume { get; set; } = true;
    public bool LogMessageValue { get; set; } = true;
    public bool LogMessageKey { get; set; } = true;
    public bool TrackSubscribe { get; set; } = false;
    public bool TrackUnsubscribe { get; set; } = false;
    public bool TrackCommit { get; set; } = false;
    public bool TrackFlush { get; set; } = false;
    public bool TrackTransactions { get; set; } = false;
    public KafkaTrackingVerbosity? SetupVerbosity { get; set; }
    public KafkaTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the producer injects test identity headers into Kafka message headers
    /// and the consumer extracts them, establishing a <see cref="Kronikol.Tracking.TestIdentityScope"/>
    /// so that downstream tracking operations are attributed to the originating test.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool PropagateTestIdentity { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the consumer stores a correlation entry in <see cref="Kronikol.Tracking.TestCorrelationStore"/>
    /// after extracting test identity from message headers. This enables parallel-safe attribution
    /// for decoupled processing patterns where the processing thread loses access to the message headers.
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoCorrelateOnConsume { get; set; } = true;

    /// <summary>
    /// Extracts a correlation key from a consumed message key.
    /// When <c>null</c>, the default format is used: <c>kafka:{ServiceName}:{messageKey}</c>.
    /// Only invoked when <see cref="AutoCorrelateOnConsume"/> is <c>true</c>.
    /// </summary>
    public Func<string, string, string>? ConsumeKeyExtractor { get; set; }

    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a message is produced or consumed inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// The <c>Add*TestTracking</c> registrations use the container's when this is null, and an accessor passed to the
    /// <see cref="KafkaTracker"/> constructor or to <see cref="KafkaTrackingInterceptor"/> takes precedence. When
    /// <see cref="PropagateTestIdentity"/> is on, a produced message carries the scenario the produce is recorded under.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}