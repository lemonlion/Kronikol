using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.InternalFlow;

/// <summary>
/// Provides extension methods for configuring internal flow tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class InternalFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers a non-invasive <see cref="InternalFlowActivityListener"/> that
    /// captures <see cref="System.Diagnostics.Activity"/> spans for internal flow diagram popups.
    /// <para>
    /// This uses a raw <see cref="System.Diagnostics.ActivityListener"/> (BCL)
    /// rather than the OTel SDK, so it works with or without an existing
    /// OpenTelemetry configuration and never interferes with the SUT's
    /// sampling, exporters, or telemetry assertions.
    /// </para>
    /// <para>
    /// <b>Note:</b> The <see cref="Tracking.TestTrackingMessageHandler"/> auto-starts a listener on its first request,
    /// and it hears every source but the runtime's connection-level ones. This method is only needed if you need the
    /// listener started before any request is sent, or owned by the container.
    /// </para>
    /// </summary>
    /// <param name="services">The service collection (typically from <c>ConfigureTestServices</c>).</param>
    public static IServiceCollection AddActivityListenerForInternalFlowTracking(this IServiceCollection services)
    {
        // Eagerly create so the listener starts capturing immediately —
        // it must be active before the host processes any requests.
        var listener = new InternalFlowActivityListener();

        // Register as a factory returning the already-created instance.
        // Factory-registered singletons ARE disposed by the container.
        services.AddSingleton(_ => listener);
        return services;
    }

    /// <summary>
    /// Takes the names of sources to capture as well, and never read them: the listener hears every source but the
    /// runtime's connection-level ones whatever it is given. Obsolete from 5.34.0 (#138), and removed in v6.
    /// </summary>
    /// <param name="services">The service collection (typically from <c>ConfigureTestServices</c>).</param>
    /// <param name="additionalActivitySources">Never read.</param>
    [Obsolete("The names were never read: the listener hears every source but the runtime's connection-level ones. Call AddActivityListenerForInternalFlowTracking() with no names, and narrow what a report draws with ReportConfigurationOptions.InternalFlowActivitySources and InternalFlowSpanGranularity.Manual.")]
    public static IServiceCollection AddActivityListenerForInternalFlowTracking(
        this IServiceCollection services,
        params string[] additionalActivitySources) =>
        services.AddActivityListenerForInternalFlowTracking();
}
