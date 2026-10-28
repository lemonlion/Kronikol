using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;
using DependencyCategories = Kronikol.Constants.DependencyCategories;

namespace Kronikol.Tests.History;

/// <summary>
/// The move to rule 8, replayed (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.8, 6.2). One suite's runs, recorded twice: once
/// as it happened, the first runs by releases of rules 6 and 7 and the rest by this one, and once by this one throughout.
/// After the move the two ledgers give the same verdicts, scenario by scenario, except where rule 8 sees more than 7
/// did. Before #192 R1 the move cost every scenario a quiet run and restarted its memory.
/// </summary>
public class HistoryMoveReplayTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-move-replay").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly string[] Ids = ["s0", "s1", "s2", "s3", "s4"];

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Kitchen",
            Scenarios =
            [
                new Scenario { Id = "s0", DisplayName = "Read the menu", Result = ExecutionResult.Passed },
                new Scenario { Id = "s1", DisplayName = "Save an order", Result = ExecutionResult.Passed },
                new Scenario { Id = "s2", DisplayName = "Look up a user", Result = ExecutionResult.Passed },
                new Scenario { Id = "s3", DisplayName = "Read one menu or the other", Result = ExecutionResult.Passed },
                new Scenario { Id = "s4", DisplayName = "Call the orders host", Result = ExecutionResult.Passed }
            ]
        }
    ];

    private static IEnumerable<RequestResponseLog> Pair(string test, OneOf<HttpMethod, string> method, Uri uri, string service, string? content = null,
        string? category = null, Func<RequestResponseLog, RequestResponseLog>? request = null)
    {
        var pair = Guid.NewGuid();
        var sent = new RequestResponseLog("t", test, method, content, uri, [], service, "Caller", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false,
            DependencyCategory: category);
        yield return request is null ? sent : request(sent);
        yield return new RequestResponseLog("t", test, method, null, uri, [], service, "Caller", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false,
            StatusCode: HttpStatusCode.OK, DependencyCategory: category);
    }

    /// <summary>
    /// Run <paramref name="n"/>'s calls: a read that never changes; an order saved in a batch that opens with two SET
    /// statements, which the classifier read before 8 as no statement and its tracker did not record; a GraphQL
    /// operation renamed at the fifth run, which only rule 8 can see; a menu read from one place for three runs, another
    /// for three, then the first again; and a host the HTTP handler names by its address, which rule 6 named localhost.
    /// </summary>
    private static RequestResponseLog[] Calls(int n) =>
    [
        .. Pair("s0", HttpMethod.Get, new Uri($"http://api/menu/{n}"), "Api"),
        .. Pair("s1", HttpMethod.Get, new Uri($"http://api/orders/{n}"), "Api"),
        .. Pair("s1", "INSERT", new Uri("sql:///OrdersDb/Orders"), "OrdersDb", $"SET NOCOUNT ON; SET XACT_ABORT ON;\nINSERT INTO [Orders] VALUES ({n})",
            DependencyCategories.SqlServer, r => r with { RecordedBefore8 = RecordedBefore.NotRecorded }),
        .. Pair("s2", HttpMethod.Post, new Uri("http://api/graphql"), "Api", $$"""{"query":"query {{(n < 5 ? "GetUser" : "GetUserV2")}} { user { name } }"}"""),
        .. Pair("s3", HttpMethod.Get, new Uri($"http://api/{(n is >= 4 and <= 6 ? "lunch" : "breakfast")}/menu"), "Api"),
        .. Pair("s4", HttpMethod.Get, new Uri("http://orders-host:8080/orders"), "orders-host:8080", request: r => r with { NamedByRemoteHostFallback = true })
    ];

    private ReportConfigurationOptions Options(string ledger) => new() { HistoryFilePath = ledger, HistoryBranch = "", HistoryMinRuns = 2 };

    /// <summary>Run <paramref name="n"/> recorded by this release, into <paramref name="ledger"/>, with its verdicts.</summary>
    private HistoryVerdicts Record(string ledger, int n)
    {
        var context = HistoryRunContext.Create(Features(), Calls(n), "Suite", null, At.AddHours(n), Options(ledger), _dir, "5.17.0", _ => null, _dir)!;
        Assert.Equal(HistoryAppendOutcome.Appended, context.Append()!.Outcome);
        return context.Verdicts!;
    }

    /// <summary>
    /// Run <paramref name="n"/>'s line as a release of rule <paramref name="version"/> wrote it: fingerprinted and counted
    /// under that rule, its pairs as the rule named them, and, for 7, the bridge to 6 it carried (#190 R0).
    /// </summary>
    private void RecordAsReleased(string ledger, int n, int version)
    {
        var logs = Calls(n);
        var built = HistoryRunBuilder.Build(Features(), logs, "Suite", null, At.AddHours(n),
            new HistoryBuildOptions { Durations = true, Shapes = true, ErrorKeys = true, Bridges = true });
        var lines = new string[Ids.Length][];
        var sets = new string[Ids.Length];
        var ordered = new string[Ids.Length];
        var counts = new int[Ids.Length];
        for (var i = 0; i < Ids.Length; i++)
        {
            var calls = InteractionShape.Calls(logs.Where(l => l.TestId == Ids[i]), null, version);
            (sets[i], ordered[i], counts[i]) = InteractionShape.Fingerprint(calls);
            lines[i] = calls.Select(c => c.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        }
        var shapes = HistoryShapes.Create(lines.SelectMany(l => l));
        var index = shapes.Calls.Select((line, position) => (line, position)).ToDictionary(p => p.line, p => p.position, StringComparer.Ordinal);
        var renamed = InteractionShape.RenamedDependencies(logs, version);
        var run = built.Run with
        {
            ShapeVersion = version,
            ShapeSet = sets,
            ShapeOrdered = ordered,
            Calls = counts,
            ShapesHash = shapes.Hash,
            CallSets = lines.Select(l => (IReadOnlyList<int>)l.Select(line => index[line]).OrderBy(i => i).ToArray()).ToArray(),
            Deps = built.Run.Deps!.Select(d => renamed.TryGetValue(d, out var then) ? then : d).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Generator = version == 6 ? "5.12.1" : "5.16.1",
            Bridge = version == 7 ? new Dictionary<string, HistoryBridge> { ["6"] = built.Run.Bridge!["6"] } : null
        };
        Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(ledger, built.Roster, run, run.Generator!, shapes: shapes).Outcome);
    }

    [Fact]
    public void A_ledger_recorded_under_rules_6_and_7_reads_the_same_verdicts_after_the_move()
    {
        var moved = Path.Combine(_dir, "moved", "history.jsonl");
        var throughout = Path.Combine(_dir, "throughout", "history.jsonl");
        for (var n = 1; n <= 4; n++)
        {
            RecordAsReleased(moved, n, n <= 2 ? 6 : 7);
            Record(throughout, n);
        }

        for (var n = 5; n <= 8; n++)
        {
            var afterMove = Record(moved, n);
            var underEight = Record(throughout, n);
            for (var i = 0; i < Ids.Length; i++)
            {
                var a = afterMove.Scenarios[i];
                var b = underEight.Scenarios[i];
                if (n == 5 && i == 2)
                {
                    // The one thing rule 8 sees that 7 did not: the GraphQL operation renamed at the fifth run. Through the bridge
                    // the first run after the move is compared as 7 read it, so the rename falls on the move.
                    Assert.Contains(HistoryVerdictKind.BehaviourChanged, b.Verdicts);
                    Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, a.Verdicts);
                    continue;
                }
                Assert.True(a.Verdicts.Order().SequenceEqual(b.Verdicts.Order()),
                    $"run {n}, {Ids[i]}: [{string.Join(", ", a.Verdicts)}] after the move, [{string.Join(", ", b.Verdicts)}] under 8 throughout. {a.Evidence} | {b.Evidence}");
            }
            Assert.Equal(underEight.NewDependencies, afterMove.NewDependencies);
        }
    }

    [Fact]
    public void A_call_rule_7_did_not_record_is_no_change_in_the_count_across_the_move()
    {
        // Rule 8 records the order's SET batch, which rule 7's tracker left out: the same scenario makes one more call,
        // and that is the move, not the scenario.
        var ledger = Path.Combine(_dir, "count", "history.jsonl");
        for (var n = 1; n <= 4; n++)
            RecordAsReleased(ledger, n, 7);

        for (var n = 5; n <= 8; n++)
        {
            var saved = Record(ledger, n).Scenarios[1];
            Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, saved.Verdicts);
            Assert.DoesNotContain("number of times", saved.Evidence, StringComparison.Ordinal);
            Assert.DoesNotContain("calls 1 in", saved.Evidence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_changed_graphql_operation_reads_behaviour_changed()
    {
        var ledger = Path.Combine(_dir, "graphql", "history.jsonl");
        for (var n = 1; n <= 4; n++)
            Assert.DoesNotContain(HistoryVerdictKind.BehaviourChanged, Record(ledger, n).Scenarios[2].Verdicts);

        var renamed = Record(ledger, 5).Scenarios[2];

        Assert.Contains(HistoryVerdictKind.BehaviourChanged, renamed.Verdicts);
        Assert.Contains("/graphql query GetUserV2", renamed.Evidence, StringComparison.Ordinal);
    }
}
