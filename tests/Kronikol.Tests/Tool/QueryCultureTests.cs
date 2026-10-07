using System.Net;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>kronikol query</c> prints and reads numbers the same way on every machine (plans/WARM_UP_PLAN.md F12, R0). Until
/// 4.7.3 every duration it printed took the machine's decimal separator (<c>1,27 s</c>, Slowest's <c>1,27s</c>), so a
/// script or an agent reading the output met a second format, and <c>--slower-than 1.5</c> on a German machine read
/// the full stop as a thousands separator and filtered at 15 seconds. The facts run under every culture of
/// <see cref="CultureRun.Cultures"/>; the last one runs every verb and holds its whole output to what it prints under
/// en-US.
/// </summary>
public class QueryCultureTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-query-culture").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Summary_prints_slowest_durations_with_a_point(string culture)
    {
        var (output, _, _) = CultureRun.Under(culture, () => Run("summary", Report()));

        Assert.Contains("  s1  2.54s  Cancel an order\n", output, StringComparison.Ordinal);
        Assert.Contains("  s0  1.27s  Order status should return order details\n", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Interactions_print_call_durations_with_a_point(string culture)
    {
        var (output, _, _) = CultureRun.Under(culture, () => Run("interactions", Report(), "s0"));

        Assert.Contains(" 1.27 s", output, StringComparison.Ordinal);
        Assert.Contains("   35 ms", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Slower_than_reads_its_seconds_with_a_point(string culture)
    {
        var (output, error, exit) = CultureRun.Under(culture, () => Run("scenarios", Report(), "--slower-than", "1.5"));

        // 1.5 seconds: the 2.54 s scenario and not the 1.27 s one. Read as 15 seconds it was neither.
        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Contains("Cancel an order", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Order status should return order details", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(CultureRun.Data), MemberType = typeof(CultureRun))]
    public void Slower_than_refuses_a_decimal_comma_rather_than_reading_it_as_thousands(string culture)
    {
        var (_, error, exit) = CultureRun.Under(culture, () => Run("scenarios", Report(), "--slower-than", "1,5"));

        Assert.Equal(2, exit);
        Assert.Contains("--slower-than takes a number of seconds", error, StringComparison.Ordinal);
    }

    /// <summary>Every verb, with arguments that make it print what it prints about durations, steps and calls.</summary>
    private static readonly (string Verb, string[] Args)[] Calls =
    [
        ("summary", []),
        ("scenarios", []),
        ("services", ["--sort", "duration"]),
        ("failures", []),
        ("repro", []),
        ("steps", ["s0"]),
        ("assertions", []),
        ("flow", ["s0"]),
        ("annotations", ["s0"]),
        ("values", ["--path", "$.total", "--stats"]),
        ("interactions", ["--group-by", "service,status"]),
        ("http", ["s0/i0"]),
        ("body", ["{body}"]),
        ("note", ["s0/d0"]),
        ("diagram", ["s0/d0", "--out", "{diagram}"]),
        ("grep", ["4173", "--number", "--tolerance", "0.5"]),
        ("trace", ["s0/i0"]),
        ("compare", ["s0", "s1"]),
        ("diff", ["{later}"]),
        // This test process runs with KRONIKOL_HISTORY=off, so the ledger is named: {ledger} is an empty one.
        ("history", ["--history", "{ledger}"]),
    ];

    public static TheoryData<string, string> VerbsUnderCultures()
    {
        var data = new TheoryData<string, string>();
        foreach (var (verb, _) in Calls)
            foreach (var culture in CultureRun.Cultures)
                data.Add(verb, culture);
        return data;
    }

    [Theory]
    [MemberData(nameof(VerbsUnderCultures))]
    public void Every_verb_prints_under_the_culture_what_it_prints_under_en_us(string verb, string culture)
    {
        var args = Calls.Single(c => c.Verb == verb).Args;

        var expected = CultureRun.Under("en-US", () => Run(verb, Report(), Resolve(args)));
        var expectedDiagram = verb == "diagram" ? File.ReadAllText(Diagram) : null;
        var actual = CultureRun.Under(culture, () => Run(verb, Report(), Resolve(args)));

        Assert.True(expected.Exit == 0, $"exit {expected.Exit}: {expected.Error}");
        Assert.Equal(expected.Output, actual.Output);
        Assert.Equal(expected.Error, actual.Error);
        if (expectedDiagram is not null)
            Assert.Equal(expectedDiagram, File.ReadAllText(Diagram));
    }

    [Fact]
    public void Every_verb_is_held_to_it()
    {
        Assert.Equal(VerbTable.Names.Order(StringComparer.Ordinal), Calls.Select(c => c.Verb).Order(StringComparer.Ordinal));
    }

    // ─── Harness ───────────────────────────────────────────────

    private static (string Output, string Error, int Exit) Run(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"), exit);
    }

    private string Diagram => Path.Combine(_directory, "diagram.puml");

    private string[] Resolve(string[] args)
    {
        var ledger = Path.Combine(_directory, "history.jsonl");
        if (!File.Exists(ledger))
            File.WriteAllText(ledger, "");
        return args.Select(a => a switch
        {
            "{ledger}" => ledger,
            "{later}" => Later(),
            "{diagram}" => Diagram,
            "{body}" => BodyAddress(),
            _ => a
        }).ToArray();
    }

    private string BodyAddress()
    {
        var (output, _, _) = Run("http", Report(), "s0/i0");
        return System.Text.RegularExpressions.Regex.Match(output, @"b:[0-9a-f]{8}").Value;
    }

    // ─── Fixture ───────────────────────────────────────────────

    private string? _report;
    private string? _later;

    /// <summary>
    /// s0 passes in 1.27 s, its first call took 1,265.4 ms and its second 35 ms; s1 fails in 2.54 s. Every duration has
    /// a fraction, so every place that prints one prints a decimal separator.
    /// </summary>
    private string Report() => _report ??= Write("TestRunReport.json", orderStatusSeconds: 1.27);

    /// <summary>The same run with s0 at 3.1 s, so <c>diff</c>'s Slower list prints two durations.</summary>
    private string Later() => _later ??= Write("Later.json", orderStatusSeconds: 3.1);

    private string Write(string name, double orderStatusSeconds)
    {
        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "q0", DisplayName = "Order status should return order details", Result = ExecutionResult.Passed,
                        Duration = TimeSpan.FromSeconds(orderStatusSeconds),
                        Steps =
                        [
                            new ScenarioStep { Keyword = "When", Text = "I ask for the order's status", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(1301.5) },
                            new ScenarioStep { Keyword = "Then", Text = "the total is 4173", Status = ExecutionResult.Passed, Duration = TimeSpan.FromMilliseconds(2.5) }
                        ]
                    },
                    new Scenario
                    {
                        Id = "q1", DisplayName = "Cancel an order", Result = ExecutionResult.Failed, Duration = TimeSpan.FromSeconds(2.54),
                        ErrorMessage = "Expected 4173 but found 3902",
                        ErrorStackTrace = "   at OrderTests.Cancel() in OrderTests.cs:line 42",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Failed, Duration = TimeSpan.FromMilliseconds(12.5),
                                FailureMessage = "Expected 4173 but found 3902", SourceFile = "OrderTests.cs", SourceLine = 42
                            }
                        ]
                    }
                ]
            }
        ];

        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        RequestResponseLog[] logs =
        [
            .. Call("q0", at, HttpMethod.Post, "/orders", 1265.4, "{\"total\":4173}"),
            .. Call("q0", at.AddSeconds(2), HttpMethod.Get, "/orders/1", 35, "{\"total\":4173.5}"),
            .. Call("q1", at.AddSeconds(5), HttpMethod.Delete, "/orders/1", 12.25, "{\"total\":3902}")
        ];
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams =
        [
            new("q0", "", "@startuml\nTest -> orders : POST /orders\nnote left\n{\"total\":4173}\nend note\n@enduml")
        ];

        var written = ReportGenerator.GenerateTestRunReportData(features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "QueryCulture_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams, logs);
        var path = Path.Combine(_directory, name);
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static RequestResponseLog[] Call(string testId, DateTimeOffset at, HttpMethod method, string path, double ms, string answer)
    {
        var id = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, method, "{\"amount\":4173}", new Uri("http://orders" + path), [],
                "orders", "Test", RequestResponseType.Request, traceId, id, false)
            { Timestamp = at, ActivityTraceId = "4bf92f35feedfacefeedface" + Convert.ToHexStringLower(BitConverter.GetBytes(at.ToUnixTimeSeconds()))[..8], ActivitySpanId = "1111222233334444" },
            new RequestResponseLog(testId, testId, method, answer, new Uri("http://orders" + path), [],
                "orders", "Test", RequestResponseType.Response, traceId, id, false, HttpStatusCode.OK)
            { Timestamp = at.AddMilliseconds(ms) }
        ];
    }
}
