using System.Net;
using Kronikol.Constants;
using Kronikol.History;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The first-call warm-up rule (plans/WARM_UP_PLAN.md 4.1, #113): for each kind of call a test makes, the run's first is
/// a warm-up when it took at least 10 times and 50 ms more than the median of the calls that started after it ended,
/// and so is a call that waited for it. <c>warmup.py marks</c> in the plan's harness is the rule's executable spec.
/// </summary>
public class WarmUpCallsTests
{
    [Fact]
    public void The_first_call_of_a_shape_ten_times_slower_than_the_later_ones_is_a_warm_up()
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600);
        for (var i = 1; i <= 4; i++)
            run.Call("s1", 600 + i * 100, 5);

        var found = run.Find();

        var mark = Assert.Single(found.Calls);
        Assert.Equal(first, mark.Key);
        Assert.Equal(WarmUpMark.FirstKind, mark.Value.Kind);
        Assert.Equal("POST /orders", mark.Value.Shape);
        Assert.Equal(5.0, mark.Value.BaselineMs, 3);
        Assert.Equal(4, mark.Value.BaselineCalls);
        Assert.Null(mark.Value.First);
        Assert.Equal(600.0, found.ScenarioMs["s1"], 3);
    }

    [Fact]
    public void Calls_that_all_take_as_long_mark_nothing()
    {
        var run = new Run().Scenario("s1");
        for (var i = 0; i < 5; i++)
            run.Call("s1", i * 1000, 600);

        var found = run.Find();

        Assert.Empty(found.Calls);
        Assert.Empty(found.ScenarioMs);
    }

    [Fact]
    public void A_shape_called_once_has_nothing_to_compare_with()
    {
        var run = new Run().Scenario("s1");
        run.Call("s1", 0, 600);

        Assert.Empty(run.Find().Calls);
    }

    [Fact]
    public void One_later_call_is_enough_to_compare_with()
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600);
        run.Call("s1", 700, 5);

        var mark = Assert.Single(run.Find().Calls);

        Assert.Equal(first, mark.Key);
        Assert.Equal(1, mark.Value.BaselineCalls);
    }

    [Fact]
    public void The_apps_own_calls_are_not_judged_and_the_warm_up_is_the_test_calls_time()
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600);
        // The app's first database call, inside the test's first call: as slow against its later calls.
        run.Call("s1", 50, 500, method: "Query", uri: "sql://db", service: "db", caller: "orders", category: DependencyCategories.SQL, content: "SELECT 1");
        for (var i = 1; i <= 4; i++)
        {
            run.Call("s1", 600 + i * 100, 5);
            run.Call("s1", 601 + i * 100, 1, method: "Query", uri: "sql://db", service: "db", caller: "orders", category: DependencyCategories.SQL, content: "SELECT 1");
        }

        var found = run.Find();

        Assert.Equal(first, Assert.Single(found.Calls).Key);
        Assert.Equal(600.0, found.ScenarioMs["s1"], 3);
    }

    [Theory]
    [InlineData("http://orders/orders/0b7d6a40-1c1e-4bde-9c2a-3c5e8f1d2a90", "http://orders/orders/7e3f9c21-5a4b-4c8d-8e2f-1a9b6c3d4e5f", "GET /orders/{id}")]
    [InlineData("http://orders/orders?page=1", "http://orders/orders?page=2", "GET /orders")]
    [InlineData("http://orders/recipe/Recipe-0123456789abcdef0123456789abcdef", "http://orders/recipe/Recipe-fedcba9876543210fedcba9876543210", "GET /recipe/Recipe-{id}")]
    public void Ids_and_query_strings_do_not_split_a_shape(string firstUri, string laterUri, string shape)
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600, method: "GET", uri: firstUri);
        run.Call("s1", 700, 5, method: "GET", uri: laterUri);
        run.Call("s1", 800, 5, method: "GET", uri: laterUri);

        var mark = Assert.Single(run.Find().Calls);

        Assert.Equal(first, mark.Key);
        Assert.Equal(shape, mark.Value.Shape);
    }

    [Fact]
    public void A_scenario_that_polls_with_every_call_warm_has_no_warm_up()
    {
        var run = new Run().Scenario("s1");
        for (var i = 0; i < 10; i++)
            run.Call("s1", i * 20, 5, method: "GET", uri: "http://orders/orders/1/status");

        Assert.Empty(run.Find().ScenarioMs);
    }

    [Fact]
    public void A_call_that_overlapped_the_first_and_is_ten_times_the_median_waited_for_it()
    {
        var run = new Run().Scenario("s1").Scenario("s2");
        var first = run.Call("s1", 0, 600);
        var waiter = run.Call("s2", 10, 300);
        run.Call("s1", 700, 5);
        run.Call("s2", 800, 5);

        var found = run.Find();

        Assert.Equal(2, found.Calls.Count);
        var waited = found.Calls[waiter];
        Assert.Equal(WarmUpMark.WaitedKind, waited.Kind);
        Assert.Equal(first, waited.First);
        Assert.Equal(5.0, waited.BaselineMs, 3);
        Assert.Equal(300.0, found.ScenarioMs["s2"], 3);
    }

    [Fact]
    public void A_call_that_overlapped_the_first_and_was_released_with_it_waited_for_it()
    {
        // 9.4 times the median: under the ratio, but it ended with the first call, so it was held by the same warm-up.
        var run = new Run().Scenario("s1").Scenario("s2");
        var first = run.Call("s1", 0, 1000);
        var waiter = run.Call("s2", 530, 470);
        run.Call("s1", 1100, 50);
        run.Call("s2", 1200, 50);

        var found = run.Find();

        Assert.Equal(WarmUpMark.WaitedKind, found.Calls[waiter].Kind);
        Assert.Equal(first, found.Calls[waiter].First);
    }

    [Fact]
    public void The_baseline_is_the_calls_that_started_after_the_first_ended()
    {
        // Two calls held by the same warm-up would raise the median to their own time and hide it.
        var run = new Run().Scenario("s1").Scenario("s2").Scenario("s3");
        var first = run.Call("s1", 0, 600);
        run.Call("s2", 10, 590);
        run.Call("s3", 20, 580);
        run.Call("s1", 700, 5);

        var found = run.Find();

        Assert.Equal(3, found.Calls.Count);
        Assert.Equal(5.0, found.Calls[first].BaselineMs, 3);
        Assert.Equal(1, found.Calls[first].BaselineCalls);
    }

    [Fact]
    public void A_call_that_overlapped_the_first_under_the_ratio_and_ended_well_before_it_is_not_marked()
    {
        var run = new Run().Scenario("s1").Scenario("s2");
        var first = run.Call("s1", 0, 1000);
        run.Call("s2", 330, 470);
        run.Call("s1", 1100, 50);
        run.Call("s2", 1200, 50);

        Assert.Equal(first, Assert.Single(run.Find().Calls).Key);
    }

    [Fact]
    public void A_slow_call_that_started_after_the_first_ended_is_not_marked()
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600);
        run.Call("s1", 700, 5);
        run.Call("s1", 800, 5);
        run.Call("s1", 900, 590);
        run.Call("s1", 1600, 5);

        Assert.Equal(first, Assert.Single(run.Find().Calls).Key);
    }

    [Fact]
    public void A_shape_with_an_untimed_call_is_not_judged()
    {
        // The untimed call has no end, so it may have been the first to finish: nothing is said about the shape.
        var run = new Run().Scenario("s1");
        run.Call("s1", 0, 600);
        run.Call("s1", 700, 5);
        run.Call("s1", 800, durationMs: null);

        Assert.Empty(run.Find().Calls);
    }

    [Fact]
    public void Overlapping_warm_up_calls_in_one_scenario_are_counted_once()
    {
        var run = new Run().Scenario("s1", durationMs: 2000);
        run.Call("s1", 0, 600);
        run.Call("s1", 100, 590);
        run.Call("s1", 800, 5);
        run.Call("s1", 900, 5);

        var found = run.Find();

        Assert.Equal(2, found.Calls.Count);
        Assert.Equal(690.0, found.ScenarioMs["s1"], 3);
    }

    [Fact]
    public void A_warm_up_longer_than_its_scenario_is_capped_at_the_scenarios_duration()
    {
        var run = new Run().Scenario("s1", durationMs: 500).Scenario("s2");
        run.Call("s1", 0, 600);
        run.Call("s2", 700, 5);

        Assert.Equal(500.0, run.Find().ScenarioMs["s1"], 3);
    }

    [Fact]
    public void A_scenario_with_no_duration_carries_its_marks_and_no_warm_up_time()
    {
        var run = new Run().Scenario("s1", durationMs: null).Scenario("s2");
        var first = run.Call("s1", 0, 600);
        run.Call("s2", 700, 5);

        var found = run.Find();

        Assert.Equal(first, Assert.Single(found.Calls).Key);
        Assert.DoesNotContain("s1", found.ScenarioMs.Keys);
    }

    [Theory]
    [InlineData(100.0, 10.0, true)]   // exactly 10 times, 90 ms above
    [InlineData(99.9, 10.0, false)]   // 9.99 times
    [InlineData(55.0, 5.0, true)]     // 11 times, exactly 50 ms above
    [InlineData(54.9, 5.0, false)]    // 10.98 times, 49.9 ms above
    public void The_ratio_and_the_floor_are_both_inclusive(double firstMs, double laterMs, bool marked)
    {
        var run = new Run().Scenario("s1");
        run.Call("s1", 0, firstMs);
        run.Call("s1", 200, laterMs);
        run.Call("s1", 300, laterMs);

        Assert.Equal(marked, run.Find().Calls.Count == 1);
    }

    [Fact]
    public void A_test_caller_of_any_name_is_judged()
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600, caller: "Client");
        run.Call("s1", 700, 5, caller: "Client");

        Assert.Equal(first, Assert.Single(run.Find().Calls).Key);
    }

    [Fact]
    public void A_caller_that_is_also_called_in_its_scenario_is_the_app_and_is_not_judged()
    {
        var run = new Run().Scenario("s1");
        run.Call("s1", 0, 1000, service: "gateway", uri: "http://gateway/orders");
        run.Call("s1", 10, 600, service: "orders", caller: "gateway");
        run.Call("s1", 1100, 5, service: "gateway", uri: "http://gateway/orders");
        run.Call("s1", 1101, 3, service: "orders", caller: "gateway");

        var found = run.Find();

        Assert.Single(found.Calls);
        Assert.All(found.Calls.Values, m => Assert.Equal("POST /orders", m.Shape));
        Assert.Equal(1000.0, found.ScenarioMs["s1"], 3);
    }

    [Fact]
    public void An_event_is_not_judged()
    {
        var run = new Run().Scenario("s1");
        run.Call("s1", 0, 600, meta: RequestResponseMetaType.Event, service: "bus", uri: "http://bus/order-placed");
        run.Call("s1", 700, 5, meta: RequestResponseMetaType.Event, service: "bus", uri: "http://bus/order-placed");

        Assert.Empty(run.Find().Calls);
    }

    [Fact]
    public void A_call_in_the_background_is_neither_judged_nor_charged()
    {
        // Expired from s1 (it arrived after s1 ended), so it is the host's: it is not the shape's first call.
        var run = new Run().Scenario("s1");
        run.Call(TestIdentityScope.UnknownTestId, 0, 600, expiredFrom: "s1");
        run.Call("s1", 700, 5);
        run.Call("s1", 800, 5);

        var found = run.Find();

        Assert.Empty(found.Calls);
        Assert.Empty(found.ScenarioMs);
    }

    [Fact]
    public void Statements_with_different_heads_on_one_connection_are_two_shapes()
    {
        var run = new Run().Scenario("s1");
        var select = run.Call("s1", 0, 600, method: "Query", uri: "sql://db", service: "db", category: DependencyCategories.SQL, content: "SELECT * FROM orders WHERE id = 1");
        var insert = run.Call("s1", 700, 300, method: "Query", uri: "sql://db", service: "db", category: DependencyCategories.SQL, content: "INSERT INTO orders VALUES (2)");
        for (var i = 1; i <= 2; i++)
        {
            run.Call("s1", 1000 + i * 100, 5, method: "Query", uri: "sql://db", service: "db", category: DependencyCategories.SQL, content: $"SELECT * FROM orders WHERE id = {i + 2}");
            run.Call("s1", 1050 + i * 100, 5, method: "Query", uri: "sql://db", service: "db", category: DependencyCategories.SQL, content: $"INSERT INTO orders VALUES ({i + 4})");
        }

        var found = run.Find();

        Assert.Equal([insert, select], found.Calls.Keys.OrderBy(k => k == select));
        Assert.StartsWith("QUERY / SELECT", found.Calls[select].Shape);
    }

    [Fact]
    public void The_order_is_the_runs_not_the_data_files()
    {
        // The data file lists features by name, so "A" comes first there; "B"'s scenario ran first and paid the warm-up.
        var run = new Run().Scenario("a1", feature: "A").Scenario("b1", feature: "B");
        run.Call("a1", 700, 5);
        var first = run.Call("b1", 0, 600);
        run.Call("a1", 800, 5);

        var found = run.Find();

        Assert.Equal(first, Assert.Single(found.Calls).Key);
        Assert.Equal(["b1"], found.ScenarioMs.Keys);
    }

    [Fact]
    public void Calls_that_start_in_one_millisecond_are_ordered_as_the_data_file_records_them()
    {
        // The data files write a start to the millisecond, so a reader of the report sees these two start together and
        // orders them by duration. The marks the report carries must be the ones its own file gives (S7: a parallel lane
        // started four GET /milk calls in one millisecond, and the first by ticks was not the first the file shows).
        var run = new Run().Scenario("s1").Scenario("s2");
        var later = run.Call("s2", 0.1, 610);
        var first = run.Call("s1", 0.9, 600);
        run.Call("s1", 1000, 5);
        run.Call("s1", 1100, 5);

        var found = run.Find();

        Assert.Equal(WarmUpMark.FirstKind, found.Calls[first].Kind);
        Assert.Equal(WarmUpMark.WaitedKind, found.Calls[later].Kind);
        Assert.Equal(first, found.Calls[later].First);
    }

    [Fact]
    public void A_failing_first_call_is_not_compared_with_calls_that_succeeded()
    {
        var run = new Run().Scenario("s1");
        run.Call("s1", 0, 600, status: 503);
        run.Call("s1", 700, 5);
        run.Call("s1", 800, 5);

        Assert.Empty(run.Find().Calls);
    }

    [Fact]
    public void A_failing_first_call_is_compared_with_the_later_calls_that_failed()
    {
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600, status: 503);
        run.Call("s1", 700, 5);
        run.Call("s1", 800, 4, status: 500);

        var mark = Assert.Single(run.Find().Calls);

        Assert.Equal(first, mark.Key);
        Assert.Equal(4.0, mark.Value.BaselineMs, 3);
        Assert.Equal(1, mark.Value.BaselineCalls);
    }

    [Fact]
    public void The_consumers_shape_templates_join_ids_the_built_in_ones_do_not_know()
    {
        var rules = HistoryShapeRules.Create([new HistoryShapeTemplate(@"order-[a-z]+", "{order}")]);
        var run = new Run().Scenario("s1");
        var first = run.Call("s1", 0, 600, method: "GET", uri: "http://orders/orders/order-alpha");
        run.Call("s1", 700, 5, method: "GET", uri: "http://orders/orders/order-beta");

        Assert.Empty(run.Find().Calls);
        var mark = Assert.Single(run.Find(rules).Calls);
        Assert.Equal(first, mark.Key);
        Assert.Equal("GET /orders/{order}", mark.Value.Shape);
    }

    /// <summary>A run's scenarios and calls, timed from one instant.</summary>
    private sealed class Run
    {
        private static readonly DateTimeOffset T0 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        private readonly List<RequestResponseLog> _logs = [];
        private readonly Dictionary<string, List<Scenario>> _features = new(StringComparer.Ordinal);

        public Run Scenario(string id, double? durationMs = 10_000, string feature = "Orders")
        {
            if (!_features.TryGetValue(feature, out var scenarios))
                _features[feature] = scenarios = [];
            scenarios.Add(new Scenario
            {
                Id = id,
                DisplayName = id,
                Result = ExecutionResult.Passed,
                Duration = durationMs is { } ms ? TimeSpan.FromTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond)) : null
            });
            return this;
        }

        public Guid Call(string scenario, double startMs, double? durationMs, string method = "POST", string uri = "http://orders/orders",
            string service = "orders", string caller = "Test", int? status = 200, RequestResponseMetaType meta = RequestResponseMetaType.Default,
            string? category = null, string? content = null, string? expiredFrom = null)
        {
            var (trace, id) = (Guid.NewGuid(), Guid.NewGuid());
            OneOf<HttpMethod, string> verb = method is "GET" or "POST" or "PUT" or "DELETE" ? new HttpMethod(method) : method;
            _logs.Add(new RequestResponseLog(scenario, scenario, verb, content, new Uri(uri, UriKind.RelativeOrAbsolute), [], service, caller,
                RequestResponseType.Request, trace, id, false, null, meta, category)
            { Timestamp = At(startMs), ExpiredFromTestId = expiredFrom });
            if (durationMs is { } ms)
                _logs.Add(new RequestResponseLog(scenario, scenario, verb, null, new Uri(uri, UriKind.RelativeOrAbsolute), [], service, caller,
                    RequestResponseType.Response, trace, id, false, status is { } code ? (HttpStatusCode)code : null, meta, category)
                { Timestamp = At(startMs + ms), ExpiredFromTestId = expiredFrom });
            return id;
        }

        public WarmUpResult Find(HistoryShapeRules? rules = null) =>
            WarmUpCalls.Find(_features.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new Feature { DisplayName = f.Key, Scenarios = f.Value.ToArray() }).ToArray(), _logs, rules);

        private static DateTimeOffset At(double ms) => T0.AddTicks((long)Math.Round(ms * TimeSpan.TicksPerMillisecond));
    }
}
