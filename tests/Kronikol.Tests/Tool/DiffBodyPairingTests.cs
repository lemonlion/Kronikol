using System.Net;
using System.Text.Json;
using Kronikol.Query;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tool;

/// <summary>
/// <c>diff &lt;old&gt; &lt;new&gt; --body ADDR</c> pairs the call by what it is, not by where it sits (#115).
///
/// <para>It found the scenario in the new run by stableId and then took the interaction with the same
/// ordinal, so where a service makes its calls concurrently, and their order changes from run to run, it
/// diffed two different calls and exited 0. Measured on two runs of one BreakfastProvider commit, 13 of
/// 1,518 addresses, a health check among them whose responses from two services read byte-identical. The
/// call is now the one with the same service, method and path and query, the n-th of several for the
/// n-th; when no call has that URI, the one whose URI differs only in what looks like an id; a response
/// through the request it answers; and a refusal, exit 2, when nothing pairs.</para>
///
/// <para>The fixtures are written through the real writer with every call given its own pairing id, as a
/// run gives them, so the scanner pairs the halves exactly as it does on a real report.</para>
/// </summary>
public class DiffBodyPairingTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("kronikol-pairing").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // ─── The call, by what it is ───────────────────────────────

    [Fact]
    public void A_request_is_paired_with_the_same_call_when_the_calls_swapped_places()
    {
        var oldA = new Call("Redis", "Set", "redis://cache:6379/a", "{\"key\":\"a\",\"v\":1}");
        var oldB = new Call("Redis", "Set", "redis://cache:6379/b", "{\"key\":\"b\",\"v\":1}");
        var newA = new Call("Redis", "Set", "redis://cache:6379/a", "{\"key\":\"a\",\"v\":2}");
        var newB = new Call("Redis", "Set", "redis://cache:6379/b", "{\"key\":\"b\",\"v\":1}");
        var old = Write("Old.json", Rq(oldA), Rs(oldA), Rq(oldB), Rs(oldB));
        var @new = Write("New.json", Rq(newB), Rs(newB), Rq(newA), Rs(newA));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i0");

        Assert.True(exit == 0, error);
        // The label is the partner's own address, so a call that moved is visible as having moved.
        Assert.Contains("+ New.json s0/i2", output);
        Assert.Contains("$.v: 1 → 2", output);
        Assert.DoesNotContain("$.key", output);
    }

    [Fact]
    public void A_response_is_paired_with_the_response_of_the_same_call()
    {
        var oldA = new Call("api", "GET", "http://api/a", "{}", "{\"v\":1}");
        var oldB = new Call("api", "GET", "http://api/b", "{}", "{\"w\":1}");
        var newA = new Call("api", "GET", "http://api/a", "{}", "{\"v\":2}");
        var newB = new Call("api", "GET", "http://api/b", "{}", "{\"w\":1}");
        var old = Write("Old.json", Rq(oldA), Rs(oldA), Rq(oldB), Rs(oldB));
        var @new = Write("New.json", Rq(newB), Rs(newB), Rq(newA), Rs(newA));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("+ New.json s0/i3", output);
        Assert.Contains("$.v: 1 → 2", output);
        Assert.DoesNotContain("$.w", output);
    }

    [Fact]
    public void A_request_and_its_response_pair_with_the_two_halves_of_one_call_whatever_order_the_responses_came_back_in()
    {
        // Two calls that share a key, answered in the other order in the new run. Ranked among the
        // responses, the first response would pair with the first response - another call's.
        var old1 = new Call("api", "GET", "http://api/x", "{\"call\":1}", "{\"call\":1,\"v\":1}");
        var old2 = new Call("api", "GET", "http://api/x", "{\"call\":2}", "{\"call\":2,\"v\":1}");
        var new1 = new Call("api", "GET", "http://api/x", "{\"call\":1}", "{\"call\":1,\"v\":2}");
        var new2 = new Call("api", "GET", "http://api/x", "{\"call\":2}", "{\"call\":2,\"v\":1}");
        var old = Write("Old.json", Rq(old1), Rq(old2), Rs(old1), Rs(old2));
        var @new = Write("New.json", Rq(new1), Rq(new2), Rs(new2), Rs(new1));

        var request = Partner(old, @new, "s0/i0");
        var response = Partner(old, @new, "s0/i2");

        Assert.Equal("s0/i0", request);
        // s0/i3 is the response to s0/i0 in the new run: its second call answered first.
        Assert.Equal("s0/i3", response);
        var (output, _, _) = Diff(old, @new, "--body", "s0/i2");
        Assert.Contains("$.v: 1 → 2", output);
        Assert.DoesNotContain("$.call", output);
    }

    [Fact]
    public void A_request_is_never_paired_with_a_response_when_calls_sharing_a_key_interleave_differently()
    {
        // Sequential in the old run, concurrent in the new. Counted over both halves, the old run's second
        // request is the third entry with its key, and the new run's third entry is the first call's response.
        var oldA = new Call("api", "POST", "http://api/charge", "{\"n\":1}", "{\"r\":1}");
        var oldB = new Call("api", "POST", "http://api/charge", "{\"n\":2,\"v\":1}", "{\"r\":2}");
        var newA = new Call("api", "POST", "http://api/charge", "{\"n\":1}", "{\"r\":1}");
        var newB = new Call("api", "POST", "http://api/charge", "{\"n\":2,\"v\":2}", "{\"r\":2}");
        var old = Write("Old.json", Rq(oldA), Rs(oldA), Rq(oldB), Rs(oldB));
        var @new = Write("New.json", Rq(newA), Rq(newB), Rs(newA), Rs(newB));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i2");

        Assert.True(exit == 0, error);
        Assert.Contains("+ New.json s0/i1", output);
        Assert.Contains("$.v: 1 → 2", output);
        Assert.DoesNotContain("$.r", output);
    }

    [Fact]
    public void The_nth_of_several_calls_sharing_a_key_pairs_with_the_nth_and_says_so()
    {
        Call Charge(int n, int v) => new("api", "POST", "http://api/charge", $"{{\"n\":{n},\"v\":{v}}}");
        var other = new Call("api", "GET", "http://api/health", "{}");
        var (o1, o2, o3) = (Charge(1, 1), Charge(2, 1), Charge(3, 1));
        var (n1, n2, n3) = (Charge(1, 1), Charge(2, 2), Charge(3, 1));
        var otherAgain = new Call("api", "GET", "http://api/health", "{}");
        var old = Write("Old.json", Rq(o1), Rs(o1), Rq(o2), Rs(o2), Rq(o3), Rs(o3), Rq(other), Rs(other));
        var @new = Write("New.json", Rq(otherAgain), Rs(otherAgain), Rq(n1), Rs(n1), Rq(n2), Rs(n2), Rq(n3), Rs(n3));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i2");

        Assert.True(exit == 0, error);
        Assert.Contains("+ New.json s0/i4", output);
        Assert.Contains("$.v: 1 → 2", output);
        Assert.Contains("api POST /charge is made 3× in Old.json s0 and 3× in New.json s0; this is the 2nd, matched in order", output);
    }

    [Fact]
    public void A_call_the_new_run_did_not_make_is_refused_with_the_calls_it_did_make()
    {
        var oldCharts = new Call("Redis", "Set", "redis://cache:6379/app:charts-agg-1", "{\"size\":369}");
        var oldMetrics = new Call("Redis", "Set", "redis://cache:6379/app:metrics", "{\"m\":1}");
        var newCompetitor = new Call("Redis", "Set", "redis://cache:6379/app:competitor-charts-agg-1", "{\"size\":63}");
        var newMetrics = new Call("Redis", "Set", "redis://cache:6379/app:metrics", "{\"m\":1}");
        var old = Write("Old.json", Rq(oldCharts), Rs(oldCharts), Rq(oldMetrics), Rs(oldMetrics));
        var @new = Write("New.json", Rq(newCompetitor), Rs(newCompetitor), Rq(newMetrics), Rs(newMetrics));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i0");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("No call in New.json s0 matches Old.json s0/i0, Redis Set /app:charts-agg-1", error);
        Assert.Contains("s0/i0 /app:competitor-charts-agg-1", error);
        Assert.Contains("s0/i2 /app:metrics", error);
        Assert.Contains("--service Redis", error);
    }

    [Fact]
    public void A_call_the_new_run_made_fewer_times_is_refused_with_both_counts()
    {
        Call Query(int n) => new("CosmosDB", "QUERY", "https://cosmos:8081/orders", $"{{\"q\":{n}}}");
        var before = Enumerable.Range(1, 4).Select(Query).ToArray();
        var after = Enumerable.Range(1, 3).Select(Query).ToArray();
        var tail = new Call("api", "GET", "http://api/after", "{\"after\":true}");
        var old = Write("Old.json", [.. before.SelectMany(q => new[] { Rq(q), Rs(q) })]);
        // The new run's fourth pair of entries is another call, so the ordinal lands on something.
        var @new = Write("New.json", [.. after.SelectMany(q => new[] { Rq(q), Rs(q) }), Rq(tail), Rs(tail)]);

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i6");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("s0 makes 3 CosmosDB QUERY /orders calls; Old.json s0/i6 is the 4th of 4.", error);
    }

    [Fact]
    public void A_regenerated_id_in_the_URI_pairs_on_the_URI_shape_and_says_so()
    {
        var oldOrder = new Call("api", "GET", "http://api/orders/0b9a7d31-5c1e-4c47-9c2f-55b8c0e1d6a2", "{}", "{\"state\":\"open\"}");
        var oldHealth = new Call("api", "GET", "http://api/health", "{}", "{\"ok\":true}");
        var newOrder = new Call("api", "GET", "http://api/orders/f3c2b1a0-9d8e-4f7a-8b6c-5d4e3f2a1b0c", "{}", "{\"state\":\"paid\"}");
        var newHealth = new Call("api", "GET", "http://api/health", "{}", "{\"ok\":true}");
        var old = Write("Old.json", Rq(oldOrder), Rs(oldOrder), Rq(oldHealth), Rs(oldHealth));
        var @new = Write("New.json", Rq(newHealth), Rs(newHealth), Rq(newOrder), Rs(newOrder));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("+ New.json s0/i3", output);
        Assert.Contains("$.state: \"open\" → \"paid\"", output);
        Assert.Contains("matched on its shape /orders/{id} (the URIs differ in what looks like an id)", output);
    }

    [Fact]
    public void The_same_call_on_another_host_port_pairs_on_its_path_and_query_not_its_shape()
    {
        // A container's port is picked per run; the service name already says which dependency it is.
        var oldOrder = new Call("orders", "GET", "http://localhost:5001/orders/7?expand=lines", "{}", "{\"lines\":1}");
        var oldHealth = new Call("orders", "GET", "http://localhost:5001/health", "{}", "{\"ok\":true}");
        var newOrder = new Call("orders", "GET", "http://localhost:6002/orders/7?expand=lines", "{}", "{\"lines\":2}");
        var newHealth = new Call("orders", "GET", "http://localhost:6002/health", "{}", "{\"ok\":true}");
        var old = Write("Old.json", Rq(oldOrder), Rs(oldOrder), Rq(oldHealth), Rs(oldHealth));
        var @new = Write("New.json", Rq(newHealth), Rs(newHealth), Rq(newOrder), Rs(newOrder));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("+ New.json s0/i3", output);
        Assert.Contains("$.lines: 1 → 2", output);
        Assert.DoesNotContain("shape", output);
    }

    [Fact]
    public void A_shape_is_never_paired_with_a_call_another_call_already_pairs_with_exactly()
    {
        // A fixed order (a seeded id, the same in both runs) beside one the test creates: the created one
        // pairs with the order the new run created, not with the seeded one, whose exact partner it is.
        var oldSeeded = new Call("api", "GET", "http://api/orders/0b9a7d31-5c1e-4c47-9c2f-55b8c0e1d6a2", "{}", "{\"order\":\"seeded\",\"v\":1}");
        var oldCreated = new Call("api", "GET", "http://api/orders/9e8d7c6b-5a4f-4e3d-8c2b-1a0f9e8d7c6b", "{}", "{\"order\":\"created\",\"v\":1}");
        var newCreated = new Call("api", "GET", "http://api/orders/1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d", "{}", "{\"order\":\"created\",\"v\":2}");
        var newSeeded = new Call("api", "GET", "http://api/orders/0b9a7d31-5c1e-4c47-9c2f-55b8c0e1d6a2", "{}", "{\"order\":\"seeded\",\"v\":1}");
        var old = Write("Old.json", Rq(oldSeeded), Rs(oldSeeded), Rq(oldCreated), Rs(oldCreated));
        var @new = Write("New.json", Rq(newCreated), Rs(newCreated), Rq(newSeeded), Rs(newSeeded));

        Assert.Equal("s0/i0", Partner(old, @new, "s0/i2"));
        Assert.Equal("s0/i2", Partner(old, @new, "s0/i0"));
        var (output, _, _) = Diff(old, @new, "--body", "s0/i3");
        Assert.Contains("$.v: 1 → 2", output);
        Assert.DoesNotContain("$.order", output);
    }

    [Fact]
    public void A_wrong_pair_cannot_hide_a_change_behind_byte_identical_bodies()
    {
        // The silent direction of #115: two services' health checks, the second answering differently in
        // the new run. By ordinal the diff compared Kitchen's response with Goat's, and they were equal.
        var oldGoat = new Call("Goat", "GET", "http://goat/health", "{}", "{\"status\":\"ok\"}");
        var oldKitchen = new Call("Kitchen", "GET", "http://kitchen/health", "{}", "{\"status\":\"ok\"}");
        var newKitchen = new Call("Kitchen", "GET", "http://kitchen/health", "{}", "{\"status\":\"degraded\"}");
        var newGoat = new Call("Goat", "GET", "http://goat/health", "{}", "{\"status\":\"ok\"}");
        var old = Write("Old.json", Rq(oldGoat), Rs(oldGoat), Rq(oldKitchen), Rs(oldKitchen));
        var @new = Write("New.json", Rq(newKitchen), Rs(newKitchen), Rq(newGoat), Rs(newGoat));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i3");

        Assert.True(exit == 0, error);
        Assert.DoesNotContain("byte-identical", output);
        Assert.Contains("$.status: \"ok\" → \"degraded\"", output);
        Assert.Contains("call: Kitchen GET /health  (response)", output);
    }

    [Fact]
    public void A_side_without_a_body_is_named_by_its_file()
    {
        var oldCall = new Call("api", "DELETE", "http://api/hold/1", "{\"hold\":1}", "{\"released\":true}");
        var newCall = new Call("api", "DELETE", "http://api/hold/1", "{\"hold\":1}", response: null);
        var old = Write("Old.json", Rq(oldCall), Rs(oldCall));
        var @new = Write("New.json", Rq(newCall), Rs(newCall));

        var (_, error, exit) = Diff(old, @new, "--body", "s0/i1");

        Assert.Equal(2, exit);
        Assert.Contains("New.json s0/i1 carries no body", error);
    }

    // ─── The scenario ──────────────────────────────────────────

    [Fact]
    public void Ids_computed_under_different_suites_are_refused_as_the_run_diff_refuses_them()
    {
        var oldCall = new Call("api", "GET", "http://api/x", "{\"v\":1}");
        var newCall = new Call("api", "GET", "http://api/x", "{\"v\":2}");
        var old = Write("Old.json", "Orders.Tests", [("Checkout", [Rq(oldCall), Rs(oldCall)])]);
        var @new = Write("New.json", "Orders.Renamed", [("Checkout", [Rq(newCall), Rs(newCall)])]);

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i0");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("no stableId is shared by the two reports", error);
        Assert.Contains("SuiteName", error);
    }

    [Fact]
    public void A_report_without_stableIds_against_one_with_them_is_refused_as_the_run_diff_refuses_it()
    {
        var old = WriteJson("Old.json", "3.0.44", [("Checkout", null, [Json("api", "GET", "http://api/x", "{\"v\":1}")])]);
        var newCall = new Call("api", "GET", "http://api/x", "{\"v\":2}");
        var @new = Write("New.json", Rq(newCall), Rs(newCall));

        var (_, error, exit) = Diff(old, @new, "--body", "s0/i0");

        Assert.Equal(2, exit);
        Assert.Contains("the old report (Old.json) has no stableIds", error);
    }

    [Fact]
    public void A_scenario_the_new_run_holds_fewer_times_is_compared_with_its_first_holder_and_says_so()
    {
        var old = WriteJson("Old.json", "3.5.1", [
            ("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":1}")]),
            ("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":3}")])]);
        var @new = WriteJson("New.json", "3.5.1", [
            ("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":2}")])]);

        var (output, error, exit) = Diff(old, @new, "--body", "s1/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("! s1 is the 2nd of 2 scenarios with sid:aaaabbbbccccdddd in Old.json; New.json has 1, compared with its first", output);
        Assert.Contains("+ New.json s0/i1", output);
        Assert.Contains("$.v: 3 → 2", output);
    }

    // ─── What the answer says ──────────────────────────────────

    [Fact]
    public void The_json_names_both_calls_and_how_they_were_paired()
    {
        var oldA = new Call("Redis", "Set", "redis://cache:6379/a", "{\"v\":1}");
        var oldB = new Call("Redis", "Set", "redis://cache:6379/b", "{\"v\":1}");
        var newA = new Call("Redis", "Set", "redis://cache:6379/a", "{\"v\":2}");
        var newB = new Call("Redis", "Set", "redis://cache:6379/b", "{\"v\":1}");
        var old = Write("Old.json", Rq(oldA), Rs(oldA), Rq(oldB), Rs(oldB));
        var @new = Write("New.json", Rq(newB), Rs(newB), Rq(newA), Rs(newA));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i0", "--json");

        Assert.True(exit == 0, error);
        var envelope = JsonDocument.Parse(output).RootElement;
        var left = envelope.GetProperty("left");
        var right = envelope.GetProperty("right");
        Assert.EndsWith("Old.json", left.GetProperty("report").GetString());
        Assert.Equal("s0/i0", left.GetProperty("address").GetString());
        Assert.Equal("request", left.GetProperty("half").GetString());
        Assert.Equal("Redis", left.GetProperty("service").GetString());
        Assert.Equal("Set", left.GetProperty("method").GetString());
        Assert.Equal("redis://cache:6379/a", left.GetProperty("uri").GetString());
        Assert.StartsWith("b:", left.GetProperty("bodyHash").GetString());
        Assert.True(left.GetProperty("bodyLength").GetInt32() > 0);
        Assert.EndsWith("New.json", right.GetProperty("report").GetString());
        Assert.Equal("s0/i2", right.GetProperty("address").GetString());
        var pairing = envelope.GetProperty("pairing");
        Assert.Equal("uri", pairing.GetProperty("on").GetString());
        Assert.Equal(1, pairing.GetProperty("position").GetInt32());
        Assert.Equal(1, pairing.GetProperty("old").GetInt32());
        Assert.Equal(1, pairing.GetProperty("new").GetInt32());
        Assert.False(pairing.TryGetProperty("shape", out _));
        Assert.Contains(envelope.GetProperty("items").EnumerateArray(), i => i.GetProperty("path").GetString() == "$.v");
    }

    [Fact]
    public void The_json_of_a_pair_on_the_shape_carries_the_shape()
    {
        var oldOrder = new Call("api", "GET", "http://api/orders/0b9a7d31-5c1e-4c47-9c2f-55b8c0e1d6a2", "{}", "{\"state\":\"open\"}");
        var newOrder = new Call("api", "GET", "http://api/orders/f3c2b1a0-9d8e-4f7a-8b6c-5d4e3f2a1b0c", "{}", "{\"state\":\"paid\"}");
        var old = Write("Old.json", Rq(oldOrder), Rs(oldOrder));
        var @new = Write("New.json", Rq(newOrder), Rs(newOrder));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1", "--json");

        Assert.True(exit == 0, error);
        var envelope = JsonDocument.Parse(output).RootElement;
        var pairing = envelope.GetProperty("pairing");
        Assert.Equal("shape", pairing.GetProperty("on").GetString());
        Assert.Equal("/orders/{id}", pairing.GetProperty("shape").GetString());
        Assert.Equal("response", envelope.GetProperty("right").GetProperty("half").GetString());
        Assert.Contains(envelope.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("matched on its shape", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refusal_under_json_is_the_error_envelope_with_the_candidates_in_its_hint()
    {
        var oldCharts = new Call("Redis", "Set", "redis://cache:6379/app:charts-agg-1", "{\"size\":369}");
        var newCompetitor = new Call("Redis", "Set", "redis://cache:6379/app:competitor-charts-agg-1", "{\"size\":63}");
        var old = Write("Old.json", Rq(oldCharts), Rs(oldCharts));
        var @new = Write("New.json", Rq(newCompetitor), Rs(newCompetitor));

        var (output, _, exit) = Diff(old, @new, "--body", "s0/i0", "--json");

        Assert.Equal(2, exit);
        var error = JsonDocument.Parse(output).RootElement.GetProperty("error");
        Assert.Equal(2, error.GetProperty("exitCode").GetInt32());
        Assert.StartsWith("No call in New.json s0 matches Old.json s0/i0", error.GetProperty("message").GetString());
        Assert.Contains("s0/i0 /app:competitor-charts-agg-1", error.GetProperty("hint").GetString());
    }

    [Fact]
    public void Count_answers_the_paths_that_differ_between_the_paired_calls()
    {
        var oldA = new Call("api", "GET", "http://api/a", "{}", "{\"v\":1,\"w\":1}");
        var oldB = new Call("api", "GET", "http://api/b", "{}", "{\"x\":1}");
        var newA = new Call("api", "GET", "http://api/a", "{}", "{\"v\":2,\"w\":2}");
        var newB = new Call("api", "GET", "http://api/b", "{}", "{\"x\":1}");
        var old = Write("Old.json", Rq(oldA), Rs(oldA), Rq(oldB), Rs(oldB));
        var @new = Write("New.json", Rq(newB), Rs(newB), Rq(newA), Rs(newA));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1", "--count");

        Assert.True(exit == 0, error);
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Count_on_a_refused_pair_exits_2_and_prints_nothing()
    {
        var oldCall = new Call("api", "GET", "http://api/a", "{}", "{\"v\":1}");
        var newCall = new Call("api", "GET", "http://api/b", "{}", "{\"v\":1}");
        var old = Write("Old.json", Rq(oldCall), Rs(oldCall));
        var @new = Write("New.json", Rq(newCall), Rs(newCall));

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1", "--count");

        Assert.Equal(2, exit);
        Assert.Equal("", output);
        Assert.Contains("No call in New.json s0 matches", error);
    }

    // ─── Provenance ────────────────────────────────────────────

    [Fact]
    public void A_body_diff_across_runs_says_what_each_report_is_missing()
    {
        var old = WriteJson("Old.json", "3.0.44", [("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":1}")])]);
        var @new = WriteJson("New.json", "3.0.44", [("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":2}")])]);

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("! old: report predates step attribution", output);
        Assert.Contains("! new: report predates step attribution", output);
    }

    [Fact]
    public void A_body_diff_across_runs_under_count_says_it_on_stderr()
    {
        var old = WriteJson("Old.json", "3.0.44", [("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":1}")])]);
        var @new = WriteJson("New.json", "3.0.44", [("Checkout", "aaaabbbbccccdddd", [Json("api", "GET", "http://api/x", "{\"v\":2}")])]);

        var (output, error, exit) = Diff(old, @new, "--body", "s0/i1", "--count");

        Assert.Equal(0, exit);
        Assert.Equal("1\n", output);
        Assert.Contains("! old: report predates step attribution", error);
    }

    [Fact]
    public void A_body_diff_in_one_report_says_what_the_report_is_missing()
    {
        var report = WriteJson("Old.json", "3.0.44", [("Checkout", "aaaabbbbccccdddd", [
            Json("api", "GET", "http://api/x", "{\"v\":1}"), Json("api", "GET", "http://api/y", "{\"v\":2}")])]);

        var (output, error, exit) = Diff(report, "s0/i0", "s0/i1");

        Assert.True(exit == 0, error);
        Assert.Contains("! report predates step attribution", output);
        var (counted, countError, _) = Diff(report, "s0/i0", "s0/i1", "--count");
        Assert.Equal("1\n", counted);
        Assert.Contains("! report predates step attribution", countError);
    }

    // ─── compare ───────────────────────────────────────────────

    [Fact]
    public void Compare_points_at_the_first_body_that_differs_between_the_same_call_in_each_scenario()
    {
        // Two scenarios of one run whose calls ran in another order: by position the first pair is two
        // different calls, and the suggested diff compared /a's request with /b's.
        var a = new Call("api", "GET", "http://api/a", "{\"q\":\"a\"}", "{\"v\":1}");
        var b = new Call("api", "GET", "http://api/b", "{\"q\":\"b\"}", "{\"w\":1}");
        var bAgain = new Call("api", "GET", "http://api/b", "{\"q\":\"b\"}", "{\"w\":1}");
        var aAgain = new Call("api", "GET", "http://api/a", "{\"q\":\"a\"}", "{\"v\":2}");
        var report = Write("Report.json", null, [("First", [Rq(a), Rs(a), Rq(b), Rs(b)]), ("Second", [Rq(bAgain), Rs(bAgain), Rq(aAgain), Rs(aAgain)])]);

        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["compare", report, "s0", "s1"], output, error);

        Assert.True(exit == 0, error.ToString());
        Assert.Contains("first differing body: diff s0/i1 s1/i3", output.ToString());
    }

    // ─── Harness ───────────────────────────────────────────────

    /// <summary>The address <c>--json</c> says the old address was paired with.</summary>
    private static string? Partner(string old, string @new, string address)
    {
        var (output, error, exit) = Diff(old, @new, "--body", address, "--json");
        Assert.True(exit == 0, $"exit {exit}: {error}");
        return JsonDocument.Parse(output).RootElement.GetProperty("right").GetProperty("address").GetString();
    }

    private static (string Output, string Error, int Exit) Diff(string report, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = QueryCommand.Run(["diff", report, .. args], output, error);
        return (output.ToString(), error.ToString(), exit);
    }

    // ─── Fixtures ──────────────────────────────────────────────

    /// <summary>One call of one run. Each instance has its own pairing id, as every call of a run does.</summary>
    private sealed class Call(string service, string method, string uri, string request, string? response = "{\"ok\":true}")
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid TraceId { get; } = Guid.NewGuid();
        public string Service => service;
        public string Method => method;
        public string Uri => uri;
        public string Request => request;
        public string? Response => response;
    }

    private static (Call Call, bool Response) Rq(Call call) => (call, false);

    private static (Call Call, bool Response) Rs(Call call) => (call, true);

    /// <summary>A run of one scenario, "Checkout", holding the halves in the order given.</summary>
    private string Write(string fileName, params (Call Call, bool Response)[] entries) =>
        Write(fileName, null, [("Checkout", entries)]);

    /// <summary>A run of the scenarios given, each holding the halves in the order given, through the real writer.</summary>
    private string Write(string fileName, string? suite, (string Name, (Call Call, bool Response)[] Entries)[] scenarios)
    {
        var at = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var logs = new List<RequestResponseLog>();
        var features = new[]
        {
            new Feature
            {
                DisplayName = "Orders",
                Scenarios = scenarios.Select((scenario, s) => new Scenario
                {
                    Id = "t" + s, DisplayName = scenario.Name, Result = ExecutionResult.Passed, Duration = TimeSpan.FromSeconds(1),
                    Steps = [new ScenarioStep { Keyword = "When", Text = "checking out", Status = ExecutionResult.Passed }]
                }).ToArray()
            }
        };

        for (var s = 0; s < scenarios.Length; s++)
        {
            var testId = "t" + s;
            foreach (var (call, response) in scenarios[s].Entries)
            {
                at = at.AddMilliseconds(10);
                OneOf<HttpMethod, string> method = IsHttp(call.Method) ? new HttpMethod(call.Method) : call.Method;
                logs.Add(response
                    ? new RequestResponseLog(testId, testId, method, call.Response, new Uri(call.Uri), [], call.Service, "test",
                        RequestResponseType.Response, call.TraceId, call.Id, false, HttpStatusCode.OK) { Timestamp = at }
                    : new RequestResponseLog(testId, testId, method, call.Request, new Uri(call.Uri), [], call.Service, "test",
                        RequestResponseType.Request, call.TraceId, call.Id, false) { Timestamp = at });
            }
        }

        var written = ReportGenerator.GenerateTestRunReportData(
            features,
            new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 10, 5, 0, DateTimeKind.Utc),
            "Pairing_" + Guid.NewGuid().ToString("N")[..8] + ".json", DataFormat.Json, diagrams: null, logs.ToArray(), suite: suite);

        var path = Path.Combine(_directory, fileName);
        File.Move(written, path, overwrite: true);
        return path;
    }

    private static bool IsHttp(string method) =>
        method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";

    /// <summary>One call as a report's JSON holds it: a request with a body, and its response, sharing one pairing id.</summary>
    private static string Json(string service, string method, string uri, string response)
    {
        var id = Guid.NewGuid();
        var request = $$"""{ "type": "Request", "method": "{{method}}", "uri": "{{uri}}", "serviceName": "{{service}}", "callerName": "test", "content": "{}", "headers": [], "requestResponseId": "{{id}}" }""";
        var answer = $$"""{ "type": "Response", "method": "{{method}}", "uri": "{{uri}}", "serviceName": "{{service}}", "callerName": "test", "content": {{JsonSerializer.Serialize(response)}}, "headers": [], "statusCode": "OK", "requestResponseId": "{{id}}" }""";
        return request + ",\n" + answer;
    }

    /// <summary>
    /// A report written by hand, for what the current writer cannot produce: no stableId, a stableId shared by
    /// two scenarios, a version before step attribution.
    /// </summary>
    private string WriteJson(string fileName, string version, (string Name, string? StableId, string[] Calls)[] scenarios)
    {
        var body = string.Join(",\n", scenarios.Select((s, i) =>
        {
            var stableId = s.StableId is null ? "" : $"\"stableId\": \"{s.StableId}\",";
            return $$"""
                { "id": "t{{i}}", {{stableId}} "name": {{JsonSerializer.Serialize(s.Name)}}, "result": "Passed",
                  "durationSeconds": 1.0, "labels": [], "categories": [], "steps": [],
                  "httpInteractions": [ {{string.Join(",\n", s.Calls)}} ] }
                """;
        }));

        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "{{version}}",
              "startTime": "2026-01-01T10:00:00Z",
              "endTime": "2026-01-01T10:05:00Z",
              "features": [ { "name": "Orders", "labels": [], "scenarios": [ {{body}} ] } ]
            }
            """);
        return path;
    }
}
