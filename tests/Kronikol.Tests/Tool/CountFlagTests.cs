using System.Net;
using Kronikol.Reports;
using Kronikol.Tool;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>--count</c> prints one number and nothing else, on every verb that takes it: the answer to a yes/no
/// question in one token, with any caveat on stderr.
///
/// <para><c>flow</c>, <c>trace</c> and <c>compare</c> declared the flag in the per-verb table and never
/// read it, so each printed its whole answer where one number was documented; the refusal of a flag a
/// verb does not read could not catch it, because the table said they read it. <c>flow</c> and
/// <c>trace</c> count now (the calls shown, the calls on the trace). <c>compare</c> selects nothing to
/// count, so it no longer takes the flag and refuses it like any other flag it does not read.</para>
/// </summary>
public class CountFlagTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-count").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Every verb that takes <c>--count</c>, with the arguments it needs on <see cref="Report"/>.</summary>
    private static readonly (string Verb, string[] Args)[] Counting =
    [
        ("summary", []),
        ("scenarios", []),
        ("services", []),
        ("failures", []),
        ("repro", []),
        ("assertions", []),
        ("flow", ["s0"]),
        ("annotations", ["s0"]),
        ("values", ["--path", "$.total"]),
        ("interactions", []),
        ("grep", ["4173"]),
        ("trace", ["s0/i0"]),
        ("diff", ["s0/i1", "s1/i1"]),
        // This test process runs with KRONIKOL_HISTORY=off, so the ledger is named: {ledger} is an empty one.
        ("history", ["--history", "{ledger}"]),
    ];

    public static TheoryData<string, string[]> CountingVerbs()
    {
        var data = new TheoryData<string, string[]>();
        foreach (var (verb, args) in Counting)
            data.Add(verb, args);
        return data;
    }

    [Theory]
    [MemberData(nameof(CountingVerbs))]
    public void Count_prints_one_number_and_nothing_else(string verb, string[] args)
    {
        var ledger = Path.Combine(_directory, "history.jsonl");
        File.WriteAllText(ledger, "");

        var (output, error, exit) = RunFull(verb, Report(), [.. args.Select(a => a == "{ledger}" ? ledger : a), "--count"]);

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Matches(@"^\d+\n$", output);
    }

    [Fact]
    public void Every_verb_that_takes_count_is_held_to_it()
    {
        // A verb that declares the flag and is missing here is a verb nothing checks, which is how three
        // of them came to print their whole answer.
        var declared = QueryCommand.FlagsByVerb
            .Where(verb => verb.Value.Contains("--count", StringComparer.Ordinal))
            .Select(verb => verb.Key)
            .Order(StringComparer.Ordinal);

        Assert.Equal(declared, Counting.Select(c => c.Verb).Order(StringComparer.Ordinal));
    }

    // ─── trace ─────────────────────────────────────────────────

    [Fact]
    public void Trace_count_is_the_number_of_calls_on_the_trace()
    {
        var (output, _, _) = RunFull("trace", Report(), "s0/i0", "--count");

        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Trace_count_says_a_leaking_trace_on_stderr()
    {
        // The warning is the verb's second job, and a count is still an answer about that trace.
        var (output, error, exit) = RunFull("trace", Report(), "s0/i0", "--count");

        Assert.Equal(0, exit);
        Assert.Equal("2\n", output);
        Assert.Contains("spans 2 scenarios", error);
    }

    [Fact]
    public void Trace_count_says_a_span_id_on_stderr()
    {
        var (output, error, exit) = RunFull("trace", Report(), SpanId, "--count");

        Assert.Equal(0, exit);
        Assert.Equal("2\n", output);
        Assert.Contains("is a span id", error);
    }

    [Fact]
    public void Trace_without_count_still_says_both_in_the_answer()
    {
        var (output, _, _) = RunFull("trace", Report(), SpanId);

        Assert.Contains("is a span id", output);
        Assert.Contains("spans 2 scenarios", output);
        Assert.Contains("2 calls across 2 scenarios", output);
    }

    // ─── compare ───────────────────────────────────────────────

    [Fact]
    public void Compare_refuses_count_rather_than_printing_the_comparison()
    {
        var (output, error, exit) = RunFull("compare", Report(), "s0", "s1", "--count");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("compare does not read --count", error);
        // The refusal used to date every flag it refused to 3.1.0; this one was ignored until 3.30.3.
        Assert.DoesNotContain("3.1.0", error);
    }

    [Fact]
    public void Compare_still_compares()
    {
        var (output, error, exit) = RunFull("compare", Report(), "s0", "s1");

        Assert.True(exit == 0, $"exit {exit}: {error}");
        Assert.Contains("calls: 1 vs 1", output);
    }

    // ─── Harness ───────────────────────────────────────────────

    private (string Output, string Error, int Exit) RunFull(string command, string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run([command, report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    // ─── Fixture ───────────────────────────────────────────────

    /// <summary>A W3C trace id both scenarios' calls carry: the fixture leakage `trace` warns about.</summary>
    private const string LeakedTrace = "4bf92f35feedfacefeedfacefeedface";

    private const string SpanId = "1111222233334444";

    private string? _report;

    /// <summary>
    /// s0 fails its assertion and s1 passes; each made one call to payments on <see cref="LeakedTrace"/>,
    /// answered with a different total. s0 carries a Row annotation.
    /// </summary>
    private string Report()
    {
        if (_report is not null)
            return _report;

        Feature[] features =
        [
            new Feature
            {
                DisplayName = "Orders",
                Scenarios =
                [
                    new Scenario
                    {
                        Id = "t0", DisplayName = "Checkout fails on a wrong total", Result = ExecutionResult.Failed,
                        Duration = TimeSpan.FromSeconds(1), ErrorMessage = "Assert.Equal() Failure",
                        Steps =
                        [
                            new ScenarioStep
                            {
                                Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Failed,
                                FailureMessage = "Expected 4173 but found 3902", SourceFile = "CheckoutTests.cs", SourceLine = 42,
                                SubSteps =
                                [
                                    new ScenarioStep
                                    {
                                        Text = "total == 4173", Status = ExecutionResult.Failed,
                                        FailureMessage = "Expected 4173 but found 3902", SourceFile = "CheckoutTests.cs", SourceLine = 42
                                    }
                                ]
                            }
                        ]
                    },
                    new Scenario
                    {
                        Id = "t1", DisplayName = "Checkout passes", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                        Steps = [new ScenarioStep { Keyword = "Then", Text = "the total is right", Status = ExecutionResult.Passed }]
                    }
                ]
            }
        ];

        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>
        {
            Marker("t0", DiagramMarkerKind.Row, "hnote across #lightyellow : Row 3"),
            Marker("t0", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>the total is right"),
        };
        logs.AddRange(Charge("t0", at, "{\"total\":3902}", SpanId));
        logs.Add(Marker("t1", DiagramMarkerKind.Step, "hnote across <<stepDelimiter>> #black:<color:white>the total is right"));
        logs.AddRange(Charge("t1", at.AddSeconds(5), "{\"total\":4173}", "5555666677778888"));

        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Count_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams: null, logs.ToArray());

        var path = Path.Combine(_directory, "TestRunReport.json");
        File.Move(written, path, overwrite: true);
        return _report = path;
    }

    private static RequestResponseLog Marker(string testId, DiagramMarkerKind kind, string plantUml) =>
        new(testId, testId, "", "", new Uri("http://override.com"), [], "", "",
            RequestResponseType.Request, Guid.NewGuid(), Guid.NewGuid(), false)
        { IsOverrideStart = true, PlantUml = plantUml, MarkerKind = kind };

    private static RequestResponseLog[] Charge(string testId, DateTimeOffset at, string answer, string spanId)
    {
        var id = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        return
        [
            new RequestResponseLog(testId, testId, HttpMethod.Post, "{\"amount\":4173}", new Uri("http://payments/charge"), [],
                "payments", "test", RequestResponseType.Request, traceId, id, false)
            { Timestamp = at, ActivityTraceId = LeakedTrace, ActivitySpanId = spanId },
            new RequestResponseLog(testId, testId, HttpMethod.Post, answer, new Uri("http://payments/charge"), [],
                "payments", "test", RequestResponseType.Response, traceId, id, false, HttpStatusCode.OK)
            { Timestamp = at.AddMilliseconds(30) }
        ];
    }
}
