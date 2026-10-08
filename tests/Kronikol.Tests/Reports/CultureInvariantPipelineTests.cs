using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The whole standard pipeline writes the same files whatever the machine's culture (plans/WARM_UP_PLAN.md F12, R0):
/// the report, the specifications, the data files, the failures digest, CTRF, the mergeable file and the internal-flow
/// charts, from one run with durations that all have a fraction, steps, a failure, a parameterized group, collapsed
/// calls and spans. The facts in <see cref="CultureInvariantOutputTests"/> name each defect; this one finds the ones
/// nobody named, in any writer, now or later.
/// </summary>
[Collection(WholeRunComparisonCollection.Name)]
public class CultureInvariantPipelineTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 10, 5, 7, TimeSpan.Zero);

    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-culture-pipeline").FullName;

    // Unique to this instance, so another test's run sees none of these scenarios' calls as its own.
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];

    public CultureInvariantPipelineTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Every_file_a_run_writes_is_the_same_under_the_culture_as_under_en_us(string culture)
    {
        LogCalls();

        var expected = CultureRun.Under("en-US", () => Run("en-US"));
        var actual = CultureRun.Under(culture, () => Run(culture));

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        // The report and the data file are among them, so the comparison is not over an empty run.
        Assert.Contains("TestRunReport.html", expected.Keys);
        Assert.Contains("TestRunReport.json", expected.Keys);
        Assert.Contains("Failures.md", expected.Keys);
        Assert.Contains("data-duration-ms=\"1260\"", expected["TestRunReport.html"], StringComparison.Ordinal);
        Assert.Contains("iflow-flame-bar", expected["TestRunReport.html"], StringComparison.Ordinal);

        foreach (var (file, text) in expected)
            AssertSame(file, text, actual[file]);
    }

    private static void AssertSame(string file, string expected, string actual)
    {
        if (expected == actual)
            return;

        var at = 0;
        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
            at++;
        string Around(string text) => text.Substring(Math.Max(0, at - 80), Math.Min(text.Length - Math.Max(0, at - 80), 160));
        Assert.Fail($"{file} differs at character {at}:\n  en-US:   {Around(expected)}\n  culture: {Around(actual)}");
    }

    /// <summary>The run's files, keyed by their path under the reports directory, with what the process shares left out.</summary>
    private Dictionary<string, string> Run(string name)
    {
        var reports = Path.Combine(_dir, name);
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = reports,
            SuiteName = "CultureSuite",
            InternalFlowTracking = true,
            InternalFlowSpanGranularity = InternalFlowSpanGranularity.Full,
            PlantUmlRendering = PlantUmlRendering.BrowserJs,
            GenerateComponentDiagram = true,
            GenerateSpecificationsReport = true,
            GenerateSpecificationsMarkdown = true,
            GenerateMergeableData = true,
            GenerateCtrfReport = true,
            CollapseConsecutiveIdenticalCalls = true,
            WriteRunSummaryToConsole = false,
        };

        // Read with no environment and history off: the two runs must differ only by culture, and a ledger found above
        // the test output (or a CI variable another test set) gave one of them a history the other lacked, and with it
        // a labs page.
        DefaultDiagramsFetcher.Reset();
        ReportGenerator.CreateStandardReportsWithDiagramsInEnvironment(Features(), At.UtcDateTime.AddSeconds(-1), At.UtcDateTime.AddMinutes(2), options,
            RunEnvironment.Unrecorded, variable => variable == "KRONIKOL_HISTORY" ? "off" : null, spans: Spans());

        return Directory.EnumerateFiles(reports, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(reports, path).Replace('\\', '/'), path => Normalise(path, File.ReadAllText(path)));
    }

    /// <summary>
    /// Every call with no scenario in the process is listed as a background call, and other tests log some while this
    /// one runs: the background block is dropped from the report and the data files, the one part that is not this
    /// run's own.
    /// </summary>
    private static string Normalise(string path, string text)
    {
        if (path.EndsWith(".html", StringComparison.Ordinal))
            return Regex.Replace(text, "<details class=\"background-calls\">.*?</details>", "", RegexOptions.Singleline);

        if (path.EndsWith(".json", StringComparison.Ordinal) && JsonNode.Parse(text) is JsonObject root)
        {
            root.Remove("background");
            return root.ToJsonString();
        }

        return text;
    }

    // ─── The run ───────────────────────────────────────────────

    private string Id(string scenario) => $"culture-{_id}-{scenario}";

    private Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios =
            [
                new Scenario
                {
                    Id = Id("place"), DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1260),
                    EndedAt = At.AddSeconds(30),
                    Steps =
                    [
                        new ScenarioStep { Keyword = "When", Text = "I place an order", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1201.5) },
                        new ScenarioStep { Keyword = "Then", Text = "it is accepted", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(4.25) }
                    ]
                },
                new Scenario
                {
                    Id = Id("cancel"), DisplayName = "Cancel an order", Result = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(2540.5),
                    EndedAt = At.AddSeconds(30), ErrorMessage = "Expected 2 but found 3",
                    ErrorStackTrace = "   at OrderTests.Cancel() in OrderTests.cs:line 42",
                    Steps =
                    [
                        new ScenarioStep
                        {
                            Keyword = "Then", Text = "the order is cancelled", Status = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(12.5),
                            FailureMessage = "Expected 2 but found 3", SourceFile = "OrderTests.cs", SourceLine = 42
                        }
                    ]
                },
                new Scenario { Id = Id("refund"), DisplayName = "Refund an order", Result = ExecutionResult.Skipped },
                new Scenario { Id = Id("audit"), DisplayName = "Audit an order", Result = ExecutionResult.Bypassed, Duration = TimeSpan.FromMilliseconds(65432.1) },
                .. Row("Widget", 410.5), .. Row("Gadget", 620.25), .. Row("Gizmo", 805.75)
            ]
        }
    ];

    private Scenario[] Row(string item, double ms) =>
    [
        new Scenario
        {
            Id = Id("row-" + item), DisplayName = $"Price an item ({item})", Result = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(ms),
            OutlineId = "Price an item", ExampleValues = new Dictionary<string, string> { ["item"] = item },
            ExampleFlatValues = new Dictionary<string, string> { ["item"] = item }, ExampleDisplayName = $"Price an item ({item})"
        }
    ];

    private const string Trace = "4bf92f3577b34da6a3ce929d0e0e4736";

    private void LogCalls()
    {
        // Three identical calls in a row, which the collapsed diagram draws as one arrow with a range of times.
        for (var i = 0; i < 3; i++)
            Log(Id("place"), HttpMethod.Get, "/orders/1", At.AddSeconds(i), 12.75 + i * 10.5);
        Log(Id("place"), HttpMethod.Post, "/orders", At.AddSeconds(4), 1165.4);
        Log(Id("cancel"), HttpMethod.Delete, "/orders/1", At.AddSeconds(10), 35.25, HttpStatusCode.InternalServerError);
    }

    private static void Log(string testId, HttpMethod method, string path, DateTimeOffset at, double ms, HttpStatusCode status = HttpStatusCode.OK)
    {
        var id = Guid.NewGuid();
        var trace = Guid.NewGuid();
        RequestResponseLogger.Log(new RequestResponseLog("Place an order", testId, method, "{\"total\":4173.5}", new Uri("http://orders" + path), [],
            "orders", "Test", RequestResponseType.Request, trace, id, false)
        { Timestamp = at, ActivityTraceId = Trace, ActivitySpanId = "00f067aa0ba902b7", AttributionSource = AttributionSource.TestContext });
        RequestResponseLogger.Log(new RequestResponseLog("Place an order", testId, method, "{\"total\":4173.5}", new Uri("http://orders" + path), [],
            "orders", "Test", RequestResponseType.Response, trace, id, false, status)
        { Timestamp = at.AddMilliseconds(ms), AttributionSource = AttributionSource.TestContext });
    }

    private static FlowSpan[] Spans() =>
    [
        new(Trace, "00f067aa0ba902b7", null, "POST /orders", "Microsoft.AspNetCore", At.UtcDateTime.AddSeconds(4), TimeSpan.FromMilliseconds(1160.33)),
        new(Trace, "00f067aa0ba902b8", "00f067aa0ba902b7", "INSERT orders", "Npgsql", At.UtcDateTime.AddSeconds(4).AddMilliseconds(101.7), TimeSpan.FromMilliseconds(333.67)),
        new(Trace, "00f067aa0ba902b9", "00f067aa0ba902b7", "publish OrderPlaced", "Azure.Messaging.ServiceBus", At.UtcDateTime.AddSeconds(4).AddMilliseconds(512.25), TimeSpan.FromMilliseconds(7.5))
    ];
}
