using System.Data.Common;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Reports;
using Kronikol.Sql;
using Kronikol.Tracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kronikol.Tests.EfCore.Relational;

/// <summary>
/// The interceptor runs inside EF Core's own calls, so nothing it does may throw into them, and the address it draws can
/// no longer be what throws: until 5.11.1 every command on LocalDB or a named instance failed with a
/// UriFormatException, and every Npgsql arrow named a host called <c>tcp</c> (plans/SQL_RESPONSE_CAPTURE_PLAN.md F5, F6,
/// facts 6 and 7).
/// </summary>
public class SqlTrackingInterceptorFaultTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _service = "Db-" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        ProcessDiagnostics.Remove(SqlCaptureFailure.KeyFor($"SqlTrackingInterceptor ({_service})"));
        GC.SuppressFinalize(this);
    }

    private SqlTrackingInterceptor Interceptor(SqlTrackingVerbosity verbosity = SqlTrackingVerbosity.Detailed, int maxValueDisplayLength = 500) => new(new SqlTrackingInterceptorOptions
    {
        ServiceName = _service,
        CurrentTestInfoFetcher = () => ("Interceptor faults", _testId),
        Verbosity = verbosity,
        MaxValueDisplayLength = maxValueDisplayLength,
    });

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private DiagnosticEntry[] Diagnostics() => ProcessDiagnostics.Entries.Where(e =>
        e.Kind == DiagnosticKind.CaptureDegraded &&
        e.Message.StartsWith($"SqlTrackingInterceptor ({_service}) could not record", StringComparison.Ordinal)).ToArray();

    [Theory]
    [InlineData("localhost,1433", "localhost:1433")]
    [InlineData("127.0.0.1,1433", "127.0.0.1:1433")]
    [InlineData(@"(localdb)\MSSQLLocalDB", "localdb-mssqllocaldb")]
    [InlineData(@".\SQLEXPRESS", "localhost-sqlexpress")]
    [InlineData(@"localhost\SQLEXPRESS", "localhost-sqlexpress")]
    [InlineData(@"myhost\inst,1433", "myhost-inst:1433")]
    [InlineData("tcp:myserver,1433", "myserver:1433")]
    [InlineData(@"np:\\.\pipe\sql\query", "localhost")]
    [InlineData("tcp://localhost:55432", "localhost:55432")]
    [InlineData("host1:5432,host2:5432", "host1:5432")]
    [InlineData("/var/run/postgresql", "localhost")]
    [InlineData(@"C:\data\app.db", "localhost")]
    [InlineData("[::1]:5432", "[::1]:5432")]
    public void Every_data_source_is_drawn_against_a_host(string dataSource, string authority)
    {
        var interceptor = Interceptor();

        interceptor.LogCommandExecuting(new StubDbCommand("SELECT * FROM Orders", dataSource: dataSource));

        var request = Assert.Single(Logs());
        Assert.Equal(authority, request.Uri.Authority);
        Assert.Equal("/TestDb/Orders", request.Uri.AbsolutePath);
    }

    [Fact]
    public void A_database_name_drawn_as_the_host_is_made_a_host()
    {
        var interceptor = Interceptor(SqlTrackingVerbosity.Summarised);

        interceptor.LogCommandExecuting(new StubDbCommand("SELECT * FROM Orders", database: "My Db"));

        Assert.Equal("sql://my-db/Orders", Assert.Single(Logs()).Uri.ToString());
    }

    private sealed class LostConnection() : StubDbConnection("TestDb", "unused")
    {
        public override string DataSource => throw new InvalidOperationException("The data source is gone.");
    }

    private sealed class LostConnectionCommand() : StubDbCommand("SELECT * FROM Orders")
    {
        private readonly LostConnection _connection = new();

        protected override DbConnection? DbConnection { get => _connection; set { } }
    }

    [Fact]
    public void A_connection_that_cannot_name_its_data_source_still_gets_an_arrow()
    {
        var interceptor = Interceptor();

        interceptor.LogCommandExecuting(new LostConnectionCommand());

        Assert.Equal("sql://localhost/TestDb/Orders", Assert.Single(Logs()).Uri.ToString());
    }

    private sealed class HostileCommand() : StubDbCommand("SELECT * FROM Orders")
    {
        public override string CommandText { get => throw new InvalidOperationException("No text here."); set { } }
    }

    [Fact]
    public async Task No_hook_throws_into_EF_Core_and_the_failure_is_said_once()
    {
        var interceptor = Interceptor();
        var command = new HostileCommand();
        var reader = new RowStubDataReader();

        Assert.False(interceptor.ReaderExecuting(command, null!, default).HasResult);
        Assert.Same(reader, interceptor.ReaderExecuted(command, null!, reader));
        Assert.False(interceptor.NonQueryExecuting(command, null!, default).HasResult);
        Assert.Equal(3, interceptor.NonQueryExecuted(command, null!, 3));
        Assert.False(interceptor.ScalarExecuting(command, null!, default).HasResult);
        Assert.Equal(7, interceptor.ScalarExecuted(command, null!, 7));
        Assert.False((await interceptor.ReaderExecutingAsync(command, null!, default)).HasResult);
        Assert.Same(reader, await interceptor.ReaderExecutedAsync(command, null!, reader));
        Assert.False((await interceptor.NonQueryExecutingAsync(command, null!, default)).HasResult);
        Assert.Equal(3, await interceptor.NonQueryExecutedAsync(command, null!, 3));
        Assert.False((await interceptor.ScalarExecutingAsync(command, null!, default)).HasResult);
        Assert.Equal(7, await interceptor.ScalarExecutedAsync(command, null!, 7));
        interceptor.CommandFailed(command, null!);
        await interceptor.CommandFailedAsync(command, null!);
        Assert.False(interceptor.DataReaderClosing(command, null!, default).IsSuppressed);
        Assert.False((await interceptor.DataReaderClosingAsync(command, null!, default)).IsSuppressed);
        Assert.False(interceptor.DataReaderDisposing(command, null!, default).IsSuppressed);

        Assert.Empty(Logs());
        Assert.Single(Diagnostics());
    }

    [Fact]
    public void A_negative_value_length_never_fails_a_scalar()
    {
        var interceptor = Interceptor(maxValueDisplayLength: -1);
        var command = new StubDbCommand("SELECT COUNT(*) FROM Orders");

        interceptor.LogCommandExecuting(command);
        var returned = interceptor.ScalarExecuted(command, null!, "abcdef");

        Assert.Equal("abcdef", returned);
        Assert.Equal("... (6 chars)", Logs().Single(l => l.Type == RequestResponseType.Response).Content);
    }
}
