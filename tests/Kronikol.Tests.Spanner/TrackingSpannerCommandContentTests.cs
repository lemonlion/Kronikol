using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
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

    // A phase's own verbosity decides its response note too: the command read the rows, and counted the rows a statement
    // changed, at the base verbosity, so a Detailed phase of a Summarised command had a count where it lists the rows,
    // and nothing where it says how many changed.
    private SpannerTrackingOptions SummarisedWithADetailedAction() => new()
    {
        CurrentTestInfoFetcher = () => ("TestMethod", _testId),
        Verbosity = SpannerTrackingVerbosity.Summarised,
        ActionVerbosity = SpannerTrackingVerbosity.Detailed,
    };

    private RequestResponseLog Response() =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == RequestResponseType.Response);

    [Fact]
    public void A_detailed_phase_of_a_summarised_command_lists_the_rows()
    {
        TestPhaseContext.Reset();
        var options = SummarisedWithADetailedAction();
        using var connection = new TrackingSpannerConnection(new FailureFakeDbConnection(), options);
        using var command = new TrackingSpannerCommand(new RowsFakeDbCommand(), connection, options)
        {
            CommandText = "SELECT id, name FROM breakfasts",
        };

        using (var reader = command.ExecuteReader()) { while (reader.Read()) { } }

        var response = Response();
        Assert.Equal("2 rows [id, name]", response.Content);
        Assert.Equal("2 rows [id, name]", response.SetupVariant!.Content);
        Assert.Contains("\"name\":\"Pancakes\"", response.ActionVariant!.Content);
    }

    [Fact]
    public void A_detailed_phase_of_a_summarised_command_says_how_many_rows_changed_with_LogResponseContent_off()
    {
        TestPhaseContext.Reset();
        var options = SummarisedWithADetailedAction();
        options.LogResponseContent = false;
        using var connection = new TrackingSpannerConnection(new FailureFakeDbConnection(), options);
        using var command = new TrackingSpannerCommand(new RowsFakeDbCommand(), connection, options)
        {
            CommandText = "UPDATE breakfasts SET name = 'Toast'",
        };

        command.ExecuteNonQuery();

        var response = Response();
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("2 rows affected", response.ActionVariant!.Content);
    }
}

/// <summary>A command whose query reads two breakfasts and whose statement changes two rows.</summary>
internal sealed class RowsFakeDbCommand : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection { get; } = new EmptyFakeParameterCollection();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(int));
        table.Columns.Add("name", typeof(string));
        table.Rows.Add(1, "Pancakes");
        table.Rows.Add(2, "Waffles");
        return table.CreateDataReader();
    }

    public override int ExecuteNonQuery() => 2;
    public override object? ExecuteScalar() => null;
    public override void Prepare() { }
    public override void Cancel() { }
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
}
