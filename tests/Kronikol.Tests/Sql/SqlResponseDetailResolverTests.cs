using Kronikol.Sql;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.Sql;

public class SqlResponseDetailResolverTests
{
    [Fact]
    public void Unset_detail_follows_verbosity()
    {
        Assert.Equal(SqlResponseDetail.FullRows,
            SqlResponseDetailResolver.Resolve(null, effectiveVerbosityIsSummarised: false));
        Assert.Equal(SqlResponseDetail.RowCountAndColumns,
            SqlResponseDetailResolver.Resolve(null, effectiveVerbosityIsSummarised: true));
    }

    [Theory]
    [InlineData(SqlResponseDetail.RowCountOnly)]
    [InlineData(SqlResponseDetail.RowCountAndColumns)]
    [InlineData(SqlResponseDetail.FullRows)]
    public void Explicit_detail_wins_regardless_of_verbosity(SqlResponseDetail configured)
    {
        Assert.Equal(configured, SqlResponseDetailResolver.Resolve(configured, effectiveVerbosityIsSummarised: false));
        Assert.Equal(configured, SqlResponseDetailResolver.Resolve(configured, effectiveVerbosityIsSummarised: true));
    }

    [Fact]
    public void Options_overload_uses_base_verbosity()
    {
        Assert.Equal(SqlResponseDetail.FullRows, SqlResponseDetailResolver.Resolve(
            new SqlTrackingOptionsBase { Verbosity = SqlTrackingVerbosityLevel.Detailed }));
        Assert.Equal(SqlResponseDetail.FullRows, SqlResponseDetailResolver.Resolve(
            new SqlTrackingOptionsBase { Verbosity = SqlTrackingVerbosityLevel.Raw }));
        Assert.Equal(SqlResponseDetail.RowCountAndColumns, SqlResponseDetailResolver.Resolve(
            new SqlTrackingOptionsBase { Verbosity = SqlTrackingVerbosityLevel.Summarised }));
    }

    [Fact]
    public void Options_overload_respects_phase_verbosity_overrides()
    {
        var options = new SqlTrackingOptionsBase
        {
            Verbosity = SqlTrackingVerbosityLevel.Detailed,
            SetupVerbosity = SqlTrackingVerbosityLevel.Summarised,
        };

        try
        {
            TestPhaseContext.Current = TestPhase.Setup;
            Assert.Equal(SqlResponseDetail.RowCountAndColumns, SqlResponseDetailResolver.Resolve(options));
            TestPhaseContext.Current = TestPhase.Action;
            Assert.Equal(SqlResponseDetail.FullRows, SqlResponseDetailResolver.Resolve(options));
        }
        finally
        {
            TestPhaseContext.Reset();
        }
    }
}
