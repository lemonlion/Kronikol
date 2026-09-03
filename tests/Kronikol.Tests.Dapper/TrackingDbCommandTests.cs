using System.Data;
using Kronikol.Tests.Dapper.Fakes;
using Kronikol.Tracking;

namespace Kronikol.Tests.Dapper;

public class TrackingDbCommandTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly FakeDbConnection _fakeConnection = new();
    private readonly DapperTrackingOptions _options;
    private readonly TrackingDbConnection _trackingConnection;

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    public TrackingDbCommandTests()
    {
        TrackingComponentRegistry.Clear();

        _options = new DapperTrackingOptions
        {
            CurrentTestInfoFetcher = () => ("TestMethod", _testId),
            ServiceName = "TestDB",
            CallerName = "TestCaller"
        };

        _trackingConnection = new TrackingDbConnection(_fakeConnection, _options);
    }

    public void Dispose()
    {
        _trackingConnection.Dispose();
        TrackingComponentRegistry.Clear();
    }

    private TrackingDbCommand CreateCommand(string sql, CommandType type = CommandType.Text)
    {
        var cmd = (TrackingDbCommand)_trackingConnection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandType = type;
        return cmd;
    }

    // ─── Response detail follows verbosity ──────────────────────
    // Unset ResponseDetail follows the effective verbosity: actual row data at Raw/Detailed,
    // a count+columns summary at Summarised. An explicit setting always wins.

    private string? RunSelectAndGetResponseContent()
    {
        var fakeCmd = new FakeDbCommand { ReaderResult = new FakeRowDbDataReader() };
        using var cmd = new TrackingDbCommand(fakeCmd, _trackingConnection, _options);
        cmd.CommandText = "SELECT id, name FROM breakfasts";
        using (var reader = cmd.ExecuteReader())
        {
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
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    [Fact]
    public void Explicit_ResponseDetail_wins_over_verbosity()
    {
        _options.ResponseDetail = Kronikol.Sql.SqlResponseDetail.RowCountAndColumns;
        Assert.Equal("2 rows [id, name]", RunSelectAndGetResponseContent());
    }

    // ─── Logging ────────────────────────────────────────────────

    [Fact]
    public void ExecuteReader_logs_request_and_response()
    {
        using var cmd = CreateCommand("SELECT * FROM Users");
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
        using var cmd = CreateCommand("SELECT * FROM Users");
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        reader.Close();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void ExecuteNonQuery_logs_insert()
    {
        using var cmd = CreateCommand("INSERT INTO Users (Name) VALUES ('test')");
        cmd.ExecuteNonQuery();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);

        var request = logs[0];
        Assert.Equal("TestMethod", request.TestName);
        Assert.Equal(_testId, request.TestId);
        Assert.Equal("TestDB", request.ServiceName);
        Assert.Equal("TestCaller", request.CallerName);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_logs_correctly()
    {
        using var cmd = CreateCommand("UPDATE Users SET Name = 'test'");
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void ExecuteScalar_logs_query()
    {
        using var cmd = CreateCommand("SELECT COUNT(*) FROM Users");
        cmd.ExecuteScalar();

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public async Task ExecuteScalarAsync_logs_correctly()
    {
        using var cmd = CreateCommand("SELECT COUNT(*) FROM Users");
        await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void Request_and_response_share_trace_and_request_response_ids()
    {
        using var cmd = CreateCommand("SELECT * FROM Users");
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
        using var cmd = new TrackingDbCommand(fakeCmd, _trackingConnection, _options);
        cmd.CommandText = "DELETE FROM Users WHERE Active = 0";
        cmd.ExecuteNonQuery();

        var logs = GetLogsForTest();
        var response = logs[1];
        Assert.Equal("5 rows affected", response.Content);
    }

    // ─── No test info → no logging ──────────────────────────────

    [Fact]
    public void No_test_info_produces_no_logs()
    {
        _options.CurrentTestInfoFetcher = () => null;
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        Assert.Empty(GetLogsForTest());
    }

    [Fact]
    public void Null_test_info_fetcher_produces_no_logs()
    {
        _options.CurrentTestInfoFetcher = null;
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        Assert.Empty(GetLogsForTest());
    }

    // ─── Excluded operations ────────────────────────────────────

    [Fact]
    public void Excluded_operation_produces_no_logs()
    {
        _options.ExcludedOperations = [DapperOperation.Query];
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        Assert.Empty(GetLogsForTest());
    }

    // ─── Parameters ─────────────────────────────────────────────

    [Fact]
    public void LogParameters_true_includes_parameters_in_content()
    {
        _options.LogParameters = true;
        _options.Verbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = @Id");
        var param = cmd.CreateParameter();
        param.ParameterName = "@Id";
        param.Value = 42;
        cmd.Parameters.Add(param);

        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Contains("@Id=42", request.Content);
        Assert.Contains("-- Parameters:", request.Content);
    }

    [Fact]
    public void LogParameters_false_excludes_parameters_from_content()
    {
        _options.LogParameters = false;
        _options.Verbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = @Id");
        var param = cmd.CreateParameter();
        param.ParameterName = "@Id";
        param.Value = 42;
        cmd.Parameters.Add(param);

        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.DoesNotContain("-- Parameters:", request.Content);
    }

    // ─── Verbosity levels ───────────────────────────────────────

    [Fact]
    public void Raw_verbosity_uses_keyword_as_method()
    {
        _options.Verbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = 1");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Equal("SELECT", request.Method.Value?.ToString());
    }

    [Fact]
    public void Detailed_verbosity_includes_table_in_method()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = 1");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Equal("SELECT FROM Users", request.Method.Value?.ToString());
    }

    [Fact]
    public void Summarised_verbosity_uses_keyword_only()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = 1");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Equal("SELECT", request.Method.Value?.ToString());
    }

    [Fact]
    public void Summarised_verbosity_has_null_content()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        _options.LogResponseContent = false;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = 1");
        using var reader = cmd.ExecuteReader();
        reader.Close();

        var response = GetLogsForTest()[1];
        Assert.Null(response.Content);
    }

    // ─── URI construction ───────────────────────────────────────

    [Fact]
    public void Detailed_uri_includes_datasource_database_and_table()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Contains("localhost", request.Uri.ToString());
        Assert.Contains("TestDb", request.Uri.ToString());
        Assert.Contains("Users", request.Uri.ToString());
    }

    [Fact]
    public void Summarised_uri_includes_database_and_table()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Contains("TestDb", request.Uri.ToString());
        Assert.Contains("Users", request.Uri.ToString());
    }

    [Fact]
    public void Raw_uri_includes_datasource_and_database()
    {
        _options.Verbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Contains("localhost", request.Uri.ToString());
        Assert.Contains("TestDb", request.Uri.ToString());
    }

    // ─── Delegation ─────────────────────────────────────────────

    [Fact]
    public void CommandText_delegates_to_inner()
    {
        using var cmd = CreateCommand("SELECT 1");
        Assert.Equal("SELECT 1", cmd.CommandText);
        cmd.CommandText = "SELECT 2";
        Assert.Equal("SELECT 2", cmd.CommandText);
    }

    [Fact]
    public void CommandType_delegates_to_inner()
    {
        using var cmd = CreateCommand("sp_Test", CommandType.StoredProcedure);
        Assert.Equal(CommandType.StoredProcedure, cmd.CommandType);
    }

    [Fact]
    public void CreateParameter_delegates_to_inner()
    {
        using var cmd = CreateCommand("SELECT 1");
        var param = cmd.CreateParameter();
        Assert.NotNull(param);
    }

    [Fact]
    public void Dispose_disposes_inner()
    {
        var innerCmd = new FakeDbCommand();
        var cmd = new TrackingDbCommand(innerCmd, _trackingConnection, _options);
        cmd.Dispose();
        Assert.True(innerCmd.WasDisposed);
    }

    // ─── LogSqlText option ──────────────────────────────────────

    [Fact]
    public void LogSqlText_false_does_not_include_sql_at_detailed()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        _options.LogSqlText = false;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Secret = 'password'");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Null(request.Content);
    }

    [Fact]
    public void LogSqlText_true_includes_sql_at_detailed()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        _options.LogSqlText = true;
        using var cmd = CreateCommand("SELECT * FROM Users");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Equal("SELECT * FROM Users", request.Content);
    }

    // ─── Invocation count still increments even when test info is null ────

    [Fact]
    public void InvocationCount_increments_even_without_test_info()
    {
        _options.CurrentTestInfoFetcher = null;
        using var cmd = CreateCommand("SELECT 1");
        cmd.ExecuteScalar();
        Assert.Equal(1, _trackingConnection.InvocationCount);
    }

    // ─── Method field (component diagram arrow label) ──────

    [Fact]
    public void LogRequest_Raw_Method_uses_keyword_not_full_sql()
    {
        _options.Verbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("SELECT CustomerId, CustomerName, PreferredMilkType FROM CustomerPreferences WHERE CustomerId = @id");
        cmd.ExecuteReader();

        var log = GetLogsForTest().First();
        Assert.Equal("SELECT", log.Method.Value?.ToString());
    }

    [Fact]
    public void LogRequest_Raw_Method_uses_keyword_for_insert()
    {
        _options.Verbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("INSERT INTO Users (Id, Name) VALUES (@id, @name)");
        cmd.ExecuteNonQuery();

        var log = GetLogsForTest().First();
        Assert.Equal("INSERT", log.Method.Value?.ToString());
    }

    [Fact]
    public void LogRequest_Detailed_Method_includes_table()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = @id");
        cmd.ExecuteReader();

        var log = GetLogsForTest().First();
        Assert.Equal("SELECT FROM Users", log.Method.Value?.ToString());
    }

    [Fact]
    public void LogRequest_Raw_Variant_method_uses_keyword()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        _options.SetupVerbosity = DapperTrackingVerbosity.Raw;
        using var cmd = CreateCommand("SELECT FeedbackId, CustomerName, OrderId FROM Feedback WHERE OrderId = @orderId ORDER BY CreatedAt");
        cmd.ExecuteReader();

        var log = GetLogsForTest().First();
        Assert.Equal("SELECT", log.SetupVariant!.Method.Value?.ToString());
    }

    // ─── Summarised + LogResponseContent ─────────────────────

    [Fact]
    public void Summarised_IncludesResponseContent_WhenLogResponseContentTrue()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        _options.LogResponseContent = true;
        var fakeCmd = new FakeDbCommand { NonQueryResult = 7 };
        using var cmd = new TrackingDbCommand(fakeCmd, _trackingConnection, _options);
        cmd.CommandText = "DELETE FROM Users WHERE Active = 0";
        cmd.ExecuteNonQuery();

        var response = GetLogsForTest()[1];
        Assert.NotNull(response.Content);
        Assert.Equal("7 rows affected", response.Content);
    }

    [Fact]
    public void Summarised_OmitsResponseContent_WhenLogResponseContentFalse()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        _options.LogResponseContent = false;
        var fakeCmd = new FakeDbCommand { NonQueryResult = 7 };
        using var cmd = new TrackingDbCommand(fakeCmd, _trackingConnection, _options);
        cmd.CommandText = "DELETE FROM Users WHERE Active = 0";
        cmd.ExecuteNonQuery();

        var response = GetLogsForTest()[1];
        Assert.Null(response.Content);
    }

    [Fact]
    public void Summarised_IncludesScalarResponseContent_WhenLogResponseContentTrue()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        _options.LogResponseContent = true;
        using var cmd = CreateCommand("SELECT COUNT(*) FROM Users");
        cmd.ExecuteScalar();

        var response = GetLogsForTest()[1];
        Assert.NotNull(response.Content);
        Assert.Equal("42", response.Content);
    }

    [Fact]
    public void Summarised_ResponseVariant_includes_content_when_LogResponseContent_true()
    {
        _options.Verbosity = DapperTrackingVerbosity.Detailed;
        _options.SetupVerbosity = DapperTrackingVerbosity.Summarised;
        _options.LogResponseContent = true;
        var fakeCmd = new FakeDbCommand { NonQueryResult = 3 };
        using var cmd = new TrackingDbCommand(fakeCmd, _trackingConnection, _options);
        cmd.CommandText = "UPDATE Users SET Active = 1";
        cmd.ExecuteNonQuery();

        var response = GetLogsForTest()[1];
        Assert.NotNull(response.SetupVariant);
        Assert.NotNull(response.SetupVariant!.Content);
        Assert.Equal("3 rows affected", response.SetupVariant!.Content);
    }

    [Fact]
    public void Summarised_still_omits_request_content_when_LogResponseContent_true()
    {
        _options.Verbosity = DapperTrackingVerbosity.Summarised;
        _options.LogResponseContent = true;
        _options.LogSqlText = false;
        using var cmd = CreateCommand("SELECT * FROM Users WHERE Id = 1");
        cmd.ExecuteReader();

        var request = GetLogsForTest()[0];
        Assert.Null(request.Content);
    }
}
