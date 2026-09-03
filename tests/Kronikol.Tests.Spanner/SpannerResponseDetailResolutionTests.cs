using Kronikol.Extensions.Spanner;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.Spanner;

/// <summary>
/// Unset ResponseDetail follows the effective verbosity: FullRows at Raw/Detailed,
/// RowCountAndColumns at Summarised. An explicit setting always wins.
/// </summary>
public class SpannerResponseDetailResolutionTests : IDisposable
{
    public void Dispose() => TestPhaseContext.Reset();

    [Theory]
    [InlineData(SpannerTrackingVerbosity.Raw, SpannerResponseDetail.FullRows)]
    [InlineData(SpannerTrackingVerbosity.Detailed, SpannerResponseDetail.FullRows)]
    [InlineData(SpannerTrackingVerbosity.Summarised, SpannerResponseDetail.RowCountAndColumns)]
    public void Unset_detail_follows_verbosity(SpannerTrackingVerbosity verbosity, SpannerResponseDetail expected)
    {
        var options = new SpannerTrackingOptions { Verbosity = verbosity };
        Assert.Equal(expected, options.ResolveResponseDetail());
    }

    [Theory]
    [InlineData(SpannerResponseDetail.RowCountOnly)]
    [InlineData(SpannerResponseDetail.RowCountAndColumns)]
    [InlineData(SpannerResponseDetail.FullRows)]
    public void Explicit_detail_wins_regardless_of_verbosity(SpannerResponseDetail configured)
    {
        var detailed = new SpannerTrackingOptions { Verbosity = SpannerTrackingVerbosity.Detailed, ResponseDetail = configured };
        var summarised = new SpannerTrackingOptions { Verbosity = SpannerTrackingVerbosity.Summarised, ResponseDetail = configured };
        Assert.Equal(configured, detailed.ResolveResponseDetail());
        Assert.Equal(configured, summarised.ResolveResponseDetail());
    }

    [Fact]
    public void Phase_verbosity_overrides_are_respected()
    {
        var options = new SpannerTrackingOptions
        {
            Verbosity = SpannerTrackingVerbosity.Detailed,
            SetupVerbosity = SpannerTrackingVerbosity.Summarised,
        };

        try
        {
            TestPhaseContext.Current = TestPhase.Setup;
            Assert.Equal(SpannerResponseDetail.RowCountAndColumns, options.ResolveResponseDetail());
            TestPhaseContext.Current = TestPhase.Action;
            Assert.Equal(SpannerResponseDetail.FullRows, options.ResolveResponseDetail());
        }
        finally
        {
            TestPhaseContext.Reset();
        }
    }
}
