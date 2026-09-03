using System.Data.Common;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Driver-specific hooks supplied by a pairing package (<c>Kronikol.Extensions.ClickHouse.Client</c>
/// for ClickHouse.Client, <c>Kronikol.Extensions.ClickHouse.Octonica</c> for Octonica.ClickHouseClient).
/// The main extension works purely against <see cref="DbConnection"/>; an adapter gives it
/// compile-time access to what that abstraction hides — e.g. ClickHouse.Client reports rows
/// written only via its <c>QueryStats</c> property, never via <c>ExecuteNonQuery</c>'s return value.
/// </summary>
public interface IClickHouseDriverAdapter
{
    /// <summary>
    /// The rows-affected count to log for a completed non-query, given the inner driver command
    /// (already executed) and the value the driver's <c>ExecuteNonQuery</c> returned.
    /// </summary>
    int ResolveRowsAffected(DbCommand innerCommand, int driverResult);
}
