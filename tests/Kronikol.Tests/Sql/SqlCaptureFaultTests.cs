using Kronikol.Reports;
using Kronikol.Sql;
using Kronikol.Tracking;

namespace Kronikol.Tests.Sql;

/// <summary>
/// The SQL trackers run inside the application's own calls (a DiagnosticSource event, a wrapped command), so nothing
/// they do may throw into them: a failure leaves the command out of the report and says so in the run's diagnostics, as
/// the MongoDB subscriber has done since 4.19.6. And the address they draw can no longer be what throws (#193 plan, F5, F6).
/// </summary>
// It reads the process-global request log back and records process diagnostics, which every report generated while
// they are held would carry: the DiagramsFetcher collection holds every class that generates one.
[Collection("DiagramsFetcher")]
public class SqlCaptureFaultTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly string _service = "Db-" + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        ProcessDiagnostics.Remove(SqlCaptureFailure.KeyFor($"Tracker ({_service})"));
        GC.SuppressFinalize(this);
    }

    private SqlTrackingOptionsBase Options() => new()
    {
        ServiceName = _service,
        CurrentTestInfoFetcher = () => ("SQL capture faults", _testId),
    };

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private DiagnosticEntry? Diagnostic() => ProcessDiagnostics.Entries.SingleOrDefault(e =>
        e.Kind == DiagnosticKind.CaptureDegraded && e.Message.StartsWith($"Tracker ({_service}) could not record", StringComparison.Ordinal));

    private sealed class Tracker(SqlTrackingOptionsBase options) : SqlDiagnosticTracker(options)
    {
        public override string ComponentName => $"Tracker ({Options.ServiceName})";
        public (Guid TraceId, Guid RequestResponseId)? Request(string? dataSource) => LogRequest("SELECT * FROM Orders", dataSource, "k193");
        public void Response((Guid TraceId, Guid RequestResponseId) ids, string? content) => LogResponse(ids.TraceId, ids.RequestResponseId, content);
        public void Response((Guid TraceId, Guid RequestResponseId) ids, int rows) => LogResponse(ids.TraceId, ids.RequestResponseId, rows);
        public void Start(Guid executionId, string? dataSource) => LogCommandStart("SELECT * FROM Orders", dataSource, "k193", executionId);
        public void End(Guid executionId) => LogCommandEnd(executionId, 1);
    }

    // ─── F6: the address ──────────────────────────────────────

    [Theory]
    [InlineData(@"(localdb)\MSSQLLocalDB", "localdb-mssqllocaldb")]
    [InlineData(@"localhost\SQLEXPRESS", "localhost-sqlexpress")]
    [InlineData("tcp:myserver,1433", "myserver:1433")]
    [InlineData("localhost,1433", "localhost:1433")]
    [InlineData("host1:5432,host2:5432", "host1:5432")]
    public void A_wrapped_commands_data_source_becomes_a_host_and_never_fails_the_command(string dataSource, string authority)
    {
        var tracker = new Tracker(Options());

        var ids = tracker.Request(dataSource);
        var executionId = Guid.NewGuid();
        tracker.Start(executionId, dataSource);

        Assert.NotNull(ids);
        Assert.All(Logs(), log => Assert.Equal(authority, log.Uri.Authority));
        Assert.Equal(2, Logs().Length);
    }

    // ─── F5: nothing thrown into the application's call ───────

    [Fact]
    public void A_request_that_cannot_be_logged_is_left_out_and_said_so()
    {
        var options = Options();
        options.ExcludedOperations = null!;
        var tracker = new Tracker(options);

        var thrown = Record.Exception(() => tracker.Request("localhost"));

        Assert.Null(thrown);
        Assert.Empty(Logs());
        var diagnostic = Diagnostic();
        Assert.NotNull(diagnostic);
        Assert.Contains("NullReferenceException", diagnostic.Message);
        Assert.Contains("The application's command was not affected", diagnostic.Message);
    }

    [Fact]
    public void A_response_that_cannot_be_logged_is_left_out_and_said_so()
    {
        var options = Options();
        var tracker = new Tracker(options);
        var ids = tracker.Request("localhost")!.Value;
        options.UriScheme = "not a scheme";

        Assert.Null(Record.Exception(() => tracker.Response(ids, "[{\"Id\":1}]")));
        Assert.Null(Record.Exception(() => tracker.Response(ids, rows: 1)));

        Assert.Single(Logs());
        Assert.NotNull(Diagnostic());
    }

    [Fact]
    public void A_DiagnosticSource_command_that_cannot_be_logged_is_left_out_and_said_so()
    {
        var options = Options();
        options.ExcludedOperations = null!;
        var tracker = new Tracker(options);
        var executionId = Guid.NewGuid();

        Assert.Null(Record.Exception(() => tracker.Start(executionId, "localhost")));
        Assert.Null(Record.Exception(() => tracker.End(executionId)));

        Assert.Empty(Logs());
        Assert.NotNull(Diagnostic());
    }

    // ─── The cell and the value as text ───────────────────────

    private sealed class Hostile
    {
        public string Boom => throw new InvalidOperationException("getter");
        public override string ToString() => throw new InvalidOperationException("ToString");
    }

    private sealed class Geography(string text)
    {
        public bool IsNull => throw new InvalidOperationException("Data is Null.");
        public override string ToString() => text;
    }

    [Fact]
    public void A_value_whose_text_throws_still_has_a_text()
    {
        Assert.Equal("(Hostile: InvalidOperationException)", SqlRowFormatter.ValueText(new Hostile()));
        Assert.Equal("(Hostile: InvalidOperationException)", SqlRowFormatter.FormatCellValue(new Hostile(), 500));
    }

    [Fact]
    public void A_provider_object_in_a_cell_is_kept_as_its_text()
    {
        Assert.Equal("POINT (1 2)", SqlRowFormatter.FormatCellValue(new Geography("POINT (1 2)"), 500));
    }

    [Fact]
    public void Plain_values_in_a_cell_are_kept_as_they_are()
    {
        object[] plain =
        [
            1, 2L, 2.5, 12.5m, true, 'c', "text", Guid.Empty, new DateTime(2026, 10, 26), new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero),
            new DateOnly(2026, 10, 26), new TimeOnly(12, 30), TimeSpan.FromSeconds(90), DayOfWeek.Monday, new[] { 1, 2 },
            new List<string> { "a" }, new long?[] { 1, null },
        ];
        foreach (var value in plain)
            Assert.Same(value, SqlRowFormatter.FormatCellValue(value, 500));
    }

    [Fact]
    public void A_scalar_is_its_value_text_cut_at_the_length()
    {
        Assert.Equal("null", SqlRowFormatter.ScalarText(null, 500));
        Assert.Equal("null", SqlRowFormatter.ScalarText(DBNull.Value, 500));
        Assert.Equal("2.5", SqlRowFormatter.ScalarText(2.5, 500));
        Assert.Equal("abc... (6 chars)", SqlRowFormatter.ScalarText("abcdef", 3));
        Assert.Equal("(Hostile: InvalidOperationException)", SqlRowFormatter.ScalarText(new Hostile(), 500));
    }

    // MaxValueDisplayLength is a plain int: a negative one cut the text with a negative range, and the
    // ArgumentOutOfRangeException reached the application's ExecuteScalar.
    [Fact]
    public void A_negative_length_cuts_at_nothing_rather_than_throwing()
    {
        Assert.Equal("... (6 chars)", SqlRowFormatter.ScalarText("abcdef", -1));
        Assert.Equal("... (6 chars)", SqlRowFormatter.FormatCellValue("abcdef", -1));
    }
}
