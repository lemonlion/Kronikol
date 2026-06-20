using System.Data;
using Kronikol.Extensions.ClickHouse;
using Kronikol.Sql;
using Kronikol.Tests.ClickHouse.Fakes;
using Kronikol.Tracking;
using Xunit;

namespace Kronikol.Tests.ClickHouse;

public class TrackingClickHouseCommandTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly FakeDbConnection _fakeConnection = new();
    private readonly ClickHouseTrackingOptions _options;
    private readonly TrackingClickHouseConnection _trackingConnection;

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    public TrackingClickHouseCommandTests()
    {
        TrackingComponentRegistry.Clear();

        _options = new ClickHouseTrackingOptions
        {
            CurrentTestInfoFetcher = () => ("TestMethod", _testId),
            ServiceName = "Analytics",
            CallerName = "TestCaller"
        };

        _trackingConnection = new TrackingClickHouseConnection(_fakeConnection, _options);
    }

    public void Dispose()
    {
        _trackingConnection.Dispose();
        TrackingComponentRegistry.Clear();
    }

    private TrackingClickHouseCommand CreateCommand(string sql, CommandType type = CommandType.Text)
    {
        var cmd = (TrackingClickHouseCommand)_trackingConnection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = type;
        return cmd;
    }

    // ─── Logging ────────────────────────────────────────────────

    [Fact]
    public void ExecuteReader_logs_request_and_response()
    {
        using var cmd = CreateCommand("SELECT * FROM events");
        using var reader = cmd.ExecuteReader();
        reader.Close();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public async Task ExecuteReaderAsync_logs_request_and_response()
    {
        using var cmd = CreateCommand("SELECT * FROM events");
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Close();

        Assert.Equal(2, GetLogsForTest().Length);
    }

    [Fact]
    public void ExecuteNonQuery_logs_insert_with_test_metadata()
    {
        using var cmd = CreateCommand("INSERT INTO events (name) VALUES ('x')");
        cmd.ExecuteNonQuery();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        var request = logs[0];
        Assert.Equal("TestMethod", request.TestName);
        Assert.Equal(_testId, request.TestId);
        Assert.Equal("Analytics", request.ServiceName);
        Assert.Equal("TestCaller", request.CallerName);
        Assert.Equal("ClickHouse", request.DependencyCategory);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_logs_correctly()
    {
        using var cmd = CreateCommand("INSERT INTO events (name) VALUES ('x')");
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, GetLogsForTest().Length);
    }

    [Fact]
    public void ExecuteScalar_logs_query()
    {
        using var cmd = CreateCommand("SELECT count() FROM events");
        cmd.ExecuteScalar();

        Assert.Equal(2, GetLogsForTest().Length);
    }

    [Fact]
    public async Task ExecuteScalarAsync_logs_correctly()
    {
        using var cmd = CreateCommand("SELECT count() FROM events");
        await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, GetLogsForTest().Length);
    }

    [Fact]
    public void Request_and_response_share_trace_and_request_response_ids()
    {
        using var cmd = CreateCommand("SELECT * FROM events");
        using var reader = cmd.ExecuteReader();
        reader.Close();

        var logs = GetLogsForTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void Response_includes_rows_affected_for_non_query()
    {
        var fakeCmd = new FakeDbCommand { NonQueryResult = 5 };
        using var cmd = new TrackingClickHouseCommand(fakeCmd, _trackingConnection);
        cmd.CommandText = "ALTER TABLE events DELETE WHERE active = 0";
        cmd.ExecuteNonQuery();

        Assert.Equal("5 rows affected", GetLogsForTest()[1].Content);
    }

    // ─── No test info → no logging ──────────────────────────────

    [Fact]
    public void No_test_info_produces_no_logs()
    {
        _options.CurrentTestInfoFetcher = () => null;
        using var cmd = CreateCommand("SELECT * FROM events");
        cmd.ExecuteReader();

        Assert.Empty(GetLogsForTest());
    }

    // ─── Excluded operations ────────────────────────────────────

    [Fact]
    public void Excluded_operation_produces_no_logs()
    {
        _options.ExcludedOperations = [UnifiedSqlOperation.Select];
        using var cmd = CreateCommand("SELECT * FROM events");
        cmd.ExecuteReader();

        Assert.Empty(GetLogsForTest());
    }

    // ─── ClickHouse dialect labels ──────────────────────────────

    [Fact]
    public void Detailed_method_classifies_alter_update_mutation_as_Update()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Detailed;
        using var cmd = CreateCommand("ALTER TABLE events UPDATE status = 'done' WHERE id = 1");
        cmd.ExecuteNonQuery();

        Assert.Equal("UPDATE events", GetLogsForTest()[0].Method.Value?.ToString());
    }

    [Fact]
    public void Detailed_method_classifies_optimize()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Detailed;
        using var cmd = CreateCommand("OPTIMIZE TABLE events FINAL");
        cmd.ExecuteNonQuery();

        Assert.Equal("OPTIMIZE events", GetLogsForTest()[0].Method.Value?.ToString());
    }

    [Fact]
    public void Detailed_verbosity_includes_table_in_method()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Detailed;
        using var cmd = CreateCommand("SELECT * FROM events WHERE id = 1");
        cmd.ExecuteReader();

        Assert.Equal("SELECT FROM events", GetLogsForTest()[0].Method.Value?.ToString());
    }

    [Fact]
    public void Raw_verbosity_uses_keyword_as_method()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Raw;
        using var cmd = CreateCommand("SELECT * FROM events WHERE id = 1");
        cmd.ExecuteReader();

        Assert.Equal("SELECT", GetLogsForTest()[0].Method.Value?.ToString());
    }

    // ─── URI construction ───────────────────────────────────────

    [Fact]
    public void Uri_uses_clickhouse_scheme_with_datasource_database_and_table()
    {
        _options.Verbosity = SqlTrackingVerbosityLevel.Detailed;
        using var cmd = CreateCommand("SELECT * FROM events");
        cmd.ExecuteReader();

        var uri = GetLogsForTest()[0].Uri.ToString();
        Assert.StartsWith("clickhouse://", uri);
        Assert.Contains("ch-host:8123", uri);
        Assert.Contains("analytics", uri);
        Assert.Contains("events", uri);
    }

    // ─── Parameters ─────────────────────────────────────────────

    [Fact]
    public void LogParameters_true_includes_parameters_in_content()
    {
        _options.LogParameters = true;
        _options.Verbosity = SqlTrackingVerbosityLevel.Raw;
        using var cmd = CreateCommand("SELECT * FROM events WHERE id = @id");
        var param = cmd.CreateParameter();
        param.ParameterName = "@id";
        param.Value = 42;
        cmd.Parameters.Add(param);

        cmd.ExecuteReader();

        Assert.Contains("@id=42", GetLogsForTest()[0].Content);
    }

    // ─── Delegation / disposal ──────────────────────────────────

    [Fact]
    public void CommandText_delegates_to_inner()
    {
        using var cmd = CreateCommand("SELECT 1");
        Assert.Equal("SELECT 1", cmd.CommandText);
        cmd.CommandText = "SELECT 2";
        Assert.Equal("SELECT 2", cmd.CommandText);
    }

    [Fact]
    public void Dispose_disposes_inner()
    {
        var innerCmd = new FakeDbCommand();
        var cmd = new TrackingClickHouseCommand(innerCmd, _trackingConnection);
        cmd.Dispose();
        Assert.True(innerCmd.WasDisposed);
    }

    [Fact]
    public void InvocationCount_increments_on_execution()
    {
        using var cmd = CreateCommand("SELECT 1");
        cmd.ExecuteScalar();
        Assert.Equal(1, _trackingConnection.InvocationCount);
        Assert.True(_trackingConnection.WasInvoked);
    }
}
