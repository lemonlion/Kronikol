using Kronikol.Constants;
namespace Kronikol.Tracking;

/// <summary>
/// Options for configuring the <see cref="TestTrackingMessageHandler"/> that intercepts HTTP traffic for diagram generation.
/// </summary>
public record TestTrackingMessageHandlerOptions
{
    /// <summary>Maps port numbers to human-readable service names for diagram participants.</summary>
    public Dictionary<int, string> PortsToServiceNames { get; set; } = new();

    /// <summary>
    /// Maps <see cref="IHttpClientFactory"/> client names (the string passed to
    /// <c>services.AddHttpClient("name")</c>) to human-readable service names for diagram participants.
    /// <para>
    /// This is useful when HTTP mocking (JustEat HttpClient Interception, WireMock, etc.) makes
    /// port-based mapping via <see cref="PortsToServiceNames"/> unreliable.
    /// </para>
    /// </summary>
    public Dictionary<string, string> ClientNamesToServiceNames { get; set; } = new();

    /// <summary>When set, uses this fixed name for the receiving service instead of inferring from the port.</summary>
    public string? FixedNameForReceivingService { get; set; }

    /// <summary>Display name of the calling service in diagrams. Default: <c>"Caller"</c>.</summary>
    public string CallerName { get; set; } = TrackingDefaults.CallerName;

    /// <summary>Use <see cref="CallerName"/> instead.</summary>
    [Obsolete("Use CallerName instead. CallingServiceName will be removed in a future version.")]
    public string CallingServiceName { get => CallerName; set => CallerName = value; }

    /// <summary>
    /// Headers to copy from the request the host is serving onto each outgoing request. The four test identity
    /// headers need not be listed: the handler puts them on every request it sends.
    /// </summary>
    public IEnumerable<string> HeadersToForward { get; set; } = [];

    /// <summary>Callback that returns the current test name and ID. Set automatically by framework adapters.</summary>
    public Func<(string Name, string Id)>? CurrentTestInfoFetcher { get; set; } = null;

    /// <summary>Callback that returns the current test step type (e.g. "Given", "When", "Then"). Set automatically by framework adapters.</summary>
    public Func<string?>? CurrentStepTypeFetcher { get; set; } = null;

    /// <summary>OpenTelemetry activity source names to capture for internal flow diagrams.</summary>
    public string[]? InternalFlowActivitySources { get; set; }

    /// <summary>When <c>false</c>, HTTP requests made during the Setup phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringSetup { get; set; } = true;

    /// <summary>When <c>false</c>, HTTP requests made during the Action phase are not tracked. Default: <c>true</c>.</summary>
    public bool TrackDuringAction { get; set; } = true;

    /// <summary>
    /// Host names to exclude from tracking. Requests to these hosts are forwarded without logging.
    /// Default: <c>["override.com"]</c> (ASP.NET Core TestServer's internal base address).
    /// Set to an empty collection to disable host-based filtering.
    /// </summary>
    public IReadOnlyCollection<string> ExcludedHosts { get; set; } = ["override.com"];

    /// <summary>
    /// Resolves test identity from HTTP context headers when the handler runs inside
    /// the SUT's request pipeline. Auto-resolved from DI by <c>CreateTestTrackingClient</c>
    /// and <c>AddTrackedGrpcClient</c> when not explicitly set.
    /// </summary>
    public Microsoft.AspNetCore.Http.IHttpContextAccessor? HttpContextAccessor { get; set; }
}