using Microsoft.Data.Sqlite;
using Kronikol.Extensions.Sqlite;
using Kronikol.Sql;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.Sqlite;

/// <summary>
/// Response payload capture through a real in-memory SQLite database: SELECT detail follows the
/// effective verbosity (actual rows at Raw/Detailed, count+columns at Summarised) unless
/// ResponseDetail is set explicitly; scalars log their value. Before 3.0.74 this wrapper logged
/// empty responses for readers and scalars.
/// </summary>
public class TrackingSqliteResponseCaptureTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly SqliteTrackingOptions _options;
    private readonly SqliteConnection _inner;
    private readonly TrackingSqliteConnection _tracking;

    public TrackingSqliteResponseCaptureTests()
    {
        TrackingComponentRegistry.Clear();
        _options = new SqliteTrackingOptions { CurrentTestInfoFetcher = () => ("TestMethod", _testId) };
        _inner = new SqliteConnection("Data Source=:memory:");
        _tracking = new TrackingSqliteConnection(_inner, _options);
        _tracking.Open();

        // Seed through the inner connection so only the facts' own commands are logged.
        using var setup = _inner.CreateCommand();
        setup.CommandText = "CREATE TABLE breakfasts (id INTEGER, name TEXT); INSERT INTO breakfasts VALUES (1, 'Pancakes'), (2, 'Waffles')";
        setup.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _tracking.Dispose();
        TrackingComponentRegistry.Clear();
    }

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    public static TheoryData<string> ExecutePaths => new()
    {
        "ExecuteNonQuery", "ExecuteNonQueryAsync", "ExecuteScalar", "ExecuteScalarAsync", "ExecuteReader", "ExecuteReaderAsync"
    };

    private static async Task Execute(System.Data.Common.DbCommand cmd, string path)
    {
        switch (path)
        {
            case "ExecuteNonQuery": cmd.ExecuteNonQuery(); break;
            case "ExecuteNonQueryAsync": await cmd.ExecuteNonQueryAsync(); break;
            case "ExecuteScalar": cmd.ExecuteScalar(); break;
            case "ExecuteScalarAsync": await cmd.ExecuteScalarAsync(); break;
            case "ExecuteReader": cmd.ExecuteReader().Dispose(); break;
            case "ExecuteReaderAsync": await (await cmd.ExecuteReaderAsync()).DisposeAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }
    }

    [Theory]
    [MemberData(nameof(ExecutePaths))]
    public async Task A_statement_that_fails_records_an_Error_response_with_its_message_and_rethrows_the_same_exception(string path)
    {
        using var cmd = _tracking.CreateCommand();
        cmd.CommandText = "SELECT * FROM no_such_table_126";

        var thrown = await Record.ExceptionAsync(() => Execute(cmd, path));

        Assert.IsType<SqliteException>(thrown);
        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
        Assert.Equal("Error", logs[1].StatusCode?.Value?.ToString());
        Assert.Equal(thrown.Message, logs[1].Content);
    }

    [Theory]
    [InlineData("Commit")]
    [InlineData("Rollback")]
    public void A_commit_or_rollback_that_fails_records_an_Error_response_with_its_message_and_rethrows_the_same_exception(string operation)
    {
        var rejected = new InvalidOperationException("the transaction was aborted by the server");
        using var tx = new TrackingSqliteTransaction(new RejectingFakeDbTransaction(rejected), _tracking);

        var thrown = Record.Exception(() =>
        {
            if (operation == "Commit") tx.Commit();
            else tx.Rollback();
        });

        Assert.Same(rejected, thrown);
        var logs = GetLogsForTest();
        // BEGIN TRANSACTION and its response, then the statement that failed and its response.
        Assert.Equal(4, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[2].Type);
        Assert.Equal(RequestResponseType.Response, logs[3].Type);
        Assert.Equal(logs[2].RequestResponseId, logs[3].RequestResponseId);
        Assert.Equal("Error", logs[3].StatusCode?.Value?.ToString());
        Assert.Equal(rejected.Message, logs[3].Content);
    }

    private string? RunSelectAndGetResponseContent()
    {
        using (var cmd = _tracking.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM breakfasts ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) { }
        }
        return GetLogsForTest().Last(l => l.Type == RequestResponseType.Response).Content;
    }

    [Fact]
    public void Select_at_default_detailed_verbosity_logs_actual_rows()
    {
        var content = RunSelectAndGetResponseContent();

        Assert.NotNull(content);
        Assert.Contains("\"name\":\"Pancakes\"", content);
        Assert.Contains("\"name\":\"Waffles\"", content);
    }

    [Fact]
    public void Select_at_summarised_verbosity_logs_count_and_columns()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Summarised;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    [Fact]
    public void Explicit_ResponseDetail_wins_over_verbosity()
    {
        _options.ResponseDetail = SqlResponseDetail.RowCountAndColumns;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    [Fact]
    public void Scalar_logs_its_value()
    {
        using (var cmd = _tracking.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM breakfasts";
            cmd.ExecuteScalar();
        }

        Assert.Equal("2", GetLogsForTest().Last(l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public void LogResponseContent_false_keeps_responses_empty()
    {
        _options.LogResponseContent = false;
        Assert.Null(RunSelectAndGetResponseContent());
    }
}

/// <summary>A transaction whose commit and rollback fail, as a driver's do when the server rejects them.</summary>
internal sealed class RejectingFakeDbTransaction(Exception rejection) : System.Data.Common.DbTransaction
{
    public override System.Data.IsolationLevel IsolationLevel => System.Data.IsolationLevel.ReadCommitted;
    protected override System.Data.Common.DbConnection? DbConnection => null;
    public override void Commit() => throw rejection;
    public override void Rollback() => throw rejection;
}
