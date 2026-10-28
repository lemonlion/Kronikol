using System.Net;
using System.Text.Json;
using Kronikol.Parties;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// #192 R3 (plans/CALL_OPERATION_AND_ORIGIN_PLAN.md 4.3, Q4): who a call is from as a role, <c>origin</c> (<c>test</c>,
/// <c>subject</c> or <c>other</c>, the caller's kind in #190's party table), and why it was made, <c>cause</c>
/// (<c>test</c>, <c>request</c>, <c>background</c> or <c>unknown</c>, from the caller's role, the record's attribution and
/// the flow nesting), as dimensions of <c>interactions --group-by</c> and members of its <c>--json</c> rows.
/// </summary>
public class QueryOriginCauseTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-origin-cause").FullName;
    private int _tick;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static Feature[] Features() =>
    [
        new Feature
        {
            DisplayName = "Orders",
            Scenarios = [new Scenario { Id = "t0", DisplayName = "Place an order", Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1) }]
        }
    ];

    /// <summary>One call: its request, then whatever ran inside it, then its response, in capture order.</summary>
    private RequestResponseLog[] Call(string caller, string service, string uri, AttributionSource? source = null, string? callerCategory = null,
        Guid? traceId = null, params RequestResponseLog[][] inside)
    {
        var pair = Guid.NewGuid();
        var trace = traceId ?? Guid.NewGuid();
        var request = new RequestResponseLog("t0", "t0", HttpMethod.Get, null, new Uri(uri), [], service, caller, RequestResponseType.Request, trace, pair, false,
            CallerDependencyCategory: callerCategory) { Timestamp = Start.AddMilliseconds(++_tick * 10), AttributionSource = source };
        var nested = inside.SelectMany(c => c).ToArray();
        var response = new RequestResponseLog("t0", "t0", HttpMethod.Get, "{}", new Uri(uri), [], service, caller, RequestResponseType.Response, trace, pair, false,
            HttpStatusCode.OK, CallerDependencyCategory: callerCategory) { Timestamp = Start.AddMilliseconds(++_tick * 10), AttributionSource = source };
        return [request, .. nested, response];
    }

    private string Write(RequestResponseLog[] logs, PartyDeclarations? declared = null)
    {
        var parties = declared is null ? null : PartyResolver.Resolve(PartiesReport.CallsOf(logs), declared, PartyObservations.None);
        var written = ReportGenerator.GenerateTestRunReportData(Features(),
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Origin_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, null, logs, null, fullStepDetail: true, ciMetadata: null,
            suite: null, environment: null, attribution: null, parties: parties);
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N")[..8] + ".json");
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

    /// <summary>The value of one dimension for each edge, <c>caller &gt; service</c>, as <c>--group-by caller,service,DIM</c> buckets it.</summary>
    private static Dictionary<string, string> ByEdge(string report, string dimension)
    {
        var (output, error, exit) = Run("interactions", report, "--group-by", "caller,service," + dimension, "--json");
        Assert.True(exit == 0, error);
        return JsonDocument.Parse(output).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("key"))
            .ToDictionary(k => k.GetProperty("caller").GetString() + " > " + k.GetProperty("service").GetString(), k => k.GetProperty(dimension).GetString()!,
                StringComparer.Ordinal);
    }

    private RequestResponseLog[] Breakfast() =>
    [
        .. Call("Caller", "orders-api", "http://orders/orders", AttributionSource.TestContext, null, null,
            Call("orders-api", "Payments", "http://payments/charges", AttributionSource.RequestHeader)),
        .. Call("Kafka Broker", "orders-api", "kafka://orders-topic/orders", callerCategory: "Kafka")
    ];

    /// <summary>A test calling a gateway in front of the service whose suite it is: the inference takes the gateway for the subject.</summary>
    private RequestResponseLog[] Gateway() =>
    [
        .. Call("Caller", "bff", "http://bff/orders", AttributionSource.TestContext, null, null,
            Call("bff", "orders-api", "http://orders/orders", AttributionSource.RequestHeader, null, null,
                Call("orders-api", "Payments", "http://payments/charges", AttributionSource.RequestHeader)))
    ];

    [Fact]
    public void Origin_is_the_callers_role_from_the_parties_block()
    {
        // The block names the service behind the gateway as the subject, which the inference would not.
        var report = Write(Gateway(), new PartyDeclarations("orders",
            [new PartyDescriptor("Caller") { Kind = PartyKind.Test }, new PartyDescriptor("orders-api") { Kind = PartyKind.UnderTest }], [], []));

        var origin = ByEdge(report, "origin");

        Assert.Equal("test", origin["Caller > bff"]);
        Assert.Equal("other", origin["bff > orders-api"]);
        Assert.Equal("subject", origin["orders-api > Payments"]);
    }

    [Fact]
    public void Origin_uses_the_inferred_roles_when_nothing_is_declared()
    {
        // #190's resolver: the test is the caller nobody calls, on the test's context; the subject its callee that calls on.
        var origin = ByEdge(Write(Gateway()), "origin");

        Assert.Equal("test", origin["Caller > bff"]);
        Assert.Equal("subject", origin["bff > orders-api"]);
        Assert.Equal("other", origin["orders-api > Payments"]);
    }

    [Fact]
    public void Brokers_deliveries_read_other()
    {
        // A broker only calls, so "a name that only calls" is no test client: its deliveries are neither the test's nor the subject's.
        var origin = ByEdge(Write(Breakfast()), "origin");

        Assert.Equal("other", origin["Kafka Broker > orders-api"]);
        Assert.Equal("test", origin["Caller > orders-api"]);
        Assert.Equal("subject", origin["orders-api > Payments"]);
    }

    [Fact]
    public void A_declared_in_process_subject_reads_subject()
    {
        // The test calls handler code in its own process, and the handler's calls carry the handler's name.
        RequestResponseLog[] logs = [.. Call("orders-handler", "OrdersDb", "http://orders-db/orders", AttributionSource.TestContext)];

        var origin = ByEdge(Write(logs, new PartyDeclarations(null, [new PartyDescriptor("orders-handler") { Kind = PartyKind.UnderTest }], [], [])), "origin");

        Assert.Equal("subject", origin["orders-handler > OrdersDb"]);
    }

    [Fact]
    public void An_undeclared_in_process_subject_reads_test()
    {
        // The limit of inference, pinned: a subject in the test's process, which nobody calls, reads as the test until it is
        // declared (plan 4.3, section 7).
        RequestResponseLog[] logs = [.. Call("orders-handler", "OrdersDb", "http://orders-db/orders", AttributionSource.TestContext)];

        Assert.Equal("test", ByEdge(Write(logs), "origin")["orders-handler > OrdersDb"]);
    }

    [Fact]
    public void A_seed_through_the_subjects_client_reads_subject()
    {
        // The limit, pinned so a change to it is a decision: a client the subject registers carries the subject's name
        // whoever uses it, so a test seeding through it writes calls under the subject's name (plan 4.3, item 1).
        RequestResponseLog[] logs =
        [
            .. Call("orders-api", "OrdersDb", "http://orders-db/seed", AttributionSource.TestContext),
            .. Call("Caller", "orders-api", "http://orders/orders", AttributionSource.TestContext)
        ];

        var report = Write(logs);

        Assert.Equal("subject", ByEdge(report, "origin")["orders-api > OrdersDb"]);
        // And on the test's context, outside any request the subject received, it reads as the subject's background work.
        Assert.Equal("background", ByEdge(report, "cause")["orders-api > OrdersDb"]);
    }

    [Fact]
    public void Cause_reads_the_callers_role_then_the_attribution_then_the_nesting()
    {
        // kronikol-ed's rule (#191): the test's own calls are test; a call attributed through a request header is the
        // request's; any other attribution is background; a call with none is the request's when it ran inside one its
        // caller received, and unknown at the top level.
        RequestResponseLog[] logs =
        [
            .. Call("Caller", "orders-api", "http://orders/orders", AttributionSource.TestContext, null, null,
                Call("orders-api", "Payments", "http://payments/charges", AttributionSource.RequestHeader),
                Call("orders-api", "Audit", "http://audit/events"),
                Call("orders-api", "Cache", "http://cache/orders", AttributionSource.Scope)),
            .. Call("orders-api", "Metrics", "http://metrics/flush")
        ];

        var cause = ByEdge(Write(logs), "cause");

        Assert.Equal("test", cause["Caller > orders-api"]);
        Assert.Equal("request", cause["orders-api > Payments"]);
        Assert.Equal("request", cause["orders-api > Audit"]);
        Assert.Equal("background", cause["orders-api > Cache"]);
        Assert.Equal("unknown", cause["orders-api > Metrics"]);
    }

    [Fact]
    public void A_call_on_the_tests_context_reads_test_when_no_party_is_the_test()
    {
        // With no test party to tell the test's calls from the subject's, the test's context is the test's.
        RequestResponseLog[] logs = [.. Call("orders-handler", "OrdersDb", "http://orders-db/orders", AttributionSource.TestContext)];

        var report = Write(logs, new PartyDeclarations(null, [new PartyDescriptor("orders-handler") { Kind = PartyKind.UnderTest }], [], []));

        Assert.Equal("test", ByEdge(report, "cause")["orders-handler > OrdersDb"]);
    }

    [Fact]
    public void An_unattributed_delivery_inside_the_publish_it_came_from_reads_request()
    {
        // An ingest or a tap records no attribution, and a broker's delivery sits inside the call that published it by its
        // trace (CallNesting): the broker received no call, yet the delivery ran inside one.
        var trace = Guid.NewGuid();
        RequestResponseLog[] logs =
        [
            .. Call("Caller", "orders-api", "http://orders/orders", null, null, trace,
                Call("Kafka Broker", "orders-worker", "kafka://orders-topic/orders", null, "Kafka", trace))
        ];

        Assert.Equal("request", ByEdge(Write(logs), "cause")["Kafka Broker > orders-worker"]);
    }

    [Fact]
    public void Only_a_command_that_reads_a_role_resolves_the_party_table_and_once()
    {
        var report = Write(Breakfast());

        int TableReads(params string[] args)
        {
            var index = ReportScanner.Scan(report);
            var error = new StringWriter();
            var options = QueryOptions.Parse([index.Path, .. args], error)!;
            var writer = new QueryWriter(new StringWriter(), options.MaxBytes, options.Json ? new QueryEnvelope("interactions", null, null, []) : null);
            Assert.Equal(0, QueryCommand.Interactions(index, options, writer, error));
            return index.PartyTableReads;
        }

        Assert.Equal(0, TableReads());
        Assert.Equal(0, TableReads("--group-by", "service,caller"));
        // Three rows, one table.
        Assert.Equal(1, TableReads("--json"));
        Assert.Equal(1, TableReads("--group-by", "origin,cause"));
    }

    [Fact]
    public void Interactions_json_rows_carry_the_origin_and_the_cause_after_the_caller()
    {
        var (output, error, exit) = Run("interactions", Write(Breakfast()), "--json");

        Assert.True(exit == 0, error);
        var payments = JsonDocument.Parse(output).RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("service").GetString() == "Payments");
        var names = payments.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(("subject", "request"), (payments.GetProperty("origin").GetString(), payments.GetProperty("cause").GetString()));
        Assert.Equal(names.IndexOf("caller") + 1, names.IndexOf("origin"));
        Assert.Equal(names.IndexOf("origin") + 1, names.IndexOf("cause"));
    }
}
