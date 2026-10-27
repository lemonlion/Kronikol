using System.Data;
using System.Data.Common;
using Dapper;
using Kronikol.Reports;
using Kronikol.Sql;
using Kronikol.Tests.Dapper.Fakes;
using Kronikol.Tracking;

namespace Kronikol.Tests.Dapper;

/// <summary>
/// Dapper opens every query reader with <see cref="CommandBehavior.SequentialAccess"/>, which SqlClient and Npgsql
/// enforce, and the wrapper read the whole row before Dapper did, so every Dapper query through it failed on those
/// providers. And Dapper's own address put the data source in a <c>new Uri</c> as it was (#193 plan, F4, F6).
/// </summary>
public class TrackingDbCommandFidelityTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _service = "Db-" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
        ProcessDiagnostics.Remove(SqlCaptureFailure.KeyFor($"TrackingDbConnection ({_service})"));
    }

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private DiagnosticEntry? Diagnostic() => ProcessDiagnostics.Entries.SingleOrDefault(e =>
        e.Kind == DiagnosticKind.CaptureDegraded &&
        e.Message.StartsWith($"TrackingDbConnection ({_service}) could not record", StringComparison.Ordinal));

    private DapperTrackingOptions Options(DapperTrackingVerbosity verbosity = DapperTrackingVerbosity.Detailed) => new()
    {
        CurrentTestInfoFetcher = () => ("Dapper fidelity", _testId),
        Verbosity = verbosity,
        ServiceName = _service,
    };

    public sealed class Row
    {
        public int A { get; set; }
        public string B { get; set; } = "";
    }

    /// <summary>A connection whose commands answer with a fresh reader from <paramref name="reader"/>.</summary>
    private sealed class ReaderConnection(Func<DbDataReader> reader, string dataSource = "localhost") : FakeDbConnection
    {
        public override string DataSource => dataSource;

        protected override DbCommand CreateDbCommand() => new FakeDbCommand { Connection = this, ReaderResult = reader() };
    }

    /// <summary>
    /// A connection that names its data source <paramref name="answers"/> times and then fails every time it is asked,
    /// so the wrapper's own logging fails at the request (0) or the response (1).
    /// </summary>
    private sealed class FailingDataSourceConnection(int answers = 0, Exception? statementFails = null) : FakeDbConnection
    {
        private int _asked;

        public override string DataSource =>
            _asked++ < answers ? "localhost" : throw new InvalidOperationException("The data source is gone.");

        protected override DbCommand CreateDbCommand() =>
            new FakeDbCommand { Connection = this, ReaderResult = TwoRows(), ThrowOnExecute = statementFails };
    }

    private static StrictSequentialReader TwoRows() => new(["A", "B"], [[1, "x"], [2, "y"]]);

    [Fact]
    public void Dapper_Query_reads_through_a_sequential_reader()
    {
        using var connection = new TrackingDbConnection(new ReaderConnection(TwoRows), Options());

        var rows = connection.Query<Row>("SELECT A, B FROM Rows").ToList();

        Assert.Equal([(1, "x"), (2, "y")], rows.Select(r => (r.A, r.B)));
        Assert.Equal("""[{"A":1,"B":"x"},{"A":2,"B":"y"}]""", Logs().Single(l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public async Task Dapper_QueryAsync_reads_through_a_sequential_reader()
    {
        await using var connection = new TrackingDbConnection(new ReaderConnection(TwoRows), Options());

        var rows = (await connection.QueryAsync<Row>("SELECT A, B FROM Rows")).ToList();

        Assert.Equal([(1, "x"), (2, "y")], rows.Select(r => (r.A, r.B)));
        Assert.Equal("""[{"A":1,"B":"x"},{"A":2,"B":"y"}]""", Logs().Single(l => l.Type == RequestResponseType.Response).Content);
    }

    [Theory]
    [InlineData(@"(localdb)\MSSQLLocalDB", DapperTrackingVerbosity.Raw, "localdb-mssqllocaldb")]
    [InlineData(@"(localdb)\MSSQLLocalDB", DapperTrackingVerbosity.Detailed, null)]
    [InlineData("localhost,1433", DapperTrackingVerbosity.Raw, "localhost:1433")]
    [InlineData("localhost,1433", DapperTrackingVerbosity.Detailed, null)]
    [InlineData("host1:5432,host2:5432", DapperTrackingVerbosity.Raw, "host1:5432")]
    [InlineData("tcp:myserver,1433", DapperTrackingVerbosity.Summarised, null)]
    public void A_data_source_never_fails_the_command(string dataSource, DapperTrackingVerbosity verbosity, string? rawAuthority)
    {
        using var connection = new TrackingDbConnection(new ReaderConnection(TwoRows, dataSource), Options(verbosity));

        var thrown = Record.Exception(() => connection.Execute("UPDATE Orders SET Name = 'x'"));

        Assert.Null(thrown);
        var request = Logs().First(l => l.Type == RequestResponseType.Request);
        if (rawAuthority is not null)
            Assert.Equal(rawAuthority, request.Uri.Authority);
    }

    [Fact]
    public void A_detailed_address_that_parsed_before_is_unchanged()
    {
        using var connection = new TrackingDbConnection(new ReaderConnection(TwoRows), Options());

        connection.Execute("UPDATE Orders SET Name = 'x'");

        Assert.Equal("sql:///localhost/TestDb/Orders", Logs().First(l => l.Type == RequestResponseType.Request).Uri.ToString());
    }

    [Fact]
    public async Task A_command_the_wrapper_cannot_record_still_runs_and_is_said_so()
    {
        await using var connection = new TrackingDbConnection(new FailingDataSourceConnection(), Options());

        Assert.Equal(1, connection.Execute("UPDATE Orders SET Name = 'x'"));
        Assert.Equal(1, await connection.ExecuteAsync("UPDATE Orders SET Name = 'x'"));
        Assert.Equal(2, connection.Query<Row>("SELECT A, B FROM Rows").Count());
        Assert.Equal(2, (await connection.QueryAsync<Row>("SELECT A, B FROM Rows")).Count());
        Assert.Equal(42, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Rows"));
        Assert.Equal(42, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Rows"));

        Assert.Empty(Logs());
        Assert.NotNull(Diagnostic());
    }

    [Theory]
    [InlineData("execute")]
    [InlineData("query")]
    [InlineData("scalar")]
    public void A_response_the_wrapper_cannot_record_leaves_the_command_alone(string kind)
    {
        using var connection = new TrackingDbConnection(new FailingDataSourceConnection(answers: 1), Options());

        var thrown = Record.Exception(() =>
        {
            _ = kind switch
            {
                "execute" => connection.Execute("UPDATE Orders SET Name = 'x'"),
                "query" => connection.Query<Row>("SELECT A, B FROM Rows").Count(),
                _ => connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Rows"),
            };
        });

        Assert.Null(thrown);
        Assert.Single(Logs(), l => l.Type == RequestResponseType.Request);
        Assert.DoesNotContain(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.NotNull(Diagnostic());
    }

    [Fact]
    public void A_negative_value_length_never_fails_a_scalar()
    {
        var options = Options();
        options.MaxValueDisplayLength = -1;
        using var connection = new TrackingDbConnection(new ReaderConnection(TwoRows), options);

        Assert.Equal(42, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Rows"));
        Assert.Equal("... (2 chars)", Logs().Single(l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public void A_failed_statement_the_wrapper_cannot_record_throws_the_statements_own_error()
    {
        var rejected = new InvalidOperationException("syntax error at or near \"UPDAT\"");
        using var connection = new TrackingDbConnection(new FailingDataSourceConnection(answers: 1, rejected), Options());

        var thrown = Record.Exception(() => connection.Execute("UPDAT Orders SET Name = 'x'"));

        Assert.Same(rejected, thrown);
        Assert.NotNull(Diagnostic());
    }
}
