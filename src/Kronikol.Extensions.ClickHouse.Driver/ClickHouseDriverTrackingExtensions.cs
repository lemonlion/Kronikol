using ClickHouse.Driver.ADO;
using Microsoft.Extensions.DependencyInjection;

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
    /// Registers ClickHouse test tracking (see
    /// <see cref="ClickHouseServiceCollectionExtensions.AddClickHouseTestTracking"/>) with the
    /// ClickHouse.Driver adapter paired.
    /// </summary>
    public static IServiceCollection AddClickHouseDriverTestTracking(
        this IServiceCollection services,
        Action<ClickHouseTrackingOptions>? configure = null)
        => services.AddClickHouseTestTracking(options =>
        {
            configure?.Invoke(options);
            options.DriverAdapter ??= ClickHouseDriverAdapter.Instance;
        });
}
