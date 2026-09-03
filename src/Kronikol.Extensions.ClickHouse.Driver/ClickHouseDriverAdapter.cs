using System.Data.Common;
using ClickHouse.Driver.ADO;

namespace Kronikol.Extensions.ClickHouse.Driver;

/// <summary>
/// Pairing adapter for ClickHouse.Driver, the official ClickHouse .NET client (HTTP).
/// Same lineage and same quirk as ClickHouse.Client: the driver's <c>ExecuteNonQuery</c> return
/// value is parsed from the HTTP response body, which is empty for an INSERT, so it always
/// reports 0; the real count arrives in the <c>X-ClickHouse-Summary</c> response header the
/// driver surfaces as <see cref="ClickHouseCommand.QueryStats"/>.
/// </summary>
public sealed class ClickHouseDriverAdapter : IClickHouseDriverAdapter
{
    public static ClickHouseDriverAdapter Instance { get; } = new();

    public int ResolveRowsAffected(DbCommand innerCommand, int driverResult)
        => innerCommand is ClickHouseCommand command
            ? Resolve(command.QueryStats, driverResult)
            : driverResult;

    internal static int Resolve(QueryStats? stats, int driverResult)
        => stats is { WrittenRows: > 0 }
            ? (int)Math.Min(stats.WrittenRows, int.MaxValue)
            : driverResult;
}
