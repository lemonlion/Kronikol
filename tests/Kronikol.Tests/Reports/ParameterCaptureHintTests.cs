using Kronikol.Constants;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The hint that closes a specific dead end (LLM_FRIENDLY_PLAN M2.2): a failing SQL call whose statement
/// reads <c>WHERE id = @p0</c> and never says what <c>@p0</c> was. The values are not in the report and
/// no amount of querying will find them, so the only useful answer is "turn the capture on and run it
/// again" — which the report should say rather than leave a reader hunting.
/// <para>What makes it a real trap: <c>LogParameters</c> alone changes nothing. The parameter block is
/// appended only at <c>Raw</c> verbosity and the default is <c>Detailed</c>, so a hint naming only
/// <c>LogParameters</c> would send someone to a setting that does not fix it.</para>
/// </summary>
public class ParameterCaptureHintTests
{
    [Theory]
    [InlineData("SELECT * FROM orders WHERE id = @p0")]
    [InlineData("SELECT * FROM orders WHERE id = $1")]
    [InlineData("SELECT * FROM orders WHERE id = :orderId")]
    [InlineData("UPDATE orders SET total = ? WHERE id = ?")]
    public void A_statement_with_placeholders_and_no_values_earns_the_hint(string statement)
    {
        Assert.True(ParameterCaptureHint.Applies(DependencyCategories.PostgreSQL, statement));
    }

    [Fact]
    public void A_statement_whose_parameters_were_captured_does_not()
    {
        // The literal marker all three trackers write (SqlDiagnosticTracker, Dapper, Spanner).
        Assert.False(ParameterCaptureHint.Applies(DependencyCategories.SQL,
            "SELECT * FROM orders WHERE id = @p0\n-- Parameters: @p0=4173"));
    }

    [Fact]
    public void A_statement_with_its_values_inline_does_not()
    {
        Assert.False(ParameterCaptureHint.Applies(DependencyCategories.SqlServer,
            "SELECT * FROM orders WHERE id = 4173"));
    }

    [Theory]
    [InlineData(DependencyCategories.HTTP, "GET /orders?id=4173")]
    [InlineData(DependencyCategories.HTTP, "POST http://payments:8080/charge")]
    [InlineData(DependencyCategories.Elasticsearch, "{ \"query\": { \"term\": { \"id\": 4173 } } }")]
    [InlineData(DependencyCategories.MongoDB, "db.orders.find({ email: \"a@b.com\" })")]
    public void Nothing_outside_the_sql_family_earns_it(string category, string content)
    {
        // `?` in a query string, `:` in a host:port, `@` in an email — every one of these would trip a
        // naive placeholder scan, which is why the hint is scoped to the SQL-ish categories.
        Assert.False(ParameterCaptureHint.Applies(category, content));
    }

    [Fact]
    public void The_hint_names_both_settings_and_is_honest_about_ef_core()
    {
        // LogParameters alone does nothing at the default verbosity, and the EF Core interceptor has no
        // parameter capture at all — yet it stamps the same SQL category, so the text has to hold for
        // a reader who is using it.
        Assert.Contains("LogParameters", ParameterCaptureHint.Message);
        Assert.Contains("Raw", ParameterCaptureHint.Message);
        Assert.Contains("EF Core", ParameterCaptureHint.Message);
    }

    // ─── Where it surfaces ─────────────────────────────────────

    private static Feature[] FailingWithSql(string testId) =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario
                {
                    Id = testId, DisplayName = "Pay", Result = ExecutionResult.Failed,
                    ErrorMessage = "no rows",
                    Steps = [new ScenarioStep { Keyword = "Then", Text = "the order is there", Status = ExecutionResult.Failed }]
                }
            ]
        }
    ];

    private static RequestResponseLog[] SqlCall(string testId, string statement) =>
    [
        new(testId, testId, "SELECT", statement, new Uri("sql://orders/orders"), [], "orders-db", "test",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        {
            DependencyCategory = DependencyCategories.PostgreSQL,
            Timestamp = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)
        }
    ];

    [Fact]
    public void The_digest_says_it_once_however_many_statements_there_are()
    {
        var logs = SqlCall("h1", "SELECT * FROM orders WHERE id = @p0")
            .Concat(SqlCall("h1", "SELECT * FROM lines WHERE order_id = @p0")).ToArray();

        var digest = FailuresDigestGenerator.Generate(FailingWithSql("h1"), logs, "TestRunReport", "3.1.0");

        // Once: a run configured this way has it on every statement, and repeating it buries the failures.
        var occurrences = digest.Markdown.Split("Parameters were not captured").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("LogParameters", digest.Markdown);
    }

    [Fact]
    public void The_digest_stays_quiet_when_the_values_were_captured()
    {
        var logs = SqlCall("h2", "SELECT * FROM orders WHERE id = @p0\n-- Parameters: @p0=4173");

        var digest = FailuresDigestGenerator.Generate(FailingWithSql("h2"), logs, "TestRunReport", "3.1.0");

        Assert.DoesNotContain("Parameters were not captured", digest.Markdown);
    }

    [Fact]
    public void Query_failures_says_it_too_and_a_green_run_reads_no_payloads()
    {
        var directory = Directory.CreateTempSubdirectory("kronikol-paramhint").FullName;
        try
        {
            var report = Path.Combine(directory, "TestRunReport.json");
            File.Move(ReportGenerator.GenerateTestRunReportData(
                FailingWithSql("h3"), DateTime.UtcNow, DateTime.UtcNow,
                $"Hint_{Guid.NewGuid():N}.json", DataFormat.Json,
                trackedLogs: SqlCall("h3", "SELECT * FROM orders WHERE id = @p0")), report, overwrite: true);

            var output = new StringWriter();
            Assert.Equal(0, Kronikol.Tool.QueryCommand.Run(["failures", report], output, new StringWriter()));
            Assert.Contains("LogParameters", output.ToString());

            // A green run must not pay for the check at all - the statement is never read.
            var green = FailingWithSql("h4");
            green[0].Scenarios[0].Result = ExecutionResult.Passed;
            green[0].Scenarios[0].Steps![0].Status = ExecutionResult.Passed;
            green[0].Scenarios[0].ErrorMessage = null;
            var greenReport = Path.Combine(directory, "Green.json");
            File.Move(ReportGenerator.GenerateTestRunReportData(
                green, DateTime.UtcNow, DateTime.UtcNow,
                $"Hint_{Guid.NewGuid():N}.json", DataFormat.Json,
                trackedLogs: SqlCall("h4", "SELECT * FROM orders WHERE id = @p0")), greenReport, overwrite: true);

            var greenOutput = new StringWriter();
            Assert.Equal(0, Kronikol.Tool.QueryCommand.Run(["failures", greenReport], greenOutput, new StringWriter()));
            Assert.Contains("nothing failed", greenOutput.ToString());
            Assert.DoesNotContain("LogParameters", greenOutput.ToString());
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
