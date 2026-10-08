using System.Net;
using System.Text.Json;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Calls logged under a test id that names none of the run's scenarios (plans/XUNIT2_FRAMEWORK_COMPOSITION_PLAN.md
/// section 8, F7): no scenario and no background section shows them, so the run records an
/// <see cref="DiagnosticKind.UnattributedInteractions"/> entry, where it printed only a console line.
/// </summary>
[Collection("DiagramsFetcher")]
public class OrphanedCallsTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-orphans-" + Guid.NewGuid().ToString("N"));

    public OrphanedCallsTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static Feature[] Features(string scenarioId) =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = scenarioId, DisplayName = "Pay by card", Result = ExecutionResult.Passed, EndedAt = At.AddMinutes(1) }]
        }
    ];

    private static RequestResponseLog[] Pair(string testId, string path = "/api/orders")
    {
        var pair = Guid.NewGuid();
        var trace = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Pay by card", testId, HttpMethod.Get, null, new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Request, trace, pair, false) { Timestamp = At },
            new RequestResponseLog("Pay by card", testId, HttpMethod.Get, "[]", new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Response, trace, pair, false, HttpStatusCode.OK) { Timestamp = At.AddMilliseconds(20) }
        ];
    }

    [Fact]
    public void A_run_whose_calls_all_name_a_scenario_or_the_background_has_none()
    {
        var found = OrphanedCalls.Find(Features("scenario-1"), [.. Pair("scenario-1"), .. Pair(TestIdentityScope.UnknownTestId)]);

        Assert.Equal(0, found.Calls);
        Assert.Empty(found.TestIds);
        Assert.Null(OrphanedCalls.Describe(found));
    }

    [Fact]
    public void Calls_under_an_id_no_scenario_has_are_counted_once_per_request_and_response_pair()
    {
        var found = OrphanedCalls.Find(Features("scenario-1"),
            [.. Pair("scenario-1"), .. Pair("orphan-b"), .. Pair("orphan-a", "/one"), .. Pair("orphan-a", "/two")]);

        Assert.Equal(3, found.Calls);
        // The id with the most calls first, then by id, so the description names the likeliest cause.
        Assert.Equal([("orphan-a", 2), ("orphan-b", 1)], found.TestIds);
    }

    [Fact]
    public void A_diagram_marker_under_an_unknown_id_is_not_a_call()
    {
        var marker = new RequestResponseLog("Pay by card", "orphan-a", HttpMethod.Get, null, new Uri("http://orders/step"), [], "orders", "Test",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false) { Timestamp = At, IsOverrideStart = true };

        var found = OrphanedCalls.Find(Features("scenario-1"), [marker]);

        Assert.Equal(0, found.Calls);
    }

    [Fact]
    public void The_description_counts_the_calls_and_the_ids_and_names_the_id_with_the_most()
    {
        var found = OrphanedCalls.Find(Features("scenario-1"), [.. Pair("orphan-b"), .. Pair("orphan-a", "/one"), .. Pair("orphan-a", "/two")]);

        var description = OrphanedCalls.Describe(found);

        Assert.Equal(
            "3 calls carried a test id that no scenario of this run has, so no scenario and no background section shows them: " +
            "2 test ids, the most calls (2) under 'orphan-a'. A test that is not tracked, or a test-info fetcher that answers " +
            "with an id the adapter does not report, logs calls like these.",
            description);
    }

    [Fact]
    public void One_call_under_one_id_reads_in_the_singular()
    {
        var description = OrphanedCalls.Describe(OrphanedCalls.Find(Features("scenario-1"), Pair("orphan-a")));

        Assert.StartsWith("1 call carried a test id that no scenario of this run has", description, StringComparison.Ordinal);
        Assert.Contains(": 1 test id, the most calls (1) under 'orphan-a'.", description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_standard_flow_records_the_orphaned_calls_as_a_diagnostic_in_the_data_file()
    {
        var scenarioId = "orphans-scenario-" + Guid.NewGuid().ToString("N");
        var orphanId = "orphans-unknown-" + Guid.NewGuid().ToString("N");
        var orphanPath = "/api/orders/elsewhere-" + Guid.NewGuid().ToString("N");
        foreach (var log in Pair(scenarioId))
            RequestResponseLogger.Log(log);
        foreach (var log in Pair(orphanId, orphanPath))
            RequestResponseLogger.Log(log);

        ReportGenerator.CreateStandardReportsWithDiagrams(Features(scenarioId), At.AddMinutes(-1).UtcDateTime, DateTime.UtcNow, new ReportConfigurationOptions
        {
            ReportsFolderPath = _dir,
            InternalFlowTracking = false,
            GenerateComponentDiagram = false,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
        });

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "TestRunReport.json")));
        var root = document.RootElement;

        // The orphan's call is in no scenario and not in the background section...
        var scenario = root.GetProperty("features").EnumerateArray().Single().GetProperty("scenarios").EnumerateArray().Single();
        Assert.Equal(2, scenario.GetProperty("httpInteractions").GetArrayLength());
        Assert.DoesNotContain(orphanPath, root.GetProperty("background").GetRawText(), StringComparison.Ordinal);

        // ...so the run says so. The logger is process-wide, so other tests' calls may be counted with this one.
        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("kind").GetString() == "UnattributedInteractions");
        Assert.Contains("carried a test id that no scenario of this run has", diagnostic.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
