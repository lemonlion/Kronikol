using Kronikol.Constants;
using System.Text.Json;

namespace Kronikol.Tracking;

/// <summary>
/// Configuration options for <see cref="MessageTracker"/>, controlling service names,
/// verbosity levels, phase-aware tracking, and dependency category rendering.
/// </summary>
public record MessageTrackerOptions
{
    /// <summary>
    /// The participant name shown in diagrams for the messaging service when used as
    /// the <c>destinationName</c> default. Also used in <see cref="MessageTracker.ComponentName"/>.
    /// </summary>
    public string ServiceName { get; set; } = "MessageBus";

    /// <summary>
    /// The participant name for the service sending/receiving messages.
    /// </summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    /// <summary>
    /// Controls how much detail is logged. <see cref="MessageTrackerVerbosity.Summarised"/>
    /// omits message payloads; other levels include them.
    /// </summary>
    public MessageTrackerVerbosity Verbosity { get; set; } = MessageTrackerVerbosity.Detailed;

    /// <summary>
    /// Returns the current test's name and ID. Required for logging — when <c>null</c>,
    /// tracking calls are silently skipped.
    /// </summary>
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }

    /// <summary>
    /// Has no effect: <see cref="MessageTracker"/> does not read it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step the message tracker does not have. Its removal is an open question
    /// for v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }

    /// <summary>
    /// JSON serialiser options used when serialising message payloads.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>Verbosity override for the Setup phase. <c>null</c> = use <see cref="Verbosity"/>.</summary>
    public MessageTrackerVerbosity? SetupVerbosity { get; set; }

    /// <summary>Verbosity override for the Action phase. <c>null</c> = use <see cref="Verbosity"/>.</summary>
    public MessageTrackerVerbosity? ActionVerbosity { get; set; }

    /// <summary>When <c>false</c>, messages during the Setup phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringSetup { get; set; } = true;

    /// <summary>When <c>false</c>, messages during the Action phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// The dependency category used in PlantUML diagrams to determine the participant shape
    /// (e.g. <c>queue</c>, <c>database</c>, <c>entity</c>).
    /// Defaults to <c>"MessageQueue"</c> which renders as a queue shape.
    /// </summary>
    public string DependencyCategory { get; set; } = DependencyCategories.MessageQueue;

    /// <summary>
    /// The dependency category for the <see cref="CallerName"/> participant in PlantUML diagrams.
    /// Use this to control the shape of the calling service independently of <see cref="DependencyCategory"/>.
    /// For example, set to <c>"MessageQueue"</c> to render a broker caller as a queue.
    /// Defaults to <c>null</c> (caller uses default entity/actor shape).
    /// </summary>
    public string? CallerDependencyCategory { get; set; }

    /// <summary>
    /// When <c>true</c>, the <see cref="MessageTracker"/> will first attempt to resolve test info
    /// from <c>IHttpContextAccessor</c> request headers (the same dual-layer correlation used by
    /// the legacy constructor), falling back to <see cref="CurrentTestInfoFetcher"/> when
    /// <c>HttpContext</c> is null or the tracking headers are absent.
    /// <para>
    /// Enable this when messages are published as side-effects of HTTP request processing and the
    /// xUnit <c>TestContext</c> ambient state may not flow to the publishing thread (e.g. thread-pool
    /// continuations, <c>ConfigureAwait(false)</c> chains).
    /// </para>
    /// Default: <c>false</c> (uses <see cref="CurrentTestInfoFetcher"/> exclusively).
    /// </summary>
    public bool UseHttpContextCorrelation { get; set; }
}
