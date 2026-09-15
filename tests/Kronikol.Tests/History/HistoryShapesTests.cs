using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// The calls behind the fingerprint, spelled out (3.17.0). A behaviour verdict used to say "calls 3 to 4":
/// the run line carried a hash of the set and a count, so a reader who wanted the call had to open two
/// reports. The run's distinct calls are now interned on a <c>shapes</c> line, keyed by content like a
/// roster, and each position carries its calls as indices into it, so the verdict names what appeared and
/// what disappeared.
/// </summary>
public class HistoryShapesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-shapes").FullName;
    private static readonly DateTimeOffset At = new(2026, 9, 12, 10, 4, 11, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string LedgerPath => Path.Combine(_dir, ".kronikol", "history.jsonl");

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios =
            [
                new Scenario { Id = "t1", DisplayName = "Pay with a valid card", Result = ExecutionResult.Passed },
                new Scenario { Id = "t2", DisplayName = "Pay with an expired card", Result = ExecutionResult.Passed }
            ]
        }
    ];

    private static RequestResponseLog[] Pair(string testId, string path, HttpStatusCode status = HttpStatusCode.OK)
    {
        var pair = Guid.NewGuid();
        return
        [
            new RequestResponseLog("Pay", testId, HttpMethod.Get, null, new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false),
            new RequestResponseLog("Pay", testId, HttpMethod.Get, null, new Uri("http://orders" + path), [], "orders", "Test",
                RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: status)
        ];
    }

    private static HistoryRoster Roster(params string[] ids) =>
        HistoryRoster.Create("Suite", ids.Select((id, i) => new HistoryRosterEntry(id, "Scenario " + i, "Feature", null)).ToArray());

    /// <summary>A run line whose positions index into <paramref name="shapes"/> by the call lines given.</summary>
    private static HistoryRun Run(HistoryRoster roster, string id, HistoryShapes shapes, params string[][] callsPerPosition) => new()
    {
        Id = id,
        Suite = roster.Suite,
        Partial = false,
        At = At.AddHours(int.Parse(id.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture)),
        Branch = "main",
        Commit = "abc1234",
        Provider = "GitHubActions",
        Url = null,
        Shards = 1,
        RosterHash = roster.Hash,
        Results = new string('P', roster.Count),
        Attempts = new string(HistoryFormat.AttemptUnknown, roster.Count),
        Calls = callsPerPosition.Select(c => c.Length).ToArray(),
        ShapeSet = callsPerPosition.Select(c => InteractionShape.Hash8(string.Join("\n", c.OrderBy(l => l, StringComparer.Ordinal)))).ToArray(),
        ShapeOrdered = callsPerPosition.Select(c => InteractionShape.Hash8(string.Join("\n", c))).ToArray(),
        ShapeVersion = InteractionShape.Version,
        ShapesHash = shapes.Hash,
        CallSets = callsPerPosition.Select(c => (IReadOnlyList<int>)c.Select(line => ((IList<string>)shapes.Calls).IndexOf(line)).OrderBy(i => i).ToArray()).ToArray(),
        Errors = null,
        ErrorText = new Dictionary<string, string>(),
        Deps = ["Test>orders"]
    };

    private const string Orders = "Test>orders GET /api/orders 200";
    private const string OrderById = "Test>orders GET /api/orders/{n} 200";
    private const string Lines = "Test>orders GET /api/orders/{n}/lines 200";

    // ─── Building ──────────────────────────────────────────────

    [Fact]
    public void The_builder_interns_the_distinct_calls_and_each_position_indexes_into_them()
    {
        RequestResponseLog[] logs = [.. Pair("t1", "/api/orders/4711"), .. Pair("t1", "/api/orders/4711"), .. Pair("t2", "/api/orders"), .. Pair("t2", "/api/orders/9")];

        var (roster, run, shapes) = HistoryRunBuilder.Build(Features(), logs, "Suite", null, At, new HistoryBuildOptions());

        Assert.NotNull(shapes);
        Assert.Equal([Orders, OrderById], shapes.Calls);
        Assert.Equal(HistoryShapes.ComputeHash(shapes.Calls), shapes.Hash);
        Assert.Equal(shapes.Hash, run.ShapesHash);
        Assert.Equal(2, roster.Count);
        Assert.Equal([1], run.CallSetAt(0));       // the repeated call, once
        Assert.Equal([0, 1], run.CallSetAt(1));
        Assert.Equal([2, 2], run.Calls);
        // The fingerprint is the hash of exactly what the call set spells out.
        Assert.Equal(InteractionShape.Hash8(OrderById), run.ShapeSet![0]);
        Assert.Equal(InteractionShape.Hash8(Orders + "\n" + OrderById), run.ShapeSet[1]);
    }

    [Fact]
    public void Shapes_switched_off_records_no_list()
    {
        var (_, run, shapes) = HistoryRunBuilder.Build(Features(), Pair("t1", "/api/orders"), "Suite", null, At, new HistoryBuildOptions { Shapes = false });

        Assert.Null(shapes);
        Assert.Null(run.ShapesHash);
        Assert.Null(run.CallSets);
    }

    // ─── Ledger ────────────────────────────────────────────────

    [Fact]
    public void A_shapes_line_is_written_once_and_the_run_lines_read_back_against_it()
    {
        var roster = Roster("a1", "b2");
        var shapes = HistoryShapes.Create([OrderById, Orders]);
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", shapes, [Orders], [OrderById]), "3.17.0", shapes: shapes);
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:2:1", shapes, [Orders, OrderById], []), "3.17.0", shapes: shapes);

        var lines = File.ReadAllLines(LedgerPath);
        var shapesLine = Assert.Single(lines, l => l.StartsWith("{\"t\":\"shapes\"", StringComparison.Ordinal));
        Assert.Equal(lines[2], shapesLine); // after the header and the roster, before the first run
        var parsed = HistoryJson.Parse(shapesLine);
        Assert.Equal(HistoryLineKind.Shapes, parsed.Kind);
        Assert.Equal(shapes.Hash, parsed.Shapes!.Hash);
        Assert.Equal([Orders, OrderById], parsed.Shapes.Calls);

        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.Equal([Orders, OrderById], ledger.Shapes(shapes.Hash)!.Calls);
        Assert.Equal(1, ledger.Stats.ShapesKept);
        var runs = ledger.Runs("Suite");
        Assert.Equal(shapes.Hash, runs[1].ShapesHash);
        Assert.Equal([0, 1], runs[1].CallSetAt(0));
        Assert.Empty(runs[1].CallSetAt(1)!);
        Assert.Empty(HistoryLedgerReader.Verify(LedgerPath));
    }

    [Fact]
    public void A_run_appended_without_its_shapes_list_is_written_without_the_references()
    {
        // An older caller, or a run rebuilt from a report: the hash and the indices mean nothing without
        // the list, so the line carries neither, and the file still verifies.
        var roster = Roster("a1");
        var shapes = HistoryShapes.Create([Orders]);

        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", shapes, [Orders]), "3.17.0");

        var run = Assert.Single(HistoryLedgerReader.Read(LedgerPath, 50).Ledger!.Runs("Suite"));
        Assert.Null(run.ShapesHash);
        Assert.Null(run.CallSets);
        Assert.NotNull(run.ShapeSet);
        Assert.Empty(HistoryLedgerReader.Verify(LedgerPath));
    }

    [Fact]
    public void A_run_that_names_a_different_shapes_list_from_the_one_given_is_refused()
    {
        var roster = Roster("a1");
        var shapes = HistoryShapes.Create([Orders]);

        Assert.Throws<ArgumentException>(() => HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", shapes, [Orders]), "3.17.0", shapes: HistoryShapes.Create([Lines])));
    }

    [Fact]
    public void Verify_reports_a_missing_or_tampered_shapes_line()
    {
        var roster = Roster("a1");
        var shapes = HistoryShapes.Create([Orders]);
        HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, "gh:1:1", shapes, [Orders]), "3.17.0", shapes: shapes);

        var lines = File.ReadAllLines(LedgerPath).ToList();
        var shapesLine = lines.Single(l => l.StartsWith("{\"t\":\"shapes\"", StringComparison.Ordinal));
        File.WriteAllLines(LedgerPath, lines.Where(l => l != shapesLine));
        Assert.Contains(HistoryLedgerReader.Verify(LedgerPath), f => f.Contains("references shapes " + shapes.Hash, StringComparison.Ordinal));

        // The calls are JSON-escaped on the line, so the tamper is the hash: a list that no longer hashes to its key.
        File.WriteAllLines(LedgerPath, lines.Select(l => l == shapesLine ? l.Replace(shapes.Hash, new string('0', 16), StringComparison.Ordinal) : l));
        Assert.Contains(HistoryLedgerReader.Verify(LedgerPath), f => f.Contains("does not hash its own calls", StringComparison.Ordinal));
    }

    [Fact]
    public void Prune_drops_the_shapes_lines_no_surviving_run_references()
    {
        var roster = Roster("a1");
        var old = HistoryShapes.Create([Orders]);
        var recent = HistoryShapes.Create([Orders, OrderById]);
        for (var i = 1; i <= 3; i++)
            HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, $"gh:{i}:1", old, [Orders]), "3.17.0", shapes: old);
        for (var i = 4; i <= 5; i++)
            HistoryLedgerWriter.Append(LedgerPath, roster, Run(roster, $"gh:{i}:1", recent, [Orders, OrderById]), "3.17.0", shapes: recent);

        var pruned = HistoryLedgerWriter.Prune(LedgerPath, window: 2, "3.17.0");

        Assert.Equal(1, pruned.ShapesDropped);
        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.Null(ledger.Shapes(old.Hash));
        Assert.NotNull(ledger.Shapes(recent.Hash));
        Assert.Empty(HistoryLedgerReader.Verify(LedgerPath));
    }

    [Fact]
    public void A_fragment_carries_the_shapes_and_the_fold_re_indexes_two_shards_into_one_list()
    {
        var a = Roster("a1");
        var b = Roster("b2");
        var shapesA = HistoryShapes.Create([OrderById]);
        var shapesB = HistoryShapes.Create([Lines, Orders]);
        var fragmentA = HistoryFragment.Parse(HistoryFragment.Write(a, Run(a, "gh:7:1", shapesA, [OrderById]), "3.17.0", shapesA));
        var fragmentB = HistoryFragment.Parse(HistoryFragment.Write(b, Run(b, "gh:7:1", shapesB, [Orders, Lines]), "3.17.0", shapesB));
        Assert.Equal([OrderById], fragmentA.Shapes!.Calls);

        var (roster, run, shapes) = Assert.Single(HistoryFold.Fold([fragmentA, fragmentB]));

        Assert.Equal(2, roster.Count);
        Assert.NotNull(shapes);
        Assert.Equal([Orders, OrderById, Lines], shapes.Calls);
        Assert.Equal(shapes.Hash, run.ShapesHash);
        Assert.Equal([1], run.CallSetAt(0));
        Assert.Equal([0, 2], run.CallSetAt(1));
    }

    [Fact]
    public void A_fragment_from_before_the_lists_folds_without_them()
    {
        var a = Roster("a1");
        var run = Run(a, "gh:7:1", HistoryShapes.Create([Orders]), [Orders]) with { ShapesHash = null, CallSets = null };

        var folded = Assert.Single(HistoryFold.Fold([new HistoryFragment(1, a, run)]));

        Assert.Null(folded.Shapes);
        Assert.Null(folded.Run.CallSets);
    }

    // ─── Verdicts ──────────────────────────────────────────────

    private static HistoryLedger Ledger(HistoryShapes shapes, params HistoryRun[] runs)
    {
        var roster = Roster("a1");
        var text = HistoryJson.HeaderLine("test") + "\n" + HistoryJson.RosterLine(roster) + "\n" + HistoryJson.ShapesLine(shapes) + "\n";
        foreach (var run in runs)
            text += HistoryJson.RunLine(run) + "\n";
        return HistoryLedgerReader.Parse(text, window: 50).Ledger!;
    }

    [Fact]
    public void A_behaviour_change_names_the_call_that_appeared_and_the_one_that_disappeared()
    {
        var roster = Roster("a1");
        var before = HistoryShapes.Create([Orders, OrderById]);
        var ledger = Ledger(before, Run(roster, "gh:1:1", before, [Orders, OrderById]), Run(roster, "gh:2:1", before, [Orders, OrderById]));
        var now = HistoryShapes.Create([Orders, Lines]);

        var scenario = HistoryAnalyzer.Analyse(ledger, roster, Run(roster, "gh:3:1", now, [Orders, Lines]), new HistoryAnalysisOptions(), shapes: now).Scenarios[0];

        Assert.Equal(HistoryVerdictKind.BehaviourChanged, scenario.Primary);
        Assert.Equal([Lines], scenario.NewCalls);
        Assert.Equal([OrderById], scenario.GoneCalls);
        Assert.Contains("; new: " + Lines, scenario.Evidence);
        Assert.Contains("; gone: " + OrderById, scenario.Evidence);
    }

    [Fact]
    public void A_change_against_a_line_that_predates_the_lists_is_counted_not_named()
    {
        var roster = Roster("a1");
        var before = HistoryShapes.Create([Orders, OrderById]);
        var old = Run(roster, "gh:1:1", before, [Orders, OrderById]) with { ShapesHash = null, CallSets = null };
        var ledger = Ledger(before, old);
        var now = HistoryShapes.Create([Orders, Lines]);

        var scenario = HistoryAnalyzer.Analyse(ledger, roster, Run(roster, "gh:3:1", now, [Orders, Lines]), new HistoryAnalysisOptions(), shapes: now).Scenarios[0];

        Assert.Equal(HistoryVerdictKind.BehaviourChanged, scenario.Primary);
        Assert.Empty(scenario.NewCalls);
        Assert.Empty(scenario.GoneCalls);
        Assert.DoesNotContain("new:", scenario.Evidence);
        Assert.Contains("the same number of calls", scenario.Evidence);
    }

    [Fact]
    public void Many_new_calls_are_capped_in_the_evidence_but_all_listed_on_the_verdict()
    {
        var roster = Roster("a1");
        var before = HistoryShapes.Create([Orders]);
        var ledger = Ledger(before, Run(roster, "gh:1:1", before, [Orders]));
        string[] added = ["Test>orders GET /a 200", "Test>orders GET /b 200", "Test>orders GET /c 200", "Test>orders GET /d 200", "Test>orders GET /e 200"];
        var now = HistoryShapes.Create([Orders, .. added]);

        var scenario = HistoryAnalyzer.Analyse(ledger, roster, Run(roster, "gh:2:1", now, [Orders, .. added]), new HistoryAnalysisOptions(), shapes: now).Scenarios[0];

        Assert.Equal(5, scenario.NewCalls.Count);
        Assert.Contains("; new: Test>orders GET /a 200, Test>orders GET /b 200, Test>orders GET /c 200 and 2 more", scenario.Evidence);
    }

    [Fact]
    public void The_run_context_writes_the_shapes_into_the_fragment_and_the_ledger()
    {
        RequestResponseLog[] logs = [.. Pair("t1", "/api/orders/4711"), .. Pair("t2", "/api/orders")];
        var options = new ReportConfigurationOptions { HistoryFilePath = LedgerPath };

        var context = HistoryRunContext.Create(Features(), logs, "Suite", null, At, options, _dir, "3.17.0", _ => null, _dir)!;
        context.Append();

        Assert.NotNull(context.Shapes);
        Assert.Equal([Orders, OrderById], context.Shapes.Calls);
        var fragment = HistoryFragment.Parse(context.Fragment());
        Assert.Equal(context.Shapes.Hash, fragment.Shapes!.Hash);
        Assert.Equal(context.Shapes.Hash, fragment.Run.ShapesHash);
        var ledger = HistoryLedgerReader.Read(LedgerPath, 50).Ledger!;
        Assert.NotNull(ledger.Shapes(context.Shapes.Hash));
        Assert.Empty(HistoryLedgerReader.Verify(LedgerPath));
    }
}
