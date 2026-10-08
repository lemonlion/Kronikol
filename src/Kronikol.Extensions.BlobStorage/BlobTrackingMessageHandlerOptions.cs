using Kronikol.Constants;
namespace Kronikol.Extensions.BlobStorage;

/// <summary>
/// Configuration options for the Azure Blob Storage test tracking message handler.
/// </summary>
public record BlobTrackingMessageHandlerOptions
{
    public string ServiceName { get; set; } = "BlobStorage";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public BlobTrackingVerbosity Verbosity { get; set; } = BlobTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }
    /// <summary>
    /// Has no effect: nothing in this package reads it. The HTTP handler's option of the same name drives its
    /// implicit start of the action phase, a step this tracker does not have. Its removal is an open question for
    /// v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }

    /// <summary>
    /// Optional list of headers to exclude from the diagram notes.
    /// Defaults to common noisy Azure Storage headers.
    /// </summary>
    public HashSet<string> ExcludedHeaders { get; set; } =
    [
        "Authorization",
        "x-ms-date",
        "x-ms-version",
        "x-ms-client-request-id",
        "x-ms-return-client-request-id",
        "User-Agent",
        "Cache-Control"
    ];
    public BlobTrackingVerbosity? SetupVerbosity { get; set; }
    public BlobTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}