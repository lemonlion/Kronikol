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
    /// <summary>
    /// Whether a message's value is recorded, as the entry's body: a JSON value is indented, offered the YAML view and
    /// queryable as an HTTP body is. Nothing is recorded under <see cref="KafkaTrackingVerbosity.Summarised"/>.
    /// </summary>
    public bool LogMessageValue { get; set; } = true;

    /// <summary>
    /// Whether a message's key is recorded, as the entry's <c>kafka-key</c> header, which a note draws on a line of its
    /// own above the body and keeps in view when the report hides headers. Nothing is recorded under
    /// <see cref="KafkaTrackingVerbosity.Summarised"/>.
    /// </summary>
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
    /// When <c>true</c> (the default), a produced message that carries no <c>traceparent</c> is given one naming
    /// <see cref="System.Diagnostics.Activity.Current"/> (or new ids, not sampled, when no span is current), so a consumer
    /// that parents on the message joins the producer's trace. Only where this wrapper is the last before Confluent's
    /// own producer: when another wrapper sits inside it, that wrapper's <c>traceparent</c> is the one sent and recorded.
    /// A <c>traceparent</c> the caller set is never replaced, and the caller's message is never written to.
    /// </summary>
    public bool PropagateTraceContext { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default <c>false</c>), the wrappers trace message processing with no change to the consuming
    /// code. <see cref="TrackingKafkaConsumer{TKey,TValue}.Consume(CancellationToken)"/> handles each message as
    /// <see cref="KafkaTracker.TrackConsume{TKey,TValue}"/>'s scope does and returns with the scope open: the message's
    /// <c>Kronikol.Kafka</c> span current, the consume not yet answered. The scope ends at the consumer's next
    /// <c>Consume</c> (at its start, before it waits), <c>Commit</c>, <c>StoreOffset</c>, <c>Close</c> or
    /// <c>Dispose</c>, leaving current what was current when that call began, or, when that was the scope's own span,
    /// what was current before the scope. A produce through the wrapper runs under a <c>{topic} publish</c> span, which
    /// the <c>traceparent</c> Kronikol adds names. Best effort: a <c>Consume</c> called inside <c>Task.Run</c> leaves
    /// nothing current for the code that awaits it, and a commit from another thread ends the window there. Where the
    /// consuming code can be changed, the scope (<c>using (tracker.TrackConsume(result))</c>) has neither limit.
    /// </summary>
    public bool TraceMessageProcessing { get; set; }

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
    /// <see cref="PropagateTestIdentity"/> is on, a message produced inside a scenario carries that scenario, the one its
    /// produce is recorded under, whichever thread the delivery report arrives on; one produced outside any scenario
    /// carries none.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}