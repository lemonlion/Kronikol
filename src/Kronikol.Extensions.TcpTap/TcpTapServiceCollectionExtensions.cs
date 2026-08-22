using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Extensions.TcpTap;

/// <summary>DI registration for <see cref="TcpTap"/>s (one hosted service each, exactly like the proxy tap).</summary>
public static class TcpTapServiceCollectionExtensions
{
    /// <summary>
    /// Registers one Redis wire tap as a singleton plus an <see cref="IHostedService"/> that starts it with the
    /// host and stops it on shutdown. Resolvable as <c>RedisTap</c>, <c>TcpTap</c> or
    /// <c>IEnumerable&lt;TcpTap&gt;</c>.
    /// </summary>
    public static IServiceCollection AddRedisTapTestTracking(this IServiceCollection services, Action<RedisTapOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new RedisTapOptions();
        configure(options);
        options.Validate();

        var tap = new RedisTap(options);
        services.AddSingleton(tap);
        services.AddSingleton<TcpTap>(tap);
        services.AddSingleton<IHostedService>(new TcpTapHostedService(tap));
        return services;
    }

    /// <summary>Registers several Redis taps from a list of options (one hosted service each).</summary>
    public static IServiceCollection AddRedisTapTestTracking(this IServiceCollection services, IEnumerable<RedisTapOptions> taps)
    {
        ArgumentNullException.ThrowIfNull(taps);
        foreach (var options in taps)
        {
            var captured = options;
            services.AddRedisTapTestTracking(target => captured.CopyTo(target));
        }

        return services;
    }

    /// <summary>
    /// Registers one MongoDB wire tap as a singleton plus an <see cref="IHostedService"/> that starts it with
    /// the host and stops it on shutdown. Resolvable as <c>MongoTap</c>, <c>TcpTap</c> or
    /// <c>IEnumerable&lt;TcpTap&gt;</c>.
    /// </summary>
    public static IServiceCollection AddMongoTapTestTracking(this IServiceCollection services, Action<MongoTapOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MongoTapOptions();
        configure(options);
        options.Validate();

        var tap = new MongoTap(options);
        services.AddSingleton(tap);
        services.AddSingleton<TcpTap>(tap);
        services.AddSingleton<IHostedService>(new TcpTapHostedService(tap));
        return services;
    }

    /// <summary>Registers several MongoDB taps from a list of options (one hosted service each).</summary>
    public static IServiceCollection AddMongoTapTestTracking(this IServiceCollection services, IEnumerable<MongoTapOptions> taps)
    {
        ArgumentNullException.ThrowIfNull(taps);
        foreach (var options in taps)
        {
            var captured = options;
            services.AddMongoTapTestTracking(target => captured.CopyTo(target));
        }

        return services;
    }

    /// <summary>
    /// Registers a tap for any other TCP protocol: set <see cref="TcpTapOptions.DecoderFactory"/> to your own
    /// <see cref="IProtocolDecoder"/> and the tee, the bounded queues, the caps and the sink plumbing come for
    /// free.
    /// </summary>
    public static IServiceCollection AddTcpTapTestTracking(this IServiceCollection services, Action<TcpTapOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new TcpTapOptions();
        configure(options);
        options.Validate();

        var tap = new TcpTap(options);
        services.AddSingleton(tap);
        services.AddSingleton<IHostedService>(new TcpTapHostedService(tap));
        return services;
    }
}

/// <summary>Starts a <see cref="TcpTap"/> with the host and disposes it on shutdown.</summary>
public sealed class TcpTapHostedService(TcpTap tap) : IHostedService
{
    /// <summary>The tap this service manages.</summary>
    public TcpTap Tap { get; } = tap;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Tap.StartAsync(cancellationToken);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken) => await Tap.DisposeAsync().ConfigureAwait(false);
}
