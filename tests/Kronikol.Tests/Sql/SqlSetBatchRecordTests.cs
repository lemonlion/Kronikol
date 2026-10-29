using Kronikol.Sql;
using Kronikol.Tracking;
using Kronikol.Tests.History;

namespace Kronikol.Tests.Sql;

/// <summary>
/// A SQL Server batch opening with two SET statements, as EF Core sends every SaveChanges: until #192 R1 the classifier
/// stripped only the first, so the diagnostic trackers (SqlClient, Npgsql, MySqlConnector, Oracle, SQLite, ClickHouse)
/// recorded it as <c>Other</c> with no table, and at Summarised not at all. The call now names its statement, and keeps
/// what 5.16.1 recorded for history's rules 6 and 7 (<see cref="RequestResponseLog.RecordedBefore8"/>).
/// </summary>
[Collection("DiagramsFetcher")]
public class SqlSetBatchRecordTests
{
    private const string Batch = "SET IMPLICIT_TRANSACTIONS OFF;\r\nSET NOCOUNT ON;\r\nINSERT INTO [Orders] ([Status]) VALUES (@p0);";
    private readonly string _testId = Guid.NewGuid().ToString();

    private sealed class Tracker(SqlTrackingOptionsBase options) : SqlDiagnosticTracker(options)
    {
        public override string ComponentName => "SET batch tracker";
        public void Start(string commandText, Guid executionId) => LogCommandStart(commandText, "localhost", "app", executionId);
        public void End(Guid executionId) => LogCommandEnd(executionId, 1);
        public (Guid TraceId, Guid RequestResponseId)? Request(string commandText) => LogRequest(commandText, "localhost", "app");
    }

    private Tracker Make(SqlTrackingVerbosityLevel verbosity, params UnifiedSqlOperation[] excluded)
    {
        var options = new SqlTrackingOptionsBase { ServiceName = "Database", Verbosity = verbosity, CurrentTestInfoFetcher = () => ("SET batches", _testId) };
        foreach (var operation in excluded)
            options.ExcludedOperations.Add(operation);
        return new Tracker(options);
    }

    private RequestResponseLog OnlyRequest() =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == RequestResponseType.Request);

    [Fact]
    public void Every_change_to_a_recorded_call_line_moves_the_version()
    {
        // The history guard (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8): what this tracker records for one fixed call, as the
        // call line history fingerprints, pinned under the rule it was pinned with (HistoryGuard). The SET batch, which rules 6 and 7 read as their tracker recorded it before 8.
        var tracker = Make(SqlTrackingVerbosityLevel.Detailed);
        var id = Guid.NewGuid();
        tracker.Start(Batch, id);
        tracker.End(id);

        HistoryGuard.Pin(RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId), 9,
            "Caller>Database INSERT INTO Orders /app/Orders INSERT INTO [Orders] ([Status]) VALUES (@p0); OK");
    }

    [Fact]
    public void A_batch_names_its_statement_at_detailed_and_keeps_its_old_record()
    {
        var tracker = Make(SqlTrackingVerbosityLevel.Detailed);
        var id = Guid.NewGuid();
        tracker.Start(Batch, id);
        tracker.End(id);

        var request = OnlyRequest();
        Assert.Equal("INSERT INTO Orders", request.Method.Value);
        Assert.EndsWith("/app/Orders", request.Uri.ToString(), StringComparison.Ordinal);
        var before = Assert.IsType<RecordedBefore>(request.RecordedBefore8);
        Assert.False(before.Absent);
        Assert.EndsWith("/app", before.Uri.ToString(), StringComparison.Ordinal);
        Assert.NotEqual("INSERT INTO Orders", before.Method.Value);
    }

    [Fact]
    public void A_batch_is_recorded_at_summarised_and_was_not_before()
    {
        Make(SqlTrackingVerbosityLevel.Summarised).Request(Batch);

        Assert.Same(RecordedBefore.NotRecorded, OnlyRequest().RecordedBefore8);
    }

    [Fact]
    public void A_batch_whose_old_reading_was_excluded_was_not_recorded_before()
    {
        Make(SqlTrackingVerbosityLevel.Detailed, UnifiedSqlOperation.Other).Request(Batch);

        Assert.Same(RecordedBefore.NotRecorded, OnlyRequest().RecordedBefore8);
    }

    [Fact]
    public void A_statement_read_alike_before_carries_no_old_record()
    {
        Make(SqlTrackingVerbosityLevel.Detailed).Request("SET NOCOUNT ON;\r\nSELECT * FROM Orders");

        Assert.Null(OnlyRequest().RecordedBefore8);
    }
}
