using Kronikol.Extensions.Spanner;
using Kronikol.Tracking;

namespace Kronikol.Tests.Spanner;

/// <summary>
/// A command's request content per phase. The command chose its content once, from the base verbosity, so with a Raw
/// base and a Detailed phase the Detailed phase showed the SQL text and its parameters whatever
/// <see cref="SpannerTrackingOptions.LogSqlText"/> said.
/// </summary>
// TrackingSpannerConnection registers with the process-wide TrackingComponentRegistry, which other classes clear.
[Collection("TrackingComponentRegistry")]
public class TrackingSpannerCommandContentTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    [Fact]
    public void A_Detailed_phase_of_a_command_logged_at_Raw_leaves_the_SQL_out_when_LogSqlText_is_off()
    {
        TestPhaseContext.Reset();
        var options = new SpannerTrackingOptions
        {
            CurrentTestInfoFetcher = () => ("TestMethod", _testId),
            Verbosity = SpannerTrackingVerbosity.Raw,
            ActionVerbosity = SpannerTrackingVerbosity.Detailed,
            LogSqlText = false,
        };
        using var connection = new TrackingSpannerConnection(new FailureFakeDbConnection(), options);
        using var command = new TrackingSpannerCommand(new FailureFakeDbCommand(new InvalidOperationException("not run")), connection, options)
        {
            CommandText = "SELECT * FROM Orders",
        };

        command.LogRequest();

        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId);
        Assert.StartsWith("SELECT * FROM Orders", log.Content);
        Assert.NotNull(log.ActionVariant);
        Assert.Null(log.ActionVariant.Content);
        Assert.StartsWith("SELECT * FROM Orders", log.SetupVariant!.Content);
    }
}
