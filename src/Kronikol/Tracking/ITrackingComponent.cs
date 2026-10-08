namespace Kronikol.Tracking;

/// <summary>
/// Implemented by tracking handlers, interceptors, and proxies to enable
/// automated detection of misconfigured components that were registered but
/// never invoked during a test run.
/// </summary>
public interface ITrackingComponent
{
    /// <summary>
    /// Human-readable name shown in diagnostic messages (e.g. "SqlTrackingInterceptor", "CosmosDB handler").
    /// </summary>
    string ComponentName { get; }

    /// <summary>
    /// True once the component has processed at least one request/command.
    /// </summary>
    bool WasInvoked { get; }

    /// <summary>
    /// Number of requests or commands processed so far.
    /// </summary>
    int InvocationCount { get; }

    /// <summary>
    /// <c>true</c> when this component holds an <c>IHttpContextAccessor</c>, which lets it attribute a call made in a
    /// host to the test whose request the host is serving, read from that request's identity headers. <c>false</c>
    /// when it holds none, and for every component that does not take one (the default). It says only that an
    /// accessor is held: a test-side client given a host's accessor reads <c>true</c> though no request is current
    /// where it runs, and resolves its identity from the test framework as one without an accessor does.
    /// The diagnostic page's HttpContextAccessor column counts the instances of each component that read <c>true</c>.
    /// </summary>
    bool HasHttpContextAccessor => false;
}
