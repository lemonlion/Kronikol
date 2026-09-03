using Microsoft.Extensions.DependencyInjection;
using OctonicaConnection = Octonica.ClickHouseClient.ClickHouseConnection;

namespace Kronikol.Extensions.ClickHouse.Octonica;

/// <summary>
/// Typed registration helpers pairing <c>Kronikol.Extensions.ClickHouse</c> with the
/// Octonica.ClickHouseClient driver: same wrapping as the driver-agnostic methods, with
/// <see cref="OctonicaClickHouseDriverAdapter"/> wired in.
/// </summary>
public static class OctonicaClickHouseTrackingExtensions
{
    /// <summary>
    /// Wraps the Octonica connection in a <see cref="TrackingClickHouseConnection"/> with the
    /// Octonica driver adapter paired. An adapter already set on <paramref name="options"/> is
    /// respected.
    /// </summary>
    public static TrackingClickHouseConnection WithOctonicaClickHouseTestTracking(
        this OctonicaConnection connection,
        ClickHouseTrackingOptions? options = null)
    {
        var opts = options ?? new ClickHouseTrackingOptions();
        opts.DriverAdapter ??= OctonicaClickHouseDriverAdapter.Instance;
        return connection.WithClickHouseTestTracking(opts);
    }

    /// <summary>
    /// Registers ClickHouse test tracking (see
    /// <see cref="ClickHouseServiceCollectionExtensions.AddClickHouseTestTracking"/>) with the
    /// Octonica driver adapter paired.
    /// </summary>
    public static IServiceCollection AddOctonicaClickHouseTestTracking(
        this IServiceCollection services,
        Action<ClickHouseTrackingOptions>? configure = null)
        => services.AddClickHouseTestTracking(options =>
        {
            configure?.Invoke(options);
            options.DriverAdapter ??= OctonicaClickHouseDriverAdapter.Instance;
        });
}
