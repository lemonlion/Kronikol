using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ClickHouse.Driver;

/// <summary>
/// Typed registration helpers pairing <c>Kronikol.Extensions.ClickHouse</c> with ClickHouse.Driver
/// (the official ClickHouse .NET client): same wrapping as the driver-agnostic methods, with
/// <see cref="ClickHouseDriverAdapter"/> wired in.
/// </summary>
public static class ClickHouseDriverTrackingExtensions
{
    /// <summary>
    /// Wraps the ClickHouse.Driver connection in a <see cref="TrackingClickHouseConnection"/> with
    /// the ClickHouse.Driver adapter paired. An adapter already set on <paramref name="options"/>
    /// is respected.
    /// </summary>
    public static TrackingClickHouseConnection WithClickHouseDriverTestTracking(
        this ClickHouseConnection connection,
        ClickHouseTrackingOptions? options = null)
    {
        var opts = options ?? new ClickHouseTrackingOptions();
        opts.DriverAdapter ??= ClickHouseDriverAdapter.Instance;
        return connection.WithClickHouseTestTracking(opts);
    }

    /// <summary>
    /// Wraps a ClickHouse.Driver <see cref="IClickHouseClient"/> in a <see cref="TrackingClickHouseClient"/>.
    /// A client that is already tracked is returned as it is.
    /// </summary>
    public static TrackingClickHouseClient WithClickHouseDriverTestTracking(
        this IClickHouseClient client,
        ClickHouseTrackingOptions? options = null)
        => client as TrackingClickHouseClient ?? new TrackingClickHouseClient(client, options);

    /// <summary>
    /// Registers ClickHouse test tracking (see
    /// <see cref="ClickHouseServiceCollectionExtensions.AddClickHouseTestTracking"/>) with the
    /// ClickHouse.Driver adapter paired, and decorates every <see cref="IClickHouseClient"/> and
    /// <see cref="IClickHouseDataSource"/> registration, keyed ones included, so the client's calls are tracked too:
    /// a data source's <c>GetClient()</c> returns a <see cref="TrackingClickHouseClient"/>. All of them share one
    /// options object. A registration of the concrete <see cref="ClickHouseClient"/> or
    /// <see cref="ClickHouseDataSource"/> cannot be decorated (both are sealed) and is left as it is.
    /// Call it after the registrations it decorates.
    /// </summary>
    public static IServiceCollection AddClickHouseDriverTestTracking(
        this IServiceCollection services,
        Action<ClickHouseTrackingOptions>? configure = null)
    {
        ClickHouseTrackingOptions? shared = null;
        services.AddClickHouseTestTracking(options =>
        {
            configure?.Invoke(options);
            options.DriverAdapter ??= ClickHouseDriverAdapter.Instance;
            shared = options;
        });
        var options = shared!;

        services.DecorateAll<IClickHouseClient>((sp, inner) =>
        {
            options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();
            return inner as TrackingClickHouseClient ?? new TrackingClickHouseClient(inner, options, options.HttpContextAccessor);
        });
        services.DecorateAll<IClickHouseDataSource>((sp, inner) =>
        {
            options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();
            return inner as TrackingClickHouseDataSource ?? new TrackingClickHouseDataSource(inner, options, options.HttpContextAccessor);
        });
        return services;
    }
}
