using System.Data.Common;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Provides extension methods for wrapping a ClickHouse <see cref="DbConnection"/> with test tracking.
/// Works with ClickHouse.Driver (<c>ClickHouse.Driver.ADO.ClickHouseConnection</c>), ClickHouse.Client
/// (<c>ClickHouse.Client.ADO.ClickHouseConnection</c>) and Octonica.ClickHouseClient
/// (<c>Octonica.ClickHouseClient.ClickHouseConnection</c>), since all three derive from <see cref="DbConnection"/>.
/// ClickHouse.Driver's <c>IClickHouseClient</c> is not a connection: its pairing package,
/// <c>Kronikol.Extensions.ClickHouse.Driver</c>, wraps it.
/// </summary>
public static class ClickHouseConnectionExtensions
{
    /// <summary>
    /// Wraps the ClickHouse <see cref="DbConnection"/> in a <see cref="TrackingClickHouseConnection"/>
    /// that intercepts all SQL operations for inclusion in test diagrams.
    /// </summary>
    public static TrackingClickHouseConnection WithClickHouseTestTracking(
        this DbConnection connection,
        ClickHouseTrackingOptions? options = null)
    {
        var opts = options ?? new ClickHouseTrackingOptions();
        return new TrackingClickHouseConnection(connection, opts, opts.HttpContextAccessor);
    }
}
