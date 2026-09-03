using ClickHouse.Client.ADO;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.ClickHouse.Client;

/// <summary>
/// Typed registration helpers pairing <c>Kronikol.Extensions.ClickHouse</c> with the
/// ClickHouse.Client driver: same wrapping as the driver-agnostic methods, with
/// <see cref="ClickHouseClientDriverAdapter"/> wired in.
/// </summary>
public static class ClickHouseClientTrackingExtensions
{
    /// <summary>
    /// Wraps the ClickHouse.Client connection in a <see cref="TrackingClickHouseConnection"/> with
    /// the ClickHouse.Client driver adapter paired. An adapter already set on
    /// <paramref name="options"/> is respected.
    /// </summary>
    public static TrackingClickHouseConnection WithClickHouseClientTestTracking(
        this ClickHouseConnection connection,
        ClickHouseTrackingOptions? options = null)
    {
        var opts = options ?? new ClickHouseTrackingOptions();
        opts.DriverAdapter ??= ClickHouseClientDriverAdapter.Instance;
        return connection.WithClickHouseTestTracking(opts);
    }

    /// <summary>
    /// Registers ClickHouse test tracking (see
    /// <see cref="ClickHouseServiceCollectionExtensions.AddClickHouseTestTracking"/>) with the
    /// ClickHouse.Client driver adapter paired.
    /// </summary>
    public static IServiceCollection AddClickHouseClientTestTracking(
        this IServiceCollection services,
        Action<ClickHouseTrackingOptions>? configure = null)
        => services.AddClickHouseTestTracking(options =>
        {
            configure?.Invoke(options);
            options.DriverAdapter ??= ClickHouseClientDriverAdapter.Instance;
        });
}
