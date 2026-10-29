using System.Net;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;
using DependencyCategories = Kronikol.Constants.DependencyCategories;

namespace Kronikol.Tests.History;

/// <summary>
/// The run's calls as the templater takes them in, before any rule, carried by the fragment (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md
/// 4.8, Q18). A run that only writes a fragment does not know the consumer's patterns its ledger's window was
/// fingerprinted under, so <c>history record</c>, which has the ledger, bridges an edit of them from these, as a run
/// that reads its ledger bridges it from its logs. They stay in the fragment: the ledger, which a public repository
/// publishes on its data branch, holds only what the rules made of them.
/// </summary>
public class HistoryRawCallsTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-raw-calls").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly HistoryShapeRules Narrow = HistoryShapeRules.Create([new HistoryShapeTemplate(@"tenant\d+x", "{tenant}")])!;
    private static readonly HistoryShapeRules Wide = HistoryShapeRules.Create([new HistoryShapeTemplate(@"key-tenant\d+x", "key-{tenant}"), new HistoryShapeTemplate(@"Orders", "{table}")])!;

    private static Feature[] Features(params string[] ids) =>
    [
        new Feature
        {
            DisplayName = "Kitchen",
            Scenarios = ids.Select(id => new Scenario { Id = id, DisplayName = "Scenario " + id, Result = ExecutionResult.Passed }).ToArray()
        }
    ];

    private static RequestResponseLog[] Pair(string test, OneOf<HttpMethod, string> method, Uri uri, string? content = null, string service = "Api",
        OneOf<HttpStatusCode, string>? status = null, string? category = null, bool answered = true)
    {
        var pair = Guid.NewGuid();
        var request = new RequestResponseLog("t", test, method, content, uri, [], service, "Caller", RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false,
            DependencyCategory: category);
        if (!answered)
            return [request];
        return
        [
            request,
            new RequestResponseLog("t", test, method, null, uri, [], service, "Caller", RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false,
                StatusCode: status ?? HttpStatusCode.OK, DependencyCategory: category)
        ];
    }

    private static RequestResponseLog[] With(RequestResponseLog[] pair, Func<RequestResponseLog, RequestResponseLog> request) => [request(pair[0]), .. pair[1..]];

    // A send that threw, answered by the tracker with the exception's type; the rules before 9 read it unanswered.
    private static RequestResponseLog[] Failed(RequestResponseLog[] pair) => [pair[0], pair[1] with { RecordedBefore9 = RecordedBefore.NotRecorded }];

    // The same for a tracker that began to answer one at 11; the rules before 11 read it unanswered.
    private static RequestResponseLog[] FailedBefore11(RequestResponseLog[] pair) => [pair[0], pair[1] with { RecordedBefore11 = RecordedBefore.NotRecorded }];

    /// <summary>
    /// A call of every form a rule reads differently: ids a pattern folds, a GraphQL operation in a body, in a query
    /// string and as a batch, a statement after a SET prologue that 6 and 7 read as their tracker recorded it then, one
    /// they did not record at all, a document's query, a host named by its address, a relative URI, escapes the data
    /// file keeps and ones it does not, an entity in the authority, a status given as text, and a call never answered;
    /// #125's: a call 6 to 8 read under another name, one they did not record, and a failed send they read as
    /// unanswered; and F26's, an Oracle call 6 to 9 read against localhost and unknown; and #125's three again, as
    /// its R2 made them, which 6 to 10 read so.
    /// </summary>
    private static RequestResponseLog[] Corpus(string first, string second, int run) =>
    [
        .. Pair(first, HttpMethod.Get, new Uri($"http://cache/menu/key-tenant{run}x?b=1&a=2"), service: "Cache"),
        .. Pair(first, HttpMethod.Post, new Uri("http://api/graphql"), """{"query":"query GetUser($id: ID!) { user(id: $id) { name } }","variables":{"id":"7"}}"""),
        .. Pair(first, HttpMethod.Get, new Uri("http://api/graphql?query=query%20Menu%20%7B%20items%20%7B%20name%20%7D%20%7D")),
        .. Pair(first, HttpMethod.Post, new Uri("http://api/graphql"), """[{"query":"{ a }"},{"query":"{ b }"}]"""),
        .. With(Pair(first, "SELECT", new Uri("sql:///OrdersDb/Orders"), $"SET NOCOUNT ON; SET XACT_ABORT ON;\nSELECT [o].[Id] FROM [Orders] AS [o] WHERE [o].[Id] = {run}",
                service: "OrdersDb", category: DependencyCategories.SqlServer),
            r => r with { RecordedBefore8 = new RecordedBefore("Other", new Uri("sql://server/OrdersDb")) }),
        .. With(Pair(first, "INSERT", new Uri("sql:///OrdersDb/Audit"), "SET NOCOUNT ON; SET XACT_ABORT ON;\nINSERT INTO [Audit] VALUES (1)", service: "OrdersDb",
                category: DependencyCategories.SqlServer),
            r => r with { RecordedBefore8 = RecordedBefore.NotRecorded }),
        .. Pair(second, HttpMethod.Post, new Uri("https://cosmos/dbs/menu/colls/items/docs"), """{"query":"SELECT * FROM c WHERE c.tenant = 'key-tenant5x'","parameters":[]}""",
            service: "Cosmos", category: DependencyCategories.CosmosDB),
        .. With(Pair(second, HttpMethod.Get, new Uri("http://orders-host:8080/orders/12"), service: "orders-host:8080"), r => r with { NamedByRemoteHostFallback = true }),
        .. Pair(second, HttpMethod.Put, new Uri($"/relative/key-tenant{run}x", UriKind.Relative)),
        .. Pair(second, HttpMethod.Get, new Uri("http://files/a%2Fb/c%20d/key%00tenant")),
        .. Pair(second, "Send", new Uri("servicebus://orders-queue/"), service: "Bus", category: DependencyCategories.ServiceBus, status: "Completed"),
        .. Pair(second, HttpMethod.Delete, new Uri($"http://cache/menu/key-tenant{run}x"), service: "Cache", answered: false),
        .. With(Pair(first, "Read", new Uri("http://127.0.0.1:9050/sales"), service: "BigQuery", category: "BigQuery"),
            r => r with { RecordedBefore9 = new RecordedBefore("Other", new Uri($"http://127.0.0.1:9050/projects/shop/datasets/sales?key=k&run={run}")) }),
        .. With(Pair(second, "Other", new Uri("gcs:///b/orders/acl/user-a"), service: "CloudStorage", category: "CloudStorage"),
            r => r with { RecordedBefore9 = RecordedBefore.NotRecorded }),
        .. Failed(Pair(second, "Insert", new Uri("http://127.0.0.1:9050/job"), service: "BigQuery", category: "BigQuery", status: "!HttpRequestException")),
        // F26's: an Oracle call 6 to 9 read against localhost and a database called unknown.
        .. With(Pair(second, "SELECT FROM breakfasts", new Uri("oracle://127.0.0.1:21521/FREEPDB1/breakfasts"), "SELECT id, name FROM breakfasts",
                service: "Oracle", category: DependencyCategories.Oracle, status: "OK"),
            r => r with { RecordedBefore10 = new RecordedBefore("SELECT FROM breakfasts", new Uri("oracle://localhost/unknown/breakfasts")) }),
        // #125 R2's: a container Azurite created, which 6 to 10 read as an upload on the account's path, a call they did
        // not record, and a failed send they read as unanswered.
        .. With(Pair(first, "CreateContainer", new Uri("http://127.0.0.1:10000/orders"), service: "BlobStorage", category: DependencyCategories.BlobStorage),
            r => r with { RecordedBefore11 = new RecordedBefore("Upload", new Uri($"http://127.0.0.1:10000/devstoreaccount1/orders?run={run}")) }),
        .. With(Pair(second, "Other", new Uri("storagequeue:///orders?comp=acl"), service: "StorageQueue", category: DependencyCategories.MessageQueue),
            r => r with { RecordedBefore11 = RecordedBefore.NotRecorded }),
        .. FailedBefore11(Pair(second, "Upload", new Uri("http://127.0.0.1:10000/orders/order-1.json"), service: "BlobStorage",
            category: DependencyCategories.BlobStorage, status: "!HttpRequestException")),
        // What a writer escapes: quotes, a backslash, angle brackets, a plus, non-ASCII and a control character.
        .. Pair(second, "SELECT", new Uri("sql:///Menu/Items"), "\"select a\\b <b>+ 'café'\u0001 FROM [Items] WHERE [Name] = 'crêpe' + '<i>'", service: "MenuDb",
            category: DependencyCategories.SqlServer)
    ];

    private static HistoryBuildResult Build(RequestResponseLog[] logs, HistoryShapeRules? rules, IReadOnlyList<HistoryShapeRules?>? window = null) =>
        HistoryRunBuilder.Build(Features("s1", "s2"), logs, "Suite", null, At, new HistoryBuildOptions
        {
            Durations = true, Shapes = true, ErrorKeys = true, Bridges = true, ShapeRules = rules, WindowRules = window
        });

    [Fact]
    public void A_fragment_carries_the_runs_untemplated_calls()
    {
        var built = Build(Corpus("s1", "s2", 7), Wide);

        var fragment = HistoryFragment.Parse(HistoryFragment.Write(built.Roster, built.Run, "5.17.0", built.Shapes, built.RawCalls));

        var raw = Assert.IsType<HistoryRawCalls>(fragment.RawCalls);
        Assert.Equal(2, raw.Order.Count);
        Assert.Contains(raw.At(0), call => call.Path == "/menu/key-tenant7x?b=1&a=2");
        Assert.Equal(built.RawCalls!.Calls, raw.Calls);
        Assert.Equal(built.RawCalls.Order.Select(o => o.ToArray()), raw.Order.Select(o => o.ToArray()));
    }

    [Fact]
    public void The_ledger_never_holds_a_raw_call()
    {
        var built = Build(Corpus("s1", "s2", 7), Wide);
        var fragment = HistoryFragment.Parse(HistoryFragment.Write(built.Roster, built.Run, "5.17.0", built.Shapes, built.RawCalls));
        var ledger = Path.Combine(_dir, "history.jsonl");

        Assert.Equal(HistoryAppendOutcome.Appended, HistoryLedgerWriter.Append(ledger, fragment.Roster, fragment.Run, "5.17.0", shapes: fragment.Shapes).Outcome);

        var text = File.ReadAllText(ledger);
        Assert.DoesNotContain("tenant7x", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rawCalls", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_kept_version_reads_the_same_calls_from_a_fragment_as_from_the_run()
    {
        var logs = Corpus("s1", "s2", 7);
        var built = Build(logs, Wide);
        var raw = HistoryFragment.Parse(HistoryFragment.Write(built.Roster, built.Run, "5.17.0", built.Shapes, built.RawCalls)).RawCalls!;

        string[] ids = ["s1", "s2"];
        foreach (var version in InteractionShape.KeptVersions)
            foreach (var rules in new[] { null, Narrow, Wide })
                for (var i = 0; i < ids.Length; i++)
                {
                    var own = logs.Where(l => l.TestId == ids[i]).ToArray();
                    Assert.Equal(InteractionShape.Calls(own, rules, version), InteractionShape.Calls(raw.At(i), rules, version));
                }
    }

    [Fact]
    public void A_bridge_made_from_a_fragment_is_the_one_the_run_makes_with_its_window_in_hand()
    {
        var logs = Corpus("s1", "s2", 7);
        // In process, the run read its ledger and bridged the window's earlier patterns from its logs.
        var inProcess = Build(logs, Wide, window: [Narrow]).Run;
        // In the fragment flow it could not, and record does it from the fragment.
        var written = Build(logs, Wide);
        var fragment = HistoryFragment.Parse(HistoryFragment.Write(written.Roster, written.Run, "5.17.0", written.Shapes, written.RawCalls));

        var recorded = HistoryBridges.WithWindowRules(fragment.Run, fragment.RawCalls, [Narrow]);

        foreach (var version in InteractionShape.KeptVersions)
        {
            var key = HistoryBridge.RuleKey(version, Narrow.Hash);
            var expected = inProcess.Bridge![key];
            var actual = recorded.Bridge![key];
            Assert.Equal(expected.ShapeSet, actual.ShapeSet);
            Assert.Equal(expected.ShapeOrdered, actual.ShapeOrdered);
            Assert.Equal(expected.ShapesHash, actual.ShapesHash);
            Assert.Equal(expected.CallSets?.Select(s => s.ToArray()), actual.CallSets?.Select(s => s.ToArray()));
            Assert.Equal(expected.Deps, actual.Deps);
            Assert.Equal(expected.Shapes?.Calls, actual.Shapes?.Calls);
        }
        Assert.Equal(inProcess.Bridge!.Keys.Order(StringComparer.Ordinal), recorded.Bridge!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_fragment_without_raw_calls_is_folded_as_it_was()
    {
        var built = Build(Corpus("s1", "s2", 7), Wide);

        var recorded = HistoryBridges.WithWindowRules(built.Run, null, [Narrow]);

        Assert.Same(built.Run, recorded);
    }
}
