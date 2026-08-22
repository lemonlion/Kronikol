using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Extensions.Otlp;

/// <summary>DI registration for <see cref="OtlpTap"/>s.</summary>
public static class OtlpTapServiceCollectionExtensions
{
    /// <summary>
    /// Registers one OTLP receiver-tee as a singleton plus an <see cref="IHostedService"/> that starts it
    /// with the host and stops it on shutdown. The <see cref="OtlpTap"/> instances are resolvable as
    /// <c>IEnumerable&lt;OtlpTap&gt;</c>.
    /// </summary>
    public static IServiceCollection AddOtlpTapTestTracking(this IServiceCollection services, Action<OtlpTapOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OtlpTapOptions();
        configure(options);
        options.Validate();

        var tap = new OtlpTap(options);
        services.AddSingleton(tap);
        services.AddSingleton<IHostedService>(new OtlpTapHostedService(tap));
        return services;
    }

    /// <summary>Registers several taps from a list of options (one hosted service each).</summary>
    public static IServiceCollection AddOtlpTapTestTracking(this IServiceCollection services, IEnumerable<OtlpTapOptions> taps)
    {
        ArgumentNullException.ThrowIfNull(taps);
        foreach (var options in taps)
        {
            var captured = options;
            services.AddOtlpTapTestTracking(o => Copy(captured, o));
        }

        return services;
    }

    private static void Copy(OtlpTapOptions from, OtlpTapOptions to)
    {
        to.ListenPort = from.ListenPort;
        to.ListenHost = from.ListenHost;
        to.ForwardBaseUri = from.ForwardBaseUri;
        foreach (var (key, value) in from.ExpectedHeaders)
            to.ExpectedHeaders[key] = value;
        to.TracesPath = from.TracesPath;
        to.MaxRequestBytes = from.MaxRequestBytes;
        to.QueueCapacity = from.QueueCapacity;
        to.Sink = from.Sink;
        to.Phase = from.Phase;
        foreach (var (key, value) in from.ServiceNameMap)
            to.ServiceNameMap[key] = value;
        to.CaptureKinds.Clear();
        to.CaptureKinds.UnionWith(from.CaptureKinds);
        to.IncludeServerSpans = from.IncludeServerSpans;
        to.AttributeByTraceId = from.AttributeByTraceId;
        to.KnownTestIds = from.KnownTestIds;
        to.FallbackTestId = from.FallbackTestId;
        to.FallbackTestName = from.FallbackTestName;
        to.ContentCapBytes = from.ContentCapBytes;
        to.DefaultCallerName = from.DefaultCallerName;
        to.Log = from.Log;
        to.Name = from.Name;
    }
}

/// <summary>Starts an <see cref="OtlpTap"/> with the host and disposes it on shutdown.</summary>
public sealed class OtlpTapHostedService(OtlpTap tap) : IHostedService
{
    /// <summary>The tap this service manages.</summary>
    public OtlpTap Tap { get; } = tap;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Tap.StartAsync(cancellationToken);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) => await Tap.DisposeAsync().ConfigureAwait(false);
}
