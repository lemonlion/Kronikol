using Kronikol.CallGraph;
using Kronikol.Parties;

namespace Kronikol.Tests.CallGraph;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.5: the parties a card reads. A report's block is taken as written, a name it does not
/// hold reading as unknown; without one, the inference over every call, the root background block's included. The
/// subject row is the <c>underTest</c> party whose id is the run's <c>serviceId</c>, else the one that received the most
/// calls, then the first by name, also when the inference keeps two.
/// </summary>
public class GraphPartiesTests
{
    private static readonly CallScenario S0 = Records.Scenario(0, "Order pancakes");

    private static Graph Build(Records records, PartiesInput parties) => CallGraphBuilder.Build([S0], records.All, parties);

    private static Party UnderTest(string name, string? id = null) => new(name, id, PartyKind.UnderTest, PartyKindSource.Declared, null);

    [Fact]
    public void A_block_is_taken_as_written_and_a_name_it_does_not_hold_is_unknown()
    {
        var r = new Records();
        r.Call(S0, "Caller", "Orders", "GET", "http://localhost/orders/1", attribution: "TestContext");
        r.Call(S0, "Orders", "Ledger", "POST", "http://localhost:5002/entries", attribution: "RequestHeader");

        var graph = Build(r, new PartiesInput([UnderTest("Orders", "orders"), new Party("Caller", null, PartyKind.Test, PartyKindSource.Observed, null)], "orders"));

        Assert.Equal([("Caller", PartyKind.Test, (PartyKindSource?)PartyKindSource.Observed), ("Ledger", PartyKind.Unknown, null), ("Orders", PartyKind.UnderTest, PartyKindSource.Declared)],
            graph.Parties.Select(p => (p.Name, p.Kind, p.KindSource)));
        Assert.Null(graph.Rules.Inference);
        Assert.Equal("Orders", graph.Subject?.Name);
    }

    [Fact]
    public void Without_a_block_the_inference_reads_every_call_the_root_background_blocks_included()
    {
        var r = new Records();
        r.Call(S0, "Caller", "Orders", "GET", "http://localhost/orders/1", attribution: "TestContext");
        r.Call(null, "Scheduler", "Orders", "POST", "http://localhost/orders/expire", attribution: "Detached");

        var graph = Build(r, PartiesInput.Inferred);

        Assert.Contains(graph.Parties, p => p.Name == "Scheduler");
        Assert.Equal(PartyResolver.InferenceVersion, graph.Rules.Inference);
        Assert.Equal("Orders", graph.Subject?.Name);
    }

    // The inference keeps a second subject that makes calls and takes a quarter of the tests' calls: Orders takes three,
    // Billing one, and Billing is first by name.
    [Fact]
    public void Of_two_parties_under_test_the_subject_is_the_one_that_received_the_most_calls()
    {
        var r = new Records();
        foreach (var id in new[] { 1, 2, 3 })
            r.Call(S0, "Caller", "Orders", "GET", $"http://localhost/orders/{id}", attribution: "TestContext");
        r.Call(S0, "Caller", "Billing", "GET", "http://localhost:5003/invoices/1", attribution: "TestContext");
        r.Call(S0, "Orders", "Db", "SELECT", "sql://localhost/orders", attribution: "RequestHeader", category: "SQL");
        r.Call(S0, "Billing", "Db", "SELECT", "sql://localhost/invoices", attribution: "RequestHeader", category: "SQL");

        var graph = Build(r, PartiesInput.Inferred);

        Assert.Equal(["Billing", "Orders"], graph.Parties.Where(p => p.Kind == PartyKind.UnderTest).Select(p => p.Name));
        Assert.Equal("Orders", graph.Subject?.Name);
    }

    [Fact]
    public void Of_two_parties_under_test_that_received_as_many_calls_the_subject_is_the_first_by_name()
    {
        var r = new Records();
        r.Call(S0, "Caller", "Orders", "GET", "http://localhost/orders/1", attribution: "TestContext");
        r.Call(S0, "Caller", "Billing", "GET", "http://localhost:5003/invoices/1", attribution: "TestContext");

        var graph = Build(r, new PartiesInput([UnderTest("Orders"), UnderTest("Billing")], null));

        Assert.Equal("Billing", graph.Subject?.Name);
    }

    [Fact]
    public void The_party_the_service_id_names_is_the_subject_whatever_it_received()
    {
        var r = new Records();
        foreach (var id in new[] { 1, 2, 3 })
            r.Call(S0, "Caller", "Orders", "GET", $"http://localhost/orders/{id}", attribution: "TestContext");
        r.Call(S0, "Caller", "Billing", "GET", "http://localhost:5003/invoices/1", attribution: "TestContext");

        var graph = Build(r, new PartiesInput([UnderTest("Orders", "orders"), UnderTest("Billing", "billing")], "billing"));

        Assert.Equal("Billing", graph.Subject?.Name);
    }

    // #192 R3's PartyTable reads the whole block, so the card and the query engine name one subject: a declared subject
    // that no call reaches is still the subject, and the card lists it.
    [Fact]
    public void A_declared_subject_no_call_reaches_is_still_the_subject_and_listed()
    {
        var r = new Records();
        r.Call(S0, "Caller", "Billing", "GET", "http://localhost:5003/invoices/1", attribution: "TestContext");

        var graph = Build(r, new PartiesInput([UnderTest("Orders", "orders"), new Party("Caller", null, PartyKind.Test, PartyKindSource.Observed, null)], "orders"));

        Assert.Equal("Orders", graph.Subject?.Name);
        Assert.Equal(["Billing", "Caller", "Orders"], graph.Parties.Select(p => p.Name));
        Assert.Empty(graph.Exposes);
    }

    // The same table answers cause: a declared test party that made no call is still the test, so an in-process
    // subject's call on the test's context reads by its attribution, not as the test's.
    [Fact]
    public void A_declared_test_party_that_made_no_call_is_still_the_test_for_cause()
    {
        var r = new Records();
        r.Call(S0, "Orders", "Orders Db", "INSERT", "sql://localhost/orders", attribution: "TestContext", category: "SQL", content: "INSERT INTO orders (id) VALUES (1)");

        var graph = Build(r, new PartiesInput([UnderTest("Orders", "orders"), new Party("Tester", null, PartyKind.Test, PartyKindSource.Declared, null)], "orders"));

        Assert.Equal(("subject", "background"), (graph.Calls.Single().Origin, graph.Calls.Single().Cause));
    }
}
