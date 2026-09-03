using Kronikol.Constants;
using Kronikol.Sql;

namespace Kronikol.Extensions.ClickHouse;

/// <summary>
/// Configuration options for the ClickHouse tracking extension.
/// Works with both ClickHouse.Client and Octonica.ClickHouseClient.
/// </summary>
public record ClickHouseTrackingOptions : SqlTrackingOptionsBase
{
    public ClickHouseTrackingOptions()
    {
        ServiceName = "ClickHouse";
        DependencyCategory = DependencyCategories.ClickHouse;
        UriScheme = "clickhouse";
    }

    /// <summary>
    /// Driver-specific hooks, normally supplied by a pairing package
    /// (<c>Kronikol.Extensions.ClickHouse.Client</c> or <c>Kronikol.Extensions.ClickHouse.Octonica</c>).
    /// Without one, rows-affected logging uses the driver's <c>ExecuteNonQuery</c> return value as-is —
    /// which for ClickHouse.Client is always 0 on INSERT (the real count only surfaces via its
    /// <c>QueryStats</c>), so pair the matching package for accurate counts.
    /// </summary>
    public IClickHouseDriverAdapter? DriverAdapter { get; set; }
}
