using System.Net;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query repro</c>, and the two lines <c>failures</c> gained with it: where a failure was
/// thrown and the command that re-runs the test. Both were in <c>errorStackTrace</c> for every failing
/// scenario, in the exact shape <c>--filter</c> wants, and no agent-facing surface printed them.
///
/// <para>Also the request/response addressing round trip: a listing folds a pair onto the request's
/// address and <c>values</c> prints the response's, and each has to lead to the body it claims.</para>
/// </summary>
public class ReproTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-repro").FullName;

    private const string Trace =
        "   at Xunit.Assert.Equal[T](T expected, T actual) in /_/src/xunit.assert/Asserts/EqualityAsserts.cs:line 120\n"
        + "   at Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext() in C:\\src\\Acme\\CheckoutTests.cs:line 42\n"
        + "--- End of stack trace from previous location ---\n";

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Repro_prints_the_frame_and_the_filter_for_each_failure()
    {
        var (output, error, exit) = Query("repro", Report());

        Assert.True(exit == 0, error);
        Assert.Contains("s1  Checkout › Pays by card  [Failed]", output);
        Assert.Contains("thrown at Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext() — C:\\src\\Acme\\CheckoutTests.cs:42", output);
        Assert.Contains("dotnet test --filter \"FullyQualifiedName~Acme.Orders.Tests.CheckoutTests.Pays_by_card\"", output);
        Assert.DoesNotContain("Browse", output);
    }

    [Fact]
    public void Repro_json_carries_the_test_name_and_the_command()
    {
        var (output, error, exit) = Query("repro", Report(), "--json");

        Assert.True(exit == 0, error);
        var item = Assert.Single(JsonDocument.Parse(output).RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Acme.Orders.Tests.CheckoutTests.Pays_by_card", item.GetProperty("testName").GetString());
        Assert.StartsWith("dotnet test --filter", item.GetProperty("rerun").GetString());
        Assert.Equal(42, item.GetProperty("thrownAt").GetProperty("line").GetInt32());
    }

    [Fact]
    public void A_passing_scenario_addressed_directly_says_it_has_no_trace()
    {
        var (output, error, exit) = Query("repro", Report(), "s0");

        Assert.True(exit == 0, error);
        Assert.Contains("[Passed]", output);
        Assert.Contains("no stack trace recorded", output);
        Assert.DoesNotContain("dotnet test", output);
    }

    [Fact]
    public void Repro_on_a_green_run_says_nothing_failed()
    {
        var (output, error, exit) = Query("repro", Report(allPassing: true));

        Assert.True(exit == 0, error);
        Assert.Contains("nothing failed", output);
    }

    [Fact]
    public void Failures_prints_where_it_was_thrown_and_how_to_rerun_it()
    {
        var (output, error, exit) = Query("failures", Report());

        Assert.True(exit == 0, error);
        Assert.Contains("thrown at Acme.Orders.Tests.CheckoutTests.<Pays_by_card>d__3.MoveNext() — C:\\src\\Acme\\CheckoutTests.cs:42", output);
        Assert.Contains("rerun: dotnet test --filter \"FullyQualifiedName~Acme.Orders.Tests.CheckoutTests.Pays_by_card\"", output);

        var json = Query("failures", Report(), "--json").Output;
        var item = Assert.Single(JsonDocument.Parse(json).RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Acme.Orders.Tests.CheckoutTests.Pays_by_card", item.GetProperty("testName").GetString());
        Assert.Equal("CheckoutTests.cs", Path.GetFileName(item.GetProperty("thrownAt").GetProperty("file").GetString()));
    }

    // ─── Request/response addressing round trip ─────────────

    [Fact]
    public void Http_names_the_other_half_of_the_pair_from_either_address()
    {
        var report = Report();

        var request = Query("http", report, "s1/i0").Output;
        Assert.Contains("response s1/i1", request);

        var response = Query("http", report, "s1/i1").Output;
        Assert.Contains("answers s1/i0", response);
    }

    [Fact]
    public void The_address_values_prints_fetches_a_body_holding_the_value()
    {
        var report = Report();
        var values = Query("values", report, "--path", "$.status").Output;
        Assert.Contains("\"declined\"", values);

        // The address beside the value is the response's; http on it, with the same path, yields the value.
        Assert.Contains("s1/i1", values);
        var fetched = Query("http", report, "s1/i1", "--path", "$.status").Output;
        Assert.Contains("declined", fetched);
    }

    [Fact]
    public void A_listing_row_carries_the_response_by_hash_and_the_hash_resolves()
    {
        var report = Report();
        var row = Query("interactions", report, "s1").Output;
        var hash = System.Text.RegularExpressions.Regex.Match(row, @"→ .*?(b:[0-9a-f]+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(hash), row);

        var resolved = Query("http", report, hash).Output;
        Assert.Contains("s1/i1", resolved);
    }

    private static (string Output, string Error, int Exit) Query(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(args, output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    private string Report(bool allPassing = false)
    {
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        OneOf<HttpMethod, string> post = HttpMethod.Post;
        OneOf<HttpStatusCode, string>? declined = HttpStatusCode.PaymentRequired;
        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        RequestResponseLog[] logs =
        [
            new("Pays by card", "t1", post, """{"card":"4111"}""", new Uri("https://payments.test/charge"), [], "Payments", "Test",
                RequestResponseType.Request, trace, pair, false) { Timestamp = start },
            new("Pays by card", "t1", post, """{"status":"declined"}""", new Uri("https://payments.test/charge"), [], "Payments", "Test",
                RequestResponseType.Response, trace, pair, false, StatusCode: declined) { Timestamp = start.AddMilliseconds(40) }
        ];

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Checkout",
                Scenarios =
                [
                    new Scenario { Id = "t0", DisplayName = "Browse", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) },
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Pays by card", Duration = TimeSpan.FromSeconds(2),
                        Result = allPassing ? ExecutionResult.Passed : ExecutionResult.Failed,
                        ErrorMessage = allPassing ? null : "Assert.Equal() Failure: declined",
                        ErrorStackTrace = allPassing ? null : Trace,
                        SourceFile = "Features/Checkout.feature", SourceLine = 12
                    }
                ]
            }
        ];

        var written = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Repro_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs);
        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return path;
    }
}
