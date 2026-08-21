using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Extensions.ProxyTap;

/// <summary>DI registration for <see cref="ProxyTap"/>s.</summary>
public static class ProxyTapServiceCollectionExtensions
{
    /// <summary>
    /// Registers one proxy tap as a singleton plus an <see cref="IHostedService"/> that starts it with the host
    /// and stops it on shutdown. Call once per hop to tap. The <see cref="ProxyTap"/> instances are resolvable
    /// as <c>IEnumerable&lt;ProxyTap&gt;</c>.
    /// </summary>
    public static IServiceCollection AddProxyTapTestTracking(this IServiceCollection services, Action<ProxyTapOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ProxyTapOptions();
        configure(options);
        options.Validate();

        var tap = new ProxyTap(options);
        services.AddSingleton(tap);
        services.AddSingleton<IHostedService>(new ProxyTapHostedService(tap));
        return services;
    }

    /// <summary>Registers several taps from a list of options (one hosted service each).</summary>
    public static IServiceCollection AddProxyTapTestTracking(this IServiceCollection services, IEnumerable<ProxyTapOptions> taps)
    {
        ArgumentNullException.ThrowIfNull(taps);
        foreach (var options in taps)
        {
            var captured = options;
            services.AddProxyTapTestTracking(o => Copy(captured, o));
        }

        return services;
    }

    private static void Copy(ProxyTapOptions from, ProxyTapOptions to)
    {
        to.ListenPort = from.ListenPort;
        to.ListenHost = from.ListenHost;
        to.ForwardBaseUri = from.ForwardBaseUri;
        to.CallerName = from.CallerName;
        to.ServiceName = from.ServiceName;
        to.DependencyCategory = from.DependencyCategory;
        to.CallerDependencyCategory = from.CallerDependencyCategory;
        to.BodyCapBytes = from.BodyCapBytes;
        to.CaptureBodies = from.CaptureBodies;
        to.HeaderPolicy = from.HeaderPolicy;
        to.HeaderWhitelist.UnionWith(from.HeaderWhitelist);
        to.SecretDenylist.Clear();
        to.SecretDenylist.UnionWith(from.SecretDenylist);
        to.DropSecretHeaders = from.DropSecretHeaders;
        to.RedactedValue = from.RedactedValue;
        to.ReinjectCorrelation = from.ReinjectCorrelation;
        to.IdentityFromTraceparent = from.IdentityFromTraceparent;
        to.TestNameHeaderFallbacks.AddRange(from.TestNameHeaderFallbacks);
        to.TestIdHeaderFallbacks.AddRange(from.TestIdHeaderFallbacks);
        to.IdentityResolver = from.IdentityResolver;
        to.FallbackTestName = from.FallbackTestName;
        to.CaptureUnattributedRequests = from.CaptureUnattributedRequests;
        to.FallbackTestId = from.FallbackTestId;
        to.Sink = from.Sink;
        to.Phase = from.Phase;
        to.ForwardTimeout = from.ForwardTimeout;
        to.ConnectTimeout = from.ConnectTimeout;
        to.EmitActivities = from.EmitActivities;
        to.SynthesizeTraceparent = from.SynthesizeTraceparent;
        to.Log = from.Log;
        to.Name = from.Name;
    }
}

/// <summary>Starts a <see cref="ProxyTap"/> with the host and disposes it on shutdown.</summary>
public sealed class ProxyTapHostedService(ProxyTap tap) : IHostedService
{
    /// <summary>The tap this service manages.</summary>
    public ProxyTap Tap { get; } = tap;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Tap.StartAsync(cancellationToken);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) => await Tap.DisposeAsync().ConfigureAwait(false);
}
