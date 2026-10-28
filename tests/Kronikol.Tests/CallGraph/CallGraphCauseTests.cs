using Kronikol.CallGraph;
using Kronikol.Parties;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.1: <c>origin</c>, <c>cause</c> and the effective phase on each shape that matters, from
/// hand-written records, one shape a fact. <c>cause</c> reads how the call got its scenario first and the nesting only
/// where a producer records nothing (4.2, steps 1 to 5); a background call keeps the phase it recorded.
/// </summary>
public class CallGraphCauseTests
{
    private static readonly CallScenario S0 = Records.Scenario(0, "Order pancakes");

    private static GraphCall Of(Graph graph, CallRecord request) =>
        graph.Calls.Single(c => c.Request.RequestResponseId == request.RequestResponseId);

    private static Graph Build(Records records, PartiesInput? parties = null) =>
        CallGraphBuilder.Build([S0], records.All, parties ?? PartiesInput.Inferred);

    [Fact]
    public void A_call_the_subject_makes_inside_the_tests_request_is_request_with_the_requests_phase()
    {
        var records = new Records();
        var order = records.Request(S0, "Caller", "Breakfast Provider", "POST", "http://localhost/orders", attribution: "TestContext", phase: "Action");
        var milk = records.Call(S0, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/milk", attribution: "RequestHeader");
        records.Answer(order);

        var graph = Build(records);

        Assert.Equal(("test", "test", "action", false), (Of(graph, order).Origin, Of(graph, order).Cause, Of(graph, order).EffectivePhase, Of(graph, order).PhaseDerived));
        var inner = Of(graph, milk);
        Assert.Equal(graph.Calls.ToList().IndexOf(Of(graph, order)), inner.Parent);
        Assert.Equal(("subject", "request", "action", true), (inner.Origin, inner.Cause, inner.EffectivePhase, inner.PhaseDerived));
    }

    [Fact]
    public void The_test_seeding_a_database_with_its_own_client_is_test()
    {
        var records = new Records();
        var seed = records.Call(S0, "Caller", "Orders Db", "INSERT", "sql://localhost/orders", attribution: "TestContext", phase: "Setup", category: "SQL");
        records.Call(S0, "Caller", "Breakfast Provider", "GET", "http://localhost/orders", attribution: "TestContext", phase: "Action");

        var call = Of(Build(records), seed);

        Assert.Equal(("test", "test", "setup"), (call.Origin, call.Cause, call.EffectivePhase));
    }

    // The documented limit (4.2, #192 4.3): seeding through the service's own client is recorded as the service's call in the
    // test's context, outside any request, which is also what the service's own background work looks like.
    [Fact]
    public void The_test_seeding_through_the_subjects_own_client_reads_origin_subject_and_cause_background()
    {
        var records = new Records();
        var seed = records.Call(S0, "Breakfast Provider", "Orders Db", "INSERT", "sql://localhost/orders", attribution: "TestContext", phase: "Setup", category: "SQL");
        records.Call(S0, "Caller", "Breakfast Provider", "GET", "http://localhost/orders", attribution: "TestContext", phase: "Action");

        var call = Of(Build(records), seed);

        Assert.Equal(("subject", "background", "setup"), (call.Origin, call.Cause, call.EffectivePhase));
    }

    [Fact]
    public void The_subjects_background_job_is_background_with_the_phase_it_recorded()
    {
        var records = new Records();
        records.Call(S0, "Caller", "Breakfast Provider", "GET", "http://localhost/orders", attribution: "TestContext", phase: "Action");
        var started = records.Call(S0, "Breakfast Provider", "Notifier", "POST", "http://localhost:5003/notify", attribution: "Scope", phase: "Setup");
        var unphased = records.Call(S0, "Breakfast Provider", "Notifier", "POST", "http://localhost:5003/notify", attribution: "Detached");

        var graph = Build(records);

        Assert.Equal(("subject", "background", "setup", false), (Of(graph, started).Origin, Of(graph, started).Cause, Of(graph, started).EffectivePhase, Of(graph, started).PhaseDerived));
        Assert.Equal(("background", "unknown"), (Of(graph, unphased).Cause, Of(graph, unphased).EffectivePhase));
    }

    // 2.4: one such call was nested under an unrelated request by recording order, took its phase, and moved the card.
    [Fact]
    public void A_background_call_recorded_while_an_unrelated_request_is_open_keeps_its_own_phase()
    {
        var records = new Records();
        var order = records.Request(S0, "Caller", "Breakfast Provider", "POST", "http://localhost/orders", attribution: "TestContext", phase: "Action");
        var outbox = records.Call(S0, "Breakfast Provider", "CosmosDB", "Replace", "https://localhost:8081/dbs/db/colls/outbox/docs/1", attribution: "DocumentOwner", category: "CosmosDB");
        records.Answer(order);

        var call = Of(Build(records), outbox);

        Assert.NotNull(call.Parent);
        Assert.Equal(("background", "unknown", false), (call.Cause, call.EffectivePhase, call.PhaseDerived));
    }

    [Fact]
    public void A_brokers_delivery_to_the_subject_carries_the_header_and_is_request()
    {
        var records = new Records();
        records.Call(S0, "Caller", "Breakfast Provider", "POST", "http://localhost/orders", attribution: "TestContext", phase: "Action");
        var delivery = records.Call(S0, "Kafka", "Breakfast Provider", "Consume ← orders", "kafka:///orders", attribution: "RequestHeader", category: "MessageQueue");

        var call = Of(Build(records), delivery);

        Assert.Null(call.Parent);
        Assert.Equal(("other", "request"), (call.Origin, call.Cause));
    }

    [Fact]
    public void A_call_of_the_root_background_block_is_background_and_in_no_scenario()
    {
        var records = new Records();
        records.Call(S0, "Caller", "Breakfast Provider", "POST", "http://localhost/orders", attribution: "TestContext", phase: "Action");
        var expired = records.Call(null, "Breakfast Provider", "CosmosDB", "Replace", "https://localhost:8081/dbs/db/colls/outbox/docs/1", attribution: "Expired", category: "CosmosDB");

        var graph = Build(records);

        Assert.Equal("background", Of(graph, expired).Cause);
        var op = Of(graph, expired).Op;
        Assert.DoesNotContain(graph.Scenarios, s => s.Ops.Contains(op) || s.AllOps.Contains(op));
        Assert.Null(graph.Operations.Single(o => o.Op == op).Example);
    }

    // Ingest, the taps and Kronikol4J record no attributionSource: the nesting alone says request or unknown, never background.
    [Fact]
    public void Without_attribution_a_call_inside_a_received_call_is_request_and_one_outside_is_unknown()
    {
        var records = new Records();
        var order = records.Request(S0, "Test", "Breakfast Provider", "POST", "http://localhost/orders");
        var inside = records.Call(S0, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/milk");
        records.Answer(order);
        var outside = records.Call(S0, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/cream");

        var graph = Build(records);

        Assert.Equal("test", Of(graph, order).Cause);
        Assert.Equal("request", Of(graph, inside).Cause);
        Assert.Equal("unknown", Of(graph, outside).Cause);
    }

    // #190 §5.2: an in-process subject is the test and the service at once; its calls in the test's context are the test's.
    [Fact]
    public void An_in_process_subject_declared_under_test_with_no_test_party_makes_test_calls_that_its_scenario_lists()
    {
        var records = new Records();
        var call = records.Call(S0, "Breakfast Provider", "Cow Service", "GET", "http://localhost:5001/milk", attribution: "TestContext", phase: "Action");
        // the service's own outbox, flushed outside any request while the scenario ran
        var flush = records.Call(S0, "Breakfast Provider", "Outbox", "POST", "http://localhost:5002/outbox", attribution: "Detached", phase: "Action");
        var block = new PartiesInput(
        [
            new Party("Breakfast Provider", "breakfast-provider", PartyKind.UnderTest, PartyKindSource.Declared, null),
            new Party("Cow Service", null, PartyKind.Unknown, null, null)
        ], "breakfast-provider");

        var graph = Build(records, block);

        var read = Of(graph, call);
        Assert.Equal(("subject", "test"), (read.Origin, read.Cause));
        Assert.Contains(read.Op, graph.Scenarios.Single().Ops);
        var flushed = Of(graph, flush);
        Assert.Equal(("subject", "background"), (flushed.Origin, flushed.Cause));
        Assert.DoesNotContain(flushed.Op, graph.Scenarios.Single().Ops);
        Assert.DoesNotContain(flushed.Op, graph.Scenarios.Single().AllOps);
    }
}
