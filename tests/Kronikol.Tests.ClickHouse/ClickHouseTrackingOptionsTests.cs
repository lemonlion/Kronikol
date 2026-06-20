using Kronikol.Extensions.ClickHouse;
using Kronikol.Sql;
using Xunit;

namespace Kronikol.Tests.ClickHouse;

public class ClickHouseTrackingOptionsTests
{
    [Fact]
    public void Default_options_use_clickhouse_values()
    {
        var options = new ClickHouseTrackingOptions();
        Assert.Equal("ClickHouse", options.ServiceName);
        Assert.Equal("ClickHouse", options.DependencyCategory);
        Assert.Equal("clickhouse", options.UriScheme);
    }

    [Fact]
    public void Options_inherit_from_SqlTrackingOptionsBase()
    {
        var options = new ClickHouseTrackingOptions();
        Assert.IsAssignableFrom<SqlTrackingOptionsBase>(options);
    }

    [Fact]
    public void Default_verbosity_is_Detailed()
    {
        var options = new ClickHouseTrackingOptions();
        Assert.Equal(SqlTrackingVerbosityLevel.Detailed, options.Verbosity);
    }

    [Fact]
    public void ExcludedOperations_defaults_to_empty()
    {
        var options = new ClickHouseTrackingOptions();
        Assert.Empty(options.ExcludedOperations);
    }

    [Fact]
    public void ExcludedOperations_can_be_configured()
    {
        var options = new ClickHouseTrackingOptions
        {
            ExcludedOperations = [UnifiedSqlOperation.Select]
        };
        Assert.Contains(UnifiedSqlOperation.Select, options.ExcludedOperations);
    }
}

public class DependencyPaletteClickHouseTests
{
    [Fact]
    public void ClickHouse_category_resolves_to_Database()
    {
        Assert.Equal(DependencyType.Database, DependencyPalette.Resolve("ClickHouse"));
    }
}
