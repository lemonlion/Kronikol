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
}
