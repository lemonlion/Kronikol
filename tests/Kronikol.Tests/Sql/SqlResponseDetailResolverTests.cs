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

    // ─── The detail a call's rows are kept at ─────────────────

    private static SqlTrackingOptionsBase SummarisedWithADetailedAction(SqlResponseDetail? configured = null) => new()
    {
        Verbosity = SqlTrackingVerbosityLevel.Summarised,
        ActionVerbosity = SqlTrackingVerbosityLevel.Detailed,
        ResponseDetail = configured,
    };

    [Fact]
    public void Rows_are_kept_at_the_most_detail_any_verbosity_a_call_may_be_drawn_at_shows()
    {
        TestPhaseContext.Reset();

        Assert.Equal(SqlResponseDetail.FullRows, SqlResponseDetailResolver.ResolveKept(SummarisedWithADetailedAction()));
    }

    [Fact]
    public void Rows_are_kept_at_the_effective_detail_once_the_phase_is_known()
    {
        try
        {
            TestPhaseContext.Current = TestPhase.Setup;
            Assert.Equal(SqlResponseDetail.RowCountAndColumns, SqlResponseDetailResolver.ResolveKept(SummarisedWithADetailedAction()));
        }
        finally
        {
            TestPhaseContext.Reset();
        }
    }

    [Fact]
    public void Rows_are_kept_at_an_explicit_detail_whatever_the_phases_show()
    {
        TestPhaseContext.Reset();

        Assert.Equal(SqlResponseDetail.RowCountOnly,
            SqlResponseDetailResolver.ResolveKept(SummarisedWithADetailedAction(SqlResponseDetail.RowCountOnly)));
    }
}
