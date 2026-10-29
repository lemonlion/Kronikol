using Kronikol.History;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Tests.Sql;

/// <summary>
/// An Oracle data source names the server and the service, and ODP.NET no database, so until F26
/// (plans/CLIENT_FLOORS_PLAN.md) every diagnostic tracker drew an Oracle call against <c>localhost</c> and a database
/// called <c>unknown</c>. The call now names both, and keeps the address history's rules 6 to 9 recorded
/// (<see cref="RequestResponseLog.RecordedBefore10"/>), so a run before the change and one after compare as the same.
/// </summary>
[Collection("DiagramsFetcher")]
public class SqlOracleAddressBridgeTests
{
    private const string Select = "SELECT id, name FROM breakfasts";
    private readonly string _testId = Guid.NewGuid().ToString();

    private sealed class Tracker(SqlTrackingOptionsBase options) : SqlDiagnosticTracker(options)
    {
        public override string ComponentName => "Oracle address tracker";

        public void Diagnostic(string dataSource, string database)
        {
            var id = Guid.NewGuid();
            LogCommandStart(Select, dataSource, database, id);
            LogCommandEnd(id, 1);
        }

        public void Wrapped(string dataSource, string database)
        {
            if (LogRequest(Select, dataSource, database) is { } ids)
                LogResponse(ids.TraceId, ids.RequestResponseId, 1);
        }
    }

    private Tracker Make(SqlTrackingVerbosityLevel verbosity = SqlTrackingVerbosityLevel.Detailed) =>
        new(new SqlTrackingOptionsBase { ServiceName = "Oracle", Verbosity = verbosity, CurrentTestInfoFetcher = () => ("Oracle addresses", _testId) });

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private RequestResponseLog Request() => Assert.Single(Logs(), l => l.Type == RequestResponseType.Request);

    private void Run(bool diagnostic, string dataSource, string database, SqlTrackingVerbosityLevel verbosity = SqlTrackingVerbosityLevel.Detailed)
    {
        var tracker = Make(verbosity);
        if (diagnostic)
            tracker.Diagnostic(dataSource, database);
        else
            tracker.Wrapped(dataSource, database);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_Oracle_call_names_its_server_and_service_and_keeps_the_address_rules_6_to_9_recorded(bool diagnostic)
    {
        Run(diagnostic, "127.0.0.1:21521/FREEPDB1", "");

        var request = Request();
        Assert.Equal("sql://127.0.0.1:21521/FREEPDB1/breakfasts", request.Uri.ToString());
        Assert.Equal("sql://localhost/unknown/breakfasts", request.RecordedBefore10!.Uri.ToString());
        Assert.Equal("SELECT FROM breakfasts", request.RecordedBefore10.Method.Value?.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void History_reads_the_call_as_each_rule_recorded_it(bool diagnostic)
    {
        Run(diagnostic, "127.0.0.1:21521/FREEPDB1", "");

        foreach (var version in new[] { 6, 7, 8, 9 })
            Assert.Equal($"Caller>Oracle SELECT FROM breakfasts /unknown/breakfasts {Select} OK",
                Assert.Single(InteractionShape.Calls(Logs(), null, version)).ToString());
        Assert.Equal($"Caller>Oracle SELECT FROM breakfasts /FREEPDB1/breakfasts {Select} OK",
            Assert.Single(InteractionShape.Calls(Logs(), null, 10)).ToString());
    }

    [Fact]
    public void At_Summarised_the_database_moves_and_is_kept_too()
    {
        Run(diagnostic: false, "127.0.0.1:21521/FREEPDB1", "", SqlTrackingVerbosityLevel.Summarised);

        var request = Request();
        Assert.Equal("sql:///FREEPDB1/breakfasts", request.Uri.ToString());
        Assert.Equal("sql:///unknown/breakfasts", request.RecordedBefore10!.Uri.ToString());
    }

    [Theory]
    [InlineData("localhost", "app")]
    [InlineData("localhost,1433", "app")]
    [InlineData("data/app.db", "main")]
    [InlineData("tcp://localhost:5432", "orders")]
    public void A_call_whose_address_did_not_move_carries_no_mark(string dataSource, string database)
    {
        Run(diagnostic: false, dataSource, database);

        Assert.Null(Request().RecordedBefore10);
    }
}
