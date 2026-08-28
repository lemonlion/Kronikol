using System.Data;
using System.Data.Common;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Tracking;

namespace Kronikol.Tests.EfCore.Relational;

public class SqlTrackingInterceptorTests : IDisposable
{
    // ─── Test infrastructure ────────────────────────────────────

    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private SqlTrackingInterceptorOptions MakeOptions(
        SqlTrackingVerbosity verbosity = SqlTrackingVerbosity.Detailed,
        string serviceName = "Database",
        string callerName = "TestCaller") => new()
    {
        ServiceName = serviceName,
        CallerName = callerName,
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My Test", _testId),
    };

    private static StubDbCommand MakeSelectCommand(string database = "MyDb", string dataSource = "localhost")
    {
        return new StubDbCommand("SELECT [u].[Id] FROM [dbo].[Users] AS [u]", database, dataSource);
    }

    private static StubDbCommand MakeInsertCommand(string database = "MyDb", string dataSource = "localhost")
    {
        return new StubDbCommand("INSERT INTO [Orders] ([Id], [Total]) VALUES (1, 42.50)", database, dataSource);
    }

    private static StubDbCommand MakeStoredProcCommand(string database = "MyDb", string dataSource = "localhost")
    {
        return new StubDbCommand("usp_GetOrders", database, dataSource, CommandType.StoredProcedure);
    }

    private static StubDbCommand MakeOtherCommand(string database = "MyDb", string dataSource = "localhost")
    {
        return new StubDbCommand("SET NOCOUNT ON", database, dataSource);
    }

    public void Dispose()
    {
        TestPhaseContext.Reset();
    }

    // ─── Basic logging ─────────────────────────────────────────

