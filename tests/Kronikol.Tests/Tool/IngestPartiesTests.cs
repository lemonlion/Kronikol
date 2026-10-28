using System.Text.Json;
using Kronikol.Ingestion;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #190 R2 (plans/PARTY_IDENTITY_PLAN.md §5.9, §5.10): an ingest can say who each party is, as a run's options can.
/// <c>--service-id</c>, <c>--parties</c> and <c>--suite</c> reach the report, each refuses a missing value, and a call a
/// capture marked with <c>testDouble</c> keeps the mark in the report.
/// </summary>
[Collection("DiagramsFetcher")]
public class IngestPartiesTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private const string TestId = "orders-a-pancake";

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-ingest-parties").FullName;

    public IngestPartiesTests() => RequestResponseLogger.Redaction = null;

    public void Dispose()
    {
        RequestResponseLogger.Redaction = null;
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// A capture: the test calls Breakfast Provider, which calls Cow Service. MSW answered Cow Service's call, and the
    /// capture says so on the response only, as the Jest recipe does.
    /// </summary>
    private string Captures(string? failure = null, string? mark = "MSW")
    {
        var captures = Path.Combine(_dir, "captures-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(captures);
        var (aRequest, aResponse) = InteractionRecord.Pair(TestId, null, "POST", "http://localhost:8081/orders", "Breakfast Provider", "Caller",
            statusCode: "201", requestTimestamp: T0.AddMilliseconds(1), responseTimestamp: T0.AddMilliseconds(40));
        var (bRequest, bResponse) = InteractionRecord.Pair(TestId, null, "GET", "http://cows.example/milk", "Cow Service", "Breakfast Provider",
            statusCode: "200", requestTimestamp: T0.AddMilliseconds(5), responseTimestamp: T0.AddMilliseconds(30));
        File.WriteAllLines(Path.Combine(captures, "web.ndjson"),
            [aRequest.ToJson(), bRequest.ToJson(), (bResponse with { TestDouble = mark }).ToJson(), aResponse.ToJson()]);
        File.WriteAllLines(Path.Combine(captures, "tests.ndjson"),
        [
            new TestRunRecord { Event = "start", TestId = TestId, TestName = "orders › a pancake", Feature = "orders.spec.ts", Timestamp = T0 }.ToJson(),
            new TestRunRecord { Event = "end", TestId = TestId, Status = failure is null ? "passed" : "failed", Error = failure, DurationMs = 50, Timestamp = T0.AddMilliseconds(50) }.ToJson(),
        ]);
        return captures;
    }

    private (int Exit, string Out, string Err) Ingest(string captures, params string[] more)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        var exit = IngestCommand.Run([captures, "--tests", Path.Combine(captures, "tests.ndjson"), .. more], output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static JsonElement Report(string reports)
    {
        using var report = JsonDocument.Parse(ReportFile.Read(ReportFile.Resolve(Path.Combine(reports, "TestRunReport.json"))));
        return report.RootElement.Clone();
    }

    [Fact]
    public void Service_id_parties_and_suite_each_reach_the_report()
    {
        var captures = Captures();
        var parties = Path.Combine(_dir, "parties.json");
        File.WriteAllText(parties, """{ "partiesFormatVersion": 1, "parties": [ { "name": "Cow Service", "id": "cow-service" } ] }""");
        var reports = Path.Combine(_dir, "out");

        var (exit, _, error) = Ingest(captures, "-o", reports, "--service-id", "breakfast-provider", "--parties", parties, "--suite", "Breakfast.Tests");

        Assert.True(exit == 0, error);
        var report = Report(reports);
        Assert.Equal("breakfast-provider", report.GetProperty("serviceId").GetString());
        Assert.Equal("Breakfast.Tests", report.GetProperty("suite").GetString());
        var cow = report.GetProperty("parties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Cow Service");
        Assert.Equal(("cow-service", "double", "observed"), (cow.GetProperty("id").GetString(), cow.GetProperty("kind").GetString(), cow.GetProperty("kindSource").GetString()));
    }

    [Fact]
    public void A_mark_on_one_half_of_a_call_reaches_both_halves_in_the_report_and_an_unmarked_call_has_none()
    {
        var reports = Path.Combine(_dir, "out-marks");

        Assert.Equal(0, Ingest(Captures(), "-o", reports).Exit);

        var calls = Report(reports).GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.All(calls.Where(c => c.GetProperty("serviceName").GetString() == "Cow Service"), c => Assert.Equal("MSW", c.GetProperty("testDouble").GetString()));
        Assert.All(calls.Where(c => c.GetProperty("serviceName").GetString() == "Breakfast Provider"), c => Assert.False(c.TryGetProperty("testDouble", out _)));
    }

    // A blank mark names nothing, so it marks nothing: the call reads as one without the key does.
    [Fact]
    public void A_blank_mark_is_no_mark()
    {
        var reports = Path.Combine(_dir, "out-blank");

        Assert.Equal(0, Ingest(Captures(mark: "  "), "-o", reports).Exit);

        var calls = Report(reports).GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("httpInteractions").EnumerateArray().ToArray();
        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.False(c.TryGetProperty("testDouble", out _)));
    }

    // A parties file among the inputs (named, or matched by a glob over the captures folder) is the parties file, not a
    // capture whose lines are all malformed.
    [Fact]
    public void A_parties_file_among_the_inputs_is_not_read_as_a_capture()
    {
        var captures = Captures();
        var parties = Path.Combine(captures, "parties.json");
        File.WriteAllText(parties, """{ "partiesFormatVersion": 1, "parties": [ { "name": "Cow Service", "id": "cow-service" } ] }""");
        var reports = Path.Combine(_dir, "out-among");

        var (exit, output, error) = Ingest(captures, parties, "--parties", parties, "-o", reports);

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("malformed", output);
        Assert.Equal("cow-service", Report(reports).GetProperty("parties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Cow Service").GetProperty("id").GetString());
    }

    // Found as the history ledger is: above the directory the ingest runs in, never above the tool's own folder, which a
    // tool built inside a checkout would otherwise read (plans/PHASE_FROM_STEPS_PLAN.md F6 for the ledger).
    [Fact]
    public void Ingest_finds_the_parties_file_from_where_it_is_run()
    {
        var project = Path.Combine(_dir, "project");
        Directory.CreateDirectory(Path.Combine(project, ".kronikol"));
        File.WriteAllText(Path.Combine(project, ".kronikol", "parties.json"),
            """{ "partiesFormatVersion": 1, "parties": [ { "name": "Cow Service", "id": "cow-service", "kind": "double", "note": "MSW" } ] }""");
        var captures = Captures();
        // The report is written outside the project, so only the directory the ingest runs in leads to the file.
        var reports = Path.Combine(_dir, "elsewhere", "Reports");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { Path.Combine(AppContext.BaseDirectory, "Kronikol.Tool.dll"), "ingest", captures, "--tests", Path.Combine(captures, "tests.ndjson"), "-o", reports })
            start.ArgumentList.Add(arg);
        start.Environment.Remove("KRONIKOL_PARTIES");

        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "kronikol ingest did not finish");
        Assert.True(process.ExitCode == 0, output.Result + error.Result);

        var cow = Report(reports).GetProperty("parties").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Cow Service");
        Assert.Equal(("cow-service", "declared", "MSW"), (cow.GetProperty("id").GetString(), cow.GetProperty("kindSource").GetString(), cow.GetProperty("note").GetString()));
    }

    // The Jest recipe's capture (wiki Ingesting-From-Jest, captureMsw), line for line as its write() emits it: MSW mocked
    // one call to the risk service and let the other through to it, and says so on the response of the mocked one only.
    [Fact]
    public void The_jest_recipes_capture_of_a_mocked_and_a_bypassed_call_gives_the_counts()
    {
        var captures = Path.Combine(_dir, "jest");
        Directory.CreateDirectory(captures);
        const string test = "\"testId\":\"quotes-a-premium\",\"testName\":\"quotes › a premium\"";
        File.WriteAllLines(Path.Combine(captures, "capture.ndjson"),
        [
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c1\",\"method\":\"POST\",\"uri\":\"http://localhost:3000/quote\",\"serviceName\":\"quote-service\",\"callerName\":\"Caller\",\"type\":\"Request\",\"timestamp\":\"2026-10-27T09:00:00.000Z\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c2\",\"method\":\"GET\",\"uri\":\"http://risk.example/score\",\"serviceName\":\"risk.example\",\"callerName\":\"quote-service\",\"type\":\"Request\",\"timestamp\":\"2026-10-27T09:00:00.010Z\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c2\",\"method\":\"GET\",\"uri\":\"http://risk.example/score\",\"serviceName\":\"risk.example\",\"callerName\":\"quote-service\",\"type\":\"Response\",\"statusCode\":\"200\",\"content\":\"{\\\"score\\\":3}\",\"timestamp\":\"2026-10-27T09:00:00.020Z\",\"testDouble\":\"MSW\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c3\",\"method\":\"GET\",\"uri\":\"http://risk.example/limits\",\"serviceName\":\"risk.example\",\"callerName\":\"quote-service\",\"type\":\"Request\",\"timestamp\":\"2026-10-27T09:00:00.030Z\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c3\",\"method\":\"GET\",\"uri\":\"http://risk.example/limits\",\"serviceName\":\"risk.example\",\"callerName\":\"quote-service\",\"type\":\"Response\",\"statusCode\":\"200\",\"timestamp\":\"2026-10-27T09:00:00.040Z\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c4\",\"method\":\"POST\",\"uri\":\"http://psp.example/charge\",\"serviceName\":\"psp.example\",\"callerName\":\"quote-service\",\"type\":\"Request\",\"timestamp\":\"2026-10-27T09:00:00.041Z\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c4\",\"method\":\"POST\",\"uri\":\"http://psp.example/charge\",\"serviceName\":\"psp.example\",\"callerName\":\"quote-service\",\"type\":\"Response\",\"statusCode\":\"201\",\"timestamp\":\"2026-10-27T09:00:00.045Z\",\"testDouble\":\"MSW\"}",
            "{" + test + ",\"traceId\":\"t1\",\"requestResponseId\":\"c1\",\"method\":\"POST\",\"uri\":\"http://localhost:3000/quote\",\"serviceName\":\"quote-service\",\"callerName\":\"Caller\",\"type\":\"Response\",\"statusCode\":\"200\",\"timestamp\":\"2026-10-27T09:00:00.050Z\"}",
        ]);
        var reports = Path.Combine(_dir, "out-jest");

        var (output, error) = (new StringWriter(), new StringWriter());
        Assert.True(IngestCommand.Run([captures, "-o", reports], output, error) == 0, error.ToString());

        var parties = new StringWriter();
        Assert.Equal(0, Kronikol.Query.QueryCommand.Run(["parties", reports, "--json"], parties, new StringWriter()));
        using var json = JsonDocument.Parse(parties.ToString());
        var risk = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("party").GetString() == "risk.example");
        // Mocked once and passed through once: a double answered one of its two calls, so its kind stays unknown.
        Assert.Equal((2, 1, "unknown"), (risk.GetProperty("callsReceived").GetInt32(), risk.GetProperty("doubleCalls").GetInt32(), risk.GetProperty("kind").GetString()));
        // Mocked every time: a double, as the capturer saw, though the report declares nothing.
        var psp = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("party").GetString() == "psp.example");
        Assert.Equal((1, "double", "observed"), (psp.GetProperty("doubleCalls").GetInt32(), psp.GetProperty("kind").GetString(), psp.GetProperty("kindSource").GetString()));
    }

    // The pair a .NET capturer builds its records with: the overload for a call a test double answered marks both halves.
    [Fact]
    public void The_pair_for_a_call_a_double_answered_marks_both_halves()
    {
        var (request, response) = InteractionRecord.Pair(TestId, null, "GET", "http://cows.example/milk", "Cow Service", "Breakfast Provider",
            null, null, "200", null, null, T0, T0.AddMilliseconds(5), null, null, null, null, null, null, "MSW");

        Assert.Equal(("MSW", "MSW"), (request.TestDouble, response.TestDouble));
    }

    [Theory]
    [InlineData("--service-id")]
    [InlineData("--parties")]
    [InlineData("--suite")]
    public void Each_flag_refuses_a_missing_value(string flag)
    {
        var (exit, _, error) = Ingest(Captures(), "-o", Path.Combine(_dir, "out-missing"), flag);

        Assert.Equal(2, exit);
        Assert.Contains("Missing value for " + flag, error);
    }

    [Theory]
    [InlineData("--service-id")]
    [InlineData("--suite")]
    public void A_blank_value_is_refused(string flag)
    {
        var (exit, _, error) = Ingest(Captures(), "-o", Path.Combine(_dir, "out-blank"), flag, " ");

        Assert.Equal(2, exit);
        Assert.Contains(flag + " must not be blank", error);
    }

    [Fact]
    public void A_parties_file_that_is_not_there_is_refused()
    {
        var (exit, _, error) = Ingest(Captures(), "-o", Path.Combine(_dir, "out-nofile"), "--parties", Path.Combine(_dir, "nowhere.json"));

        Assert.Equal(1, exit);
        Assert.Contains("Parties file not found", error);
    }

    // The pointer the ingest prints addresses the report's own ids: the suite scopes every stable id, so a pointer that
    // computed them without it would hand the reader addresses the report does not have.
    [Fact]
    public void With_a_suite_the_pointer_addresses_the_reports_own_ids()
    {
        var reports = Path.Combine(_dir, "out-suite");

        var (exit, output, error) = Ingest(Captures(failure: "Expected 201 but got 500"), "-o", reports, "--suite", "Breakfast.Tests");

        Assert.True(exit == 0, error);
        var stableId = Report(reports).GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("stableId").GetString()!;
        Assert.Contains(stableId, output);

        var unscoped = Path.Combine(_dir, "out-no-suite");
        Assert.Equal(0, Ingest(Captures(failure: "Expected 201 but got 500"), "-o", unscoped).Exit);
        Assert.NotEqual(stableId, Report(unscoped).GetProperty("features")[0].GetProperty("scenarios")[0].GetProperty("stableId").GetString());
    }
}
