using Kronikol.Constants;
namespace Kronikol.Extensions.SQS;

/// <summary>
/// Configuration options for the Amazon SQS test tracking message handler.
/// </summary>
public record SqsTrackingMessageHandlerOptions
{
    public string ServiceName { get; set; } = "SQS";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public SqsTrackingVerbosity Verbosity { get; set; } = SqsTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public HashSet<string> ExcludedHeaders { get; set; } =
    [
        "Authorization", "x-amz-date", "x-amz-security-token",
        "x-amz-content-sha256", "User-Agent", "amz-sdk-invocation-id",
        "amz-sdk-request"
    ];
    public SqsTrackingVerbosity? SetupVerbosity { get; set; }
    public SqsTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}