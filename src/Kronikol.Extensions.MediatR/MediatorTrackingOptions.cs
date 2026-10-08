using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Extensions.MediatR;

/// <summary>
/// Configuration options for MediatR test tracking.
/// </summary>
public record MediatorTrackingOptions
{
    public string ServiceName { get; init; } = "Application";

    /// <summary>The participant name for the calling service in diagrams.</summary>
    public string CallerName { get; init; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; init => CallerName = value; }
    public string? ActivitySourceName { get; init; }
    public TrackingLogMode LogMode { get; init; } = TrackingLogMode.Immediate;
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; init; }
    public TrackingSerializerOptions? SerializerOptions { get; init; }
    public bool TrackDuringSetup { get; init; } = true;
    public bool TrackDuringAction { get; init; } = true;

    /// <summary>
    /// Resolves the scenario from the test-tracking request headers when a request or notification is sent inside a host's request
    /// pipeline (a host <c>WebApplicationFactory</c> starts, or any other host in the test process). Pass the host's own
    /// (<c>sp.GetService&lt;IHttpContextAccessor&gt;()</c>): ASP.NET Core fills an accessor only in a host that registers
    /// one (<c>AddHttpContextAccessor</c>, which <c>TrackDependenciesForDiagrams</c> calls).
    /// <see cref="MediatorTrackingExtensions.TrackMediatorForDiagrams"/> uses the container's when this is null.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; init; }
}