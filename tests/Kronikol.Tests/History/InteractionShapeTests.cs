using System.Net;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.History;

/// <summary>
/// The interaction fingerprint (plans/CROSS_RUN_HISTORY_PLAN.md §2.4, §5.6): what a scenario did,
/// reduced to the calls it made with their variable parts templated away, so the same behaviour on two
/// runs hashes the same and a new call, a dropped call or a changed status does not. Measured on the
/// real corpus: 68 percent of scenarios have interactions at all, and of those the raw URI was stable
/// across two runs in fewer than half - ids and timestamps in paths are why the templater exists.
/// </summary>
public class InteractionShapeTests
{
    [Theory]
    [InlineData("/orders/550e8400-e29b-41d4-a716-446655440000", "/orders/{id}")]
    [InlineData("/orders/550E8400E29B41D4A716446655440000/lines", "/orders/{id}/lines")]
    [InlineData("/sessions/a1b2c3d4e5f60718", "/sessions/{id}")]
    [InlineData("/events/01ARZ3NDEKTSV4RRFFQ69G5FAV", "/events/{id}")]
    [InlineData("/orders/123", "/orders/{n}")]
    [InlineData("/orders/123/items/4567", "/orders/{n}/items/{n}")]
    [InlineData("/v2/orders", "/v2/orders")]
    [InlineData("/reports/2026-09-12", "/reports/{ts}")]
    [InlineData("/snapshots/2026-09-12T10:04:11Z", "/snapshots/{ts}")]
    [InlineData("/snapshots/2026-09-12T10:04:11.123+01:00", "/snapshots/{ts}")]
    [InlineData("/customers/cust-4711/orders", "/customers/cust-{n}/orders")]
    [InlineData("/customers/abc123", "/customers/abc123")]
    [InlineData("/orders?page=3&sort=desc", "/orders?page&sort")]
    [InlineData("/orders?sort=desc&page=3", "/orders?page&sort")]
    [InlineData("/orders?id=550e8400-e29b-41d4-a716-446655440000", "/orders?id")]
    [InlineData("/prefix_550e8400-e29b-41d4-a716-446655440000_suffix", "/prefix_{id}_suffix")]
    public void Variable_path_parts_are_templated(string path, string expected)
    {
        Assert.Equal(expected, InteractionShape.Template(path));
    }

    [Fact]
    public void There_is_no_base64_rule_because_it_templated_real_words()
    {
        // §5.6, reversed by measurement: a base64 detector matched ordinary path words like "customers"
        // and "download", templating the very segments that distinguish two endpoints. A payload id in a
        // path is rare enough to live with; a rule that erases route names is not.
        Assert.Equal("/files/SGVsbG8gV29ybGQ", InteractionShape.Template("/files/SGVsbG8gV29ybGQ"));
        Assert.Equal("/customers/download", InteractionShape.Template("/customers/download"));
    }

    private static RequestResponseLog Request(string testId, string method, string uri, string service, string caller, Guid pair, string? category = null, string? content = null) =>
        new("Scenario", testId, new HttpMethod(method), content, new Uri("http://" + service + uri), [], service, caller,
            RequestResponseType.Request, Guid.NewGuid(), pair, TrackingIgnore: false, DependencyCategory: category);

    private static RequestResponseLog Response(string testId, string service, string caller, Guid pair, HttpStatusCode status) =>
        new("Scenario", testId, HttpMethod.Get, null, new Uri("http://" + service + "/"), [], service, caller,
            RequestResponseType.Response, Guid.NewGuid(), pair, TrackingIgnore: false, StatusCode: status);

    private static RequestResponseLog[] Pair(string testId, string method, string uri, HttpStatusCode status, string service = "orders", string caller = "Test")
    {
        var id = Guid.NewGuid();
        return [Request(testId, method, uri, service, caller, id), Response(testId, service, caller, id, status)];
    }

    [Fact]
    public void Calls_pair_a_request_with_its_response_by_id_and_keep_request_order()
    {
        var logs = new List<RequestResponseLog>();
        logs.AddRange(Pair("t1", "POST", "/orders", HttpStatusCode.Created));
        logs.AddRange(Pair("t1", "GET", "/orders/123", HttpStatusCode.OK));

        var calls = InteractionShape.Calls(logs);

        Assert.Equal(2, calls.Count);
        Assert.Equal(new ShapeCall("Test", "orders", "POST", "/orders", "201"), calls[0]);
        Assert.Equal(new ShapeCall("Test", "orders", "GET", "/orders/{n}", "200"), calls[1]);
    }

    [Fact]
    public void A_request_without_a_response_is_a_call_with_no_status()
    {
        var calls = InteractionShape.Calls([Request("t1", "GET", "/health", "orders", "Test", Guid.NewGuid())]);

        Assert.Equal(new ShapeCall("Test", "orders", "GET", "/health", null), Assert.Single(calls));
    }

    [Fact]
    public void Diagram_markers_and_ignored_records_are_not_calls()
    {
        var marker = Request("t1", "GET", "/", "orders", "Test", Guid.NewGuid()) with { IsOverrideStart = true };
        var ignored = Request("t1", "GET", "/", "orders", "Test", Guid.NewGuid()) with { TrackingIgnore = true };

        Assert.Empty(InteractionShape.Calls([marker, ignored]));
    }

    [Fact]
    public void A_statement_shaped_call_keeps_the_templated_statement_head()
    {
        // Every SQL call of a scenario shares one URI - the connection - so without the statement the
        // fingerprint could not tell "SELECT customers" from "DELETE customers". The head is templated
        // like a path, so a literal id in a WHERE clause does not make every run different.
        var call = Request("t1", "QUERY", "/", "db", "Api", Guid.NewGuid(), category: "database",
            content: "SELECT * FROM orders WHERE id = 4711\nORDER BY created");

        var shaped = Assert.Single(InteractionShape.Calls([call]));

        Assert.Contains("SELECT * FROM orders WHERE id = {n}", shaped.Uri);
        Assert.DoesNotContain("ORDER BY", shaped.Uri);
    }

    [Fact]
    public void The_set_fingerprint_ignores_order_and_the_ordered_one_does_not()
    {
        var a = new[] { new ShapeCall("Test", "orders", "POST", "/orders", "201"), new ShapeCall("Test", "orders", "GET", "/orders/{n}", "200") };
        var b = new[] { a[1], a[0] };

        var (setA, orderedA, callsA) = InteractionShape.Fingerprint(a);
        var (setB, orderedB, _) = InteractionShape.Fingerprint(b);

        Assert.Equal(setA, setB);
        Assert.NotEqual(orderedA, orderedB);
        Assert.Equal(2, callsA);
        Assert.Matches("^[0-9a-f]{8}$", setA);
    }

    [Fact]
    public void A_changed_status_a_new_call_or_a_dropped_call_changes_the_set_fingerprint()
    {
        var baseline = new[] { new ShapeCall("Test", "orders", "GET", "/orders/{n}", "200") };
        var status = new[] { new ShapeCall("Test", "orders", "GET", "/orders/{n}", "500") };
        var extra = new[] { baseline[0], new ShapeCall("Test", "audit", "POST", "/events", "202") };

        Assert.NotEqual(InteractionShape.Fingerprint(baseline).ShapeSet, InteractionShape.Fingerprint(status).ShapeSet);
        Assert.NotEqual(InteractionShape.Fingerprint(baseline).ShapeSet, InteractionShape.Fingerprint(extra).ShapeSet);
        Assert.NotEqual(InteractionShape.Fingerprint(extra).ShapeSet, InteractionShape.Fingerprint([]).ShapeSet);
    }

    [Fact]
    public void The_same_calls_with_different_ids_fingerprint_the_same()
    {
        var run1 = InteractionShape.Calls(Pair("t1", "GET", "/orders/550e8400-e29b-41d4-a716-446655440000", HttpStatusCode.OK));
        var run2 = InteractionShape.Calls(Pair("t1", "GET", "/orders/6ba7b810-9dad-11d1-80b4-00c04fd430c8", HttpStatusCode.OK));

        Assert.Equal(InteractionShape.Fingerprint(run1).ShapeSet, InteractionShape.Fingerprint(run2).ShapeSet);
    }

    [Fact]
    public void Dependencies_are_the_distinct_sorted_caller_service_pairs()
    {
        var logs = new List<RequestResponseLog>();
        logs.AddRange(Pair("t1", "GET", "/a", HttpStatusCode.OK, service: "orders", caller: "Test"));
        logs.AddRange(Pair("t1", "GET", "/b", HttpStatusCode.OK, service: "audit", caller: "orders"));
        logs.AddRange(Pair("t2", "GET", "/c", HttpStatusCode.OK, service: "orders", caller: "Test"));

        Assert.Equal(["Test>orders", "orders>audit"], InteractionShape.Dependencies(logs));
    }
}
