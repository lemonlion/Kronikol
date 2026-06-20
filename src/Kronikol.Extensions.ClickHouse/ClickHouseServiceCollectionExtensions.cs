using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Extension methods for registering ClickHouse test tracking via dependency injection.
/// </summary>
public static class ClickHouseServiceCollectionExtensions
{
    /// <summary>
    /// Decorates all registered <see cref="DbConnection"/> services whose runtime type is a ClickHouse
    /// connection (from ClickHouse.Client or Octonica.ClickHouseClient) with a
    /// <see cref="TrackingClickHouseConnection"/>. Non-ClickHouse connections are left untouched.
    /// <para>
    /// An <see cref="IHttpContextAccessor"/> is resolved from DI (if registered) and wired
    /// into the tracking options automatically.
    /// </para>
    /// </summary>
    public static IServiceCollection AddClickHouseTestTracking(
        this IServiceCollection services,
        Action<ClickHouseTrackingOptions>? configure = null)
    {
        var options = new ClickHouseTrackingOptions();
        configure?.Invoke(options);

        services.DecorateAll<DbConnection>((sp, inner) =>
        {
            if (!IsClickHouseConnection(inner)) return inner;
            options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();
            return new TrackingClickHouseConnection(inner, options, options.HttpContextAccessor);
        });

        return services;
    }

    /// <summary>
    /// Determines whether the given connection is a ClickHouse connection from a supported client,
    /// without taking a hard dependency on either client package. Both ClickHouse.Client and
    /// Octonica.ClickHouseClient name their connection type <c>ClickHouseConnection</c>.
    /// </summary>
    internal static bool IsClickHouseConnection(DbConnection connection)
    {
        for (var type = connection.GetType(); type is not null; type = type.BaseType)
        {
            if (type.Name == "ClickHouseConnection")
                return true;
        }
        return false;
    }
}
