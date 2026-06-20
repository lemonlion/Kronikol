using System.Data.Common;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Provides extension methods for wrapping a ClickHouse <see cref="DbConnection"/> with test tracking.
/// Works with both ClickHouse.Client (<c>ClickHouse.Client.ADO.ClickHouseConnection</c>) and
/// Octonica.ClickHouseClient (<c>Octonica.ClickHouseClient.ClickHouseConnection</c>), since both
/// derive from <see cref="DbConnection"/>.
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
