using System.Net;
using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #184 R4 (plans/QUERY_FOR_PROGRAMS_PLAN.md 5.4): what <c>summary</c> and <c>grep --count</c> left out, said. #183:
/// <c>summary</c> gave one diagnostic per kind, its first message, so a host checking its capture saw one of its two
/// degraded taps. #182: <c>grep --count</c> searched the default targets, which leave out headers and notes, and printed a
/// bare 0, so an absence check passed over a credential in a header.
/// </summary>
public class QueryCompletenessTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-completeness").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Write(IReadOnlyList<DiagnosticEntry>? diagnostics = null)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario { Id = "t-1", DisplayName = "Lists orders", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario { Id = "t-2", DisplayName = "Cancels an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }
                ]
            }
        ];
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            new("Lists orders", "t-1", HttpMethod.Get, null, new Uri("http://orders-api/orders"), [("Authorization", "Bearer s3cret-token")],
                "orders-api", "orders-ui", RequestResponseType.Request, trace, pair, false) { Timestamp = Start },
            new("Lists orders", "t-1", HttpMethod.Get, "{}", new Uri("http://orders-api/orders"), [], "orders-api", "orders-ui",
                RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = Start.AddMilliseconds(20) }
        ];
        var written = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Complete_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs, diagnostics: diagnostics);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static (string Output, string Error, int Exit) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private static readonly DiagnosticEntry[] TwoDegradedTaps =
    [
        new(DiagnosticKind.UnattributedInteractions, "orders-api: 3 calls had no test", "t-1"),
        new(DiagnosticKind.UnattributedInteractions, "payments-api: 5 calls had no test", "t-2"),
        new(DiagnosticKind.MalformedLine, "line 4 of calls.ndjson is not JSON")
    ];

    [Fact]
    public void Summary_json_lists_every_diagnostic_of_a_kind_with_its_scenario()
    {
        var report = Write(TwoDegradedTaps);

        var (output, error, exit) = Run("summary", report, "--json");

        Assert.True(exit == 0, error);
        using var summary = JsonDocument.Parse(output);
        var groups = summary.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        var unattributed = Assert.Single(groups, g => g.GetProperty("kind").GetString() == "UnattributedInteractions");
        Assert.Equal(2, unattributed.GetProperty("count").GetInt32());
        Assert.Equal("orders-api: 3 calls had no test", unattributed.GetProperty("message").GetString());
        var entries = unattributed.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["orders-api: 3 calls had no test", "payments-api: 5 calls had no test"], entries.Select(e => e.GetProperty("message").GetString()));
        Assert.Equal(["t-1", "t-2"], entries.Select(e => e.GetProperty("id").GetString()));
        Assert.Equal(["s0", "s1"], entries.Select(e => e.GetProperty("scenario").GetString()));

        var malformed = Assert.Single(groups, g => g.GetProperty("kind").GetString() == "MalformedLine");
        var only = Assert.Single(malformed.GetProperty("entries").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, only.GetProperty("scenario").ValueKind);
    }

    [Fact]
    public void Summary_text_keeps_one_line_a_kind_and_says_where_the_rest_are()
    {
        var report = Write(TwoDegradedTaps);

        var (output, error, exit) = Run("summary", report);

        Assert.True(exit == 0, error);
        var line = Assert.Single(output.Split('\n'), l => l.Contains("UnattributedInteractions", StringComparison.Ordinal));
        Assert.Contains("×2", line, StringComparison.Ordinal);
        Assert.Contains("summary --json lists all 2", line, StringComparison.Ordinal);
        var single = Assert.Single(output.Split('\n'), l => l.Contains("MalformedLine", StringComparison.Ordinal));
        Assert.DoesNotContain("lists all", single, StringComparison.Ordinal);
    }

    [Fact]
    public void Grep_count_over_the_default_targets_says_on_stderr_what_it_did_not_search()
    {
        // The issue's case: the credential is in a header, the default leaves headers out, and the count read 0.
        var report = Write();

        var (output, error, exit) = Run("grep", report, "s3cret-token", "--count");

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Contains("not searched: headers, notes", error, StringComparison.Ordinal);
        Assert.Contains("--in", error, StringComparison.Ordinal);

        var (widened, widenedError, widenedExit) = Run("grep", report, "s3cret-token", "--count", "--in", "headers");
        Assert.Equal(0, widenedExit);
        Assert.Equal("1", widened.Trim());
        Assert.DoesNotContain("not searched", widenedError, StringComparison.Ordinal);
    }
}
