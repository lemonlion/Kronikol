using Kronikol.Constants;
namespace Kronikol.Extensions.CloudStorage;

/// <summary>
/// Configuration options for the Google Cloud Storage test tracking message handler.
/// </summary>
public record CloudStorageTrackingMessageHandlerOptions
{
    public string ServiceName { get; set; } = "CloudStorage";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public CloudStorageTrackingVerbosity Verbosity { get; set; } = CloudStorageTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }
    public HashSet<string> ExcludedHeaders { get; set; } =
    [
        "Authorization", "x-goog-api-client", "User-Agent"
    ];
    public CloudStorageTrackingVerbosity? SetupVerbosity { get; set; }
    public CloudStorageTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}