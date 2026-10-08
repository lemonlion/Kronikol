using Kronikol.Constants;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Extensions.Grpc;

/// <summary>
/// Configuration options for gRPC test tracking.
/// </summary>
public record GrpcTrackingOptions
{
    public string ServiceName { get; set; } = "GrpcService";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    public GrpcTrackingVerbosity Verbosity { get; set; } = GrpcTrackingVerbosity.Detailed;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; }

    /// <summary>
    /// Has no effect: no part of the gRPC package reads it. (The HTTP handler's option of the same name drives its
    /// implicit start of the action phase; the gRPC interceptor has no such step.) Its removal is an open question
    /// for v5 (plans/V5_PLAN.md).
    /// </summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; }

    public bool UseProtoServiceNameInDiagram { get; set; } = false;
    public GrpcTrackingVerbosity? SetupVerbosity { get; set; }
    public GrpcTrackingVerbosity? ActionVerbosity { get; set; }
    public bool TrackDuringSetup { get; set; } = true;
    public bool TrackDuringAction { get; set; } = true;
    public IHttpContextAccessor? HttpContextAccessor { get; set; }
}