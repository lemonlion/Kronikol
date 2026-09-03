using System.Data.Common;

namespace Kronikol.Extensions.ClickHouse.Octonica;

/// <summary>
/// Pairing adapter for the Octonica.ClickHouseClient (native TCP) driver.
/// Octonica's <c>ExecuteNonQuery</c> already returns real written-row counts (verified live:
/// a single-row INSERT returns 1, a three-row VALUES INSERT returns 3), so this passes the
/// driver's value through unchanged.
/// </summary>
public sealed class OctonicaClickHouseDriverAdapter : IClickHouseDriverAdapter
{
    public static OctonicaClickHouseDriverAdapter Instance { get; } = new();

    public int ResolveRowsAffected(DbCommand innerCommand, int driverResult) => driverResult;
}