    [Fact]
    public void LogCommandExecuting_And_LogCommandExecuted_Logs_request_and_response()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 5);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void Logs_correct_service_and_caller_names()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(callerName: "MyApi", serviceName: "OrdersDB"));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command);

        var logs = GetLogsFromThisTest();
        Assert.Equal("OrdersDB", logs[0].ServiceName);
        Assert.Equal("MyApi", logs[0].CallerName);
    }

    [Fact]
    public void Does_not_log_when_no_test_info_fetcher()
    {
        var options = MakeOptions();
        options.CurrentTestInfoFetcher = null;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void NonQuery_Logs_request_and_response()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeInsertCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 1);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    [Fact]
    public void Scalar_Logs_request_and_response()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
    }

    // ─── Detailed verbosity ────────────────────────────────────

    [Fact]
    public void Detailed_Select_UsesClassifiedLabel()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Select", log.Method.Value?.ToString());
    }

    [Fact]
    public void Detailed_IncludesCommandTextAsContent()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("SELECT", log.Content);
    }

    [Fact]
    public void Detailed_BuildsCleanUriWithTableName()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeSelectCommand("OrdersDb", "sql-server.local");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("sql://sql-server.local/OrdersDb/Users", log.Uri.ToString());
    }

    [Fact]
    public void Detailed_StoredProc_ShowsProcNameInUri()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeStoredProcCommand("OrdersDb", "sql-server.local");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("sql://sql-server.local/OrdersDb/usp_GetOrders", log.Uri.ToString());
    }

    [Fact]
    public void Detailed_Insert_CorrectLabel()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeInsertCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Insert", log.Method.Value?.ToString());
    }

    // ─── Summarised verbosity ──────────────────────────────────

    [Fact]
    public void Summarised_UsesOperationNameOnly()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Summarised));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Select", log.Method.Value?.ToString());
    }

    [Fact]
    public void Summarised_OmitsContent()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Summarised));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Summarised_UriOmitsServer()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Summarised));
        var command = MakeSelectCommand("OrdersDb", "sql-server.local");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("sql://ordersdb/Users", log.Uri.ToString());
    }

    [Fact]
    public void Summarised_SkipsOtherOperations()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Summarised));
        var command = MakeOtherCommand();

        interceptor.LogCommandExecuting(command);

        var logs = GetLogsFromThisTest();
        Assert.Empty(logs);
    }

    [Fact]
    public void Summarised_DoesNotSkipStoredProc()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Summarised));
        var command = MakeStoredProcCommand();

        interceptor.LogCommandExecuting(command);

        var logs = GetLogsFromThisTest();
        Assert.NotEmpty(logs);
    }

    // ─── Raw verbosity ─────────────────────────────────────────

    [Fact]
    public void Raw_UsesRawSqlKeyword()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Raw));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("SELECT", log.Method.Value?.ToString());
    }

    [Fact]
    public void Raw_IncludesFullContent()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Raw));
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("[dbo].[Users]", log.Content);
    }

    [Fact]
    public void Raw_DoesNotSkipOtherOperations()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Raw));
        var command = MakeOtherCommand();

        interceptor.LogCommandExecuting(command);

        var logs = GetLogsFromThisTest();
        Assert.NotEmpty(logs);
    }

    [Fact]
    public void Raw_UsesFullUri()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Raw));
        var command = MakeSelectCommand("OrdersDb", "sql-server.local");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("sql://sql-server.local/OrdersDb", log.Uri.ToString());
    }

    // ─── Response ──────────────────────────────────────────────

    [Fact]
    public void Response_CapturesRowsAffected()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeInsertCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 3);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("3", log.Content);
    }

    [Fact]
    public void Response_CapturesOkStatus()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal("OK", log.StatusCode?.Value?.ToString());
    }

    [Fact]
    public void Request_CapturesDatabaseFromConnection()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand("TestDatabase", "test-server");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("TestDatabase", log.Uri.ToString());
    }

    // ─── Request/Response pairing ──────────────────────────────

    [Fact]
    public void Request_and_response_share_same_traceId()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 5);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
    }

    [Fact]
    public void Request_and_response_share_same_requestResponseId()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 5);

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void Concurrent_commands_get_distinct_paired_ids()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command1 = MakeSelectCommand();
        var command2 = MakeInsertCommand();

        interceptor.LogCommandExecuting(command1);
        interceptor.LogCommandExecuting(command2);
        interceptor.LogCommandExecuted(command2, rowsAffected: 1);
        interceptor.LogCommandExecuted(command1, rowsAffected: 10);

        var logs = GetLogsFromThisTest();
        var req1 = logs.First(l => l.Type == RequestResponseType.Request && l.Content?.Contains("SELECT") == true);
        var res1 = logs.First(l => l.Type == RequestResponseType.Response && l.Content?.Contains("10") == true);
        var req2 = logs.First(l => l.Type == RequestResponseType.Request && l.Content?.Contains("INSERT") == true);
        var res2 = logs.First(l => l.Type == RequestResponseType.Response && l.Content?.Contains("1 rows") == true);

        // Each pair shares IDs
        Assert.Equal(req1.TraceId, res1.TraceId);
        Assert.Equal(req1.RequestResponseId, res1.RequestResponseId);
        Assert.Equal(req2.TraceId, res2.TraceId);
        Assert.Equal(req2.RequestResponseId, res2.RequestResponseId);

        // Different pairs have different IDs
        Assert.NotEqual(req1.TraceId, req2.TraceId);
        Assert.NotEqual(req1.RequestResponseId, req2.RequestResponseId);
    }

    [Fact]
    public void Request_CapturesDataSourceFromConnection()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeSelectCommand("TestDatabase", "test-server");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("test-server", log.Uri.ToString());
    }

    // ─── ITrackingComponent ────────────────────────────────────

    [Fact]
    public void Implements_ITrackingComponent()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        Assert.IsAssignableFrom<ITrackingComponent>(interceptor);
    }

    [Fact]
    public void WasInvoked_IsFalse_BeforeAnyCommands()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        Assert.False(interceptor.WasInvoked);
    }

    [Fact]
    public void WasInvoked_IsTrue_AfterCommand()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        interceptor.LogCommandExecuting(MakeSelectCommand());
        Assert.True(interceptor.WasInvoked);
    }

    [Fact]
    public void InvocationCount_StartsAtZero()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        Assert.Equal(0, interceptor.InvocationCount);
    }

    [Fact]
    public void InvocationCount_IncreasesWithEachCommand()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        interceptor.LogCommandExecuting(MakeSelectCommand());
        interceptor.LogCommandExecuting(MakeInsertCommand());
        Assert.Equal(2, interceptor.InvocationCount);
    }

    [Fact]
    public void ComponentName_MatchesServiceName()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(serviceName: "IdentityDB"));
        Assert.Equal("SqlTrackingInterceptor (IdentityDB)", interceptor.ComponentName);
    }

    // Constructor_AutoRegistersWithTrackingComponentRegistry lives in
    // SqlTrackingInterceptorHttpContextTests: TrackingComponentRegistry is a
    // global static and every test touching it must share the
    // "TrackingComponentRegistry" xunit collection — from this (parallel)
    // class, another class's Clear() raced the registration away.

    // ─── DataSource with comma-separated port (SQL Server Docker) ──

    [Theory]
    [InlineData(SqlTrackingVerbosity.Detailed)]
    [InlineData(SqlTrackingVerbosity.Raw)]
    public void DataSource_with_comma_port_does_not_throw(SqlTrackingVerbosity verbosity)
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(verbosity));
        var command = MakeSelectCommand("MyDb", "127.0.0.1,33262");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Contains("127.0.0.1", log.Uri.Host);
    }

    [Fact]
    public void Detailed_DataSource_with_comma_port_normalizes_to_colon()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Detailed));
        var command = MakeSelectCommand("MyDb", "127.0.0.1,33262");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("sql://127.0.0.1:33262/MyDb/Users", log.Uri.ToString());
    }

    [Fact]
    public void Raw_DataSource_with_comma_port_normalizes_to_colon()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions(SqlTrackingVerbosity.Raw));
        var command = MakeSelectCommand("MyDb", "127.0.0.1,33262");

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("sql://127.0.0.1:33262/MyDb", log.Uri.ToString());
    }

    // ─── Phase-aware verbosity variants ─────────────────────────

    [Fact]
    public void Unknown_phase_with_both_overrides_populates_both_variants()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Raw;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(log.SetupVariant);
        Assert.NotNull(log.ActionVariant);
    }

    [Fact]
    public void Unknown_phase_with_no_overrides_has_no_variants()
    {
        TestPhaseContext.Reset();
        var interceptor = new SqlTrackingInterceptor(MakeOptions());
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.SetupVariant);
        Assert.Null(log.ActionVariant);
    }

    [Fact]
    public void Known_Setup_phase_with_overrides_has_no_variants()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Raw;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.SetupVariant);
        Assert.Null(log.ActionVariant);
    }

    [Fact]
    public void Known_Action_phase_with_overrides_has_no_variants()
    {
        TestPhaseContext.Current = TestPhase.Action;
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Raw;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.SetupVariant);
        Assert.Null(log.ActionVariant);
    }

    [Fact]
    public void Known_Setup_phase_uses_SetupVerbosity_directly()
    {
        TestPhaseContext.Current = TestPhase.Setup;
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        // Summarised = no content
        Assert.Null(log.Content);
    }

    [Fact]
    public void Known_Action_phase_uses_ActionVerbosity_directly()
    {
        TestPhaseContext.Current = TestPhase.Action;
        var options = MakeOptions();
        options.ActionVerbosity = SqlTrackingVerbosity.Summarised;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }

    [Fact]
    public void SetupVariant_with_Summarised_has_skip_for_Other_operations()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Detailed;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeOtherCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.NotNull(log.SetupVariant);
        Assert.True(log.SetupVariant!.Skip);
        Assert.NotNull(log.ActionVariant);
        Assert.False(log.ActionVariant!.Skip);
    }

    [Fact]
    public void SetupVariant_Summarised_has_null_content()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Detailed;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.SetupVariant!.Content);
        Assert.NotNull(log.ActionVariant!.Content);
    }

    [Fact]
    public void ActionVariant_Raw_uses_raw_keyword_as_method()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Detailed;
        options.ActionVerbosity = SqlTrackingVerbosity.Raw;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Equal("Select", log.SetupVariant!.Method.Value?.ToString());
        Assert.Equal("SELECT", log.ActionVariant!.Method.Value?.ToString());
    }

    [Fact]
    public void Response_also_gets_variants_when_phase_unknown_with_overrides()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Detailed;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 5);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.NotNull(log.SetupVariant);
        Assert.NotNull(log.ActionVariant);
    }

    // ─── Summarised + LogResponseContent ────────────────────────

    [Fact]
    public void Summarised_IncludesResponseContent_WhenLogResponseContentTrue()
    {
        var options = MakeOptions(SqlTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeInsertCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 3);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("3", log.Content);
    }

    [Fact]
    public void Summarised_OmitsResponseContent_WhenLogResponseContentFalse()
    {
        var options = MakeOptions(SqlTrackingVerbosity.Summarised);
        options.LogResponseContent = false;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeInsertCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 3);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Null(log.Content);
    }

    [Fact]
    public void Summarised_IncludesScalarResponseContent_WhenLogResponseContentTrue()
    {
        var options = MakeOptions(SqlTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecutedWithContent(command, "42");

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Equal("42", log.Content);
    }

    [Fact]
    public void Summarised_ResponseVariant_includes_content_when_LogResponseContent_true()
    {
        TestPhaseContext.Reset();
        var options = MakeOptions();
        options.LogResponseContent = true;
        options.SetupVerbosity = SqlTrackingVerbosity.Summarised;
        options.ActionVerbosity = SqlTrackingVerbosity.Detailed;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeInsertCommand();

        interceptor.LogCommandExecuting(command);
        interceptor.LogCommandExecuted(command, rowsAffected: 5);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Response);
        Assert.Contains("5", log.SetupVariant!.Content);
    }

    [Fact]
    public void Summarised_still_omits_request_content_when_LogResponseContent_true()
    {
        var options = MakeOptions(SqlTrackingVerbosity.Summarised);
        options.LogResponseContent = true;
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeSelectCommand();

        interceptor.LogCommandExecuting(command);

        var log = GetLogsFromThisTest().First(l => l.Type == RequestResponseType.Request);
        Assert.Null(log.Content);
    }
}
