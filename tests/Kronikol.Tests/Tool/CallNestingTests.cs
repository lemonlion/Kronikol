using Kronikol.Tool.Query;

namespace Kronikol.Tests.Tool;

/// <summary>
/// Which call each call ran inside, rule R4 of plans/FLOW_NESTING_PLAN.md (§4.1): the parent of a request is
/// the innermost request still waiting for its answer whose service made it, or, when another party made it,
/// whose Kronikol trace it carries. A request never answered, or with no <c>requestResponseId</c>, is never
/// a parent, since nobody knows when it ended.
///
/// <para>Each fact is a shape measured on real reports (plan §3): the plain "innermost open call" rule turned a
/// service's parallel fan-out into a staircase, hung the test's next call under a request never answered, and
/// put the test's call under a background query from outside the test.</para>
/// </summary>
public class CallNestingTests
{
    [Fact]
    public void A_call_made_by_the_service_handling_an_open_call_is_inside_it()
    {
        var parents = Parents(
            Request("a", "test", "api"),
            Request("b", "api", "db"),
            Response("b"),
            Response("a"));

        Assert.Equal([null, 0, null, null], parents);
    }

    [Fact]
    public void Calls_a_service_makes_at_once_are_siblings_whatever_order_they_answer_in()
    {
        // The innermost open call is a sibling each time: the plain rule nests b, c and d at depths 1 to 3.
        var parents = Parents(
            Request("a", "test", "api"),
            Request("b", "api", "kitchen"),
            Request("c", "api", "supplier"),
            Request("d", "api", "cows"),
            Response("d"),
            Response("b"),
            Request("e", "api", "goats"),
            Response("e"),
            Response("c"),
            Response("a"));

        Assert.Equal([null, 0, 0, 0, null, null, 0, null, null, null], parents);
    }

    [Fact]
    public void A_message_delivered_on_an_open_calls_trace_is_inside_it()
    {
        // A delivery is recorded broker → consumer, so no open call has the broker as its service; it arrives
        // on the trace of the call that published it. The database call on a fresh trace is still open and
        // is not what it ran inside.
        var parents = Parents(
            Request("a", "test", "api", trace: "t1"),
            Request("b", "api", "db", trace: "t2"),
            Request("c", "broker", "api", trace: "t1"),
            Response("c"),
            Response("b"),
            Response("a"));

        Assert.Equal([null, 0, 0, null, null, null], parents);
    }

    [Fact]
    public void Two_calls_from_one_caller_are_never_parent_and_child()
    {
        // The same trace, so only the rule that one party's calls are siblings keeps b out of a.
        var parents = Parents(
            Request("a", "test", "api", trace: "t1"),
            Request("b", "test", "search", trace: "t1"),
            Response("b"),
            Response("a"));

        Assert.Equal([null, null, null, null], parents);
    }

    [Fact]
    public void A_request_never_answered_is_never_a_parent()
    {
        // Open for ever, it would hold every later call its service made, and the test's next call with it.
        var parents = Parents(
            Request("a", "test", "api"),
            Request("b", "api", "db"),
            Response("b"),
            Request("c", "test", "api"),
            Response("c"));

        Assert.Equal([null, null, null, null, null], parents);
    }

    [Fact]
    public void A_request_without_a_requestResponseId_is_never_a_parent()
    {
        var parents = Parents(
            Request(null, "test", "api"),
            Request("b", "api", "db"),
            Response("b"),
            Response(null, "test", "api"));

        Assert.Equal([null, null, null, null], parents);
    }

    [Fact]
    public void A_call_from_another_caller_on_another_trace_stays_top_level()
    {
        // A query the service started before the step, from outside the test, is still open when the test
        // makes its call.
        var parents = Parents(
            Request("a", "api", "cosmos", trace: "t1"),
            Request("b", "test", "api", trace: "t2"),
            Response("b"),
            Response("a"));

        Assert.Equal([null, null, null, null], parents);
    }

    [Fact]
    public void The_innermost_qualifying_call_is_the_parent()
    {
        // api is the service of two open calls, the test's and auth's callback; the database call was made
        // while handling the callback.
        var parents = Parents(
            Request("a", "test", "api"),
            Request("b", "api", "auth"),
            Request("c", "auth", "api"),
            Request("d", "api", "db"),
            Response("d"),
            Response("c"),
            Response("b"),
            Response("a"));

        Assert.Equal([null, 0, 1, 2, null, null, null, null], parents);
    }

    [Fact]
    public void Nesting_goes_as_deep_as_the_calls_do()
    {
        // No real report nests past one level, so depths two and three are held here.
        var parents = Parents(
            Request("a", "test", "gateway"),
            Request("b", "gateway", "orders"),
            Request("c", "orders", "payments"),
            Request("d", "payments", "bank"),
            Response("d"),
            Response("c"),
            Response("b"),
            Response("a"));

        Assert.Equal([null, 0, 1, 2, null, null, null, null], parents);
    }

    [Fact]
    public void A_response_closes_its_request_wherever_it_sits()
    {
        // b is answered while c, opened after it, is still waiting: b is closed, so what kitchen makes next
        // ran inside nothing, and what supplier makes ran inside c.
        var parents = Parents(
            Request("a", "test", "api"),
            Request("b", "api", "kitchen"),
            Request("c", "api", "supplier"),
            Response("b"),
            Request("d", "kitchen", "oven"),
            Response("d"),
            Request("e", "supplier", "warehouse"),
            Response("e"),
            Response("c"),
            Response("a"));

        Assert.Equal([null, 0, 0, null, null, null, 2, null, null, null], parents);
    }

    [Fact]
    public void A_request_answered_before_it_was_recorded_is_never_a_parent()
    {
        // Its response is behind it, so it was never waiting while a later call was made; left open, it
        // would hold every later call its service made.
        var parents = Parents(
            Response("a", "test", "api"),
            Request("a", "test", "api"),
            Request("b", "api", "db"),
            Response("b"));

        Assert.Equal([null, null, null, null], parents);
    }

    // ─── Harness ───────────────────────────────────────────────

    private sealed record Record(string Type, string? Id, string? Caller, string? Service, string? Trace);

    private static Record Request(string? id, string caller, string service, string? trace = null) =>
        new("Request", id, caller, service, trace ?? "trace-" + (id ?? Guid.NewGuid().ToString("N")));

    /// <summary>A response repeats its request's caller and service, as the report writes it.</summary>
    private static Record Response(string? id, string? caller = null, string? service = null) =>
        new("Response", id, caller, service, null);

    private static int?[] Parents(params Record[] records)
    {
        var entries = new List<InteractionEntry>();
        foreach (var record in records)
        {
            var request = records.FirstOrDefault(r => r.Type == "Request" && r.Id is not null && r.Id == record.Id);
            entries.Add(new InteractionEntry
            {
                Ordinal = entries.Count,
                Type = record.Type,
                RequestResponseId = record.Id,
                CallerName = record.Caller ?? request?.Caller ?? "",
                ServiceName = record.Service ?? request?.Service ?? "",
                TraceId = record.Trace ?? request?.Trace
            });
        }

        return CallNesting.Parents(entries);
    }
}
