using System.Data.Common;
using ClickHouse.Client.ADO;

namespace Kronikol.Extensions.ClickHouse.Client;

/// <summary>
/// Pairing adapter for the ClickHouse.Client (HTTP) driver.
/// The driver's <c>ExecuteNonQuery</c> return value is parsed from the HTTP response body, which is
/// empty for an INSERT, so it always reports 0; the real count arrives in the
/// <c>X-ClickHouse-Summary</c> response header the driver surfaces as
/// <see cref="ClickHouseCommand.QueryStats"/>.
/// </summary>
public sealed class ClickHouseClientDriverAdapter : IClickHouseDriverAdapter
{
    public static ClickHouseClientDriverAdapter Instance { get; } = new();

    public int ResolveRowsAffected(DbCommand innerCommand, int driverResult)
        => innerCommand is ClickHouseCommand command
            ? Resolve(command.QueryStats, driverResult)
            : driverResult;

    internal static int Resolve(QueryStats? stats, int driverResult)
        => stats is { WrittenRows: > 0 }
            ? (int)Math.Min(stats.WrittenRows, int.MaxValue)
            : driverResult;
}
