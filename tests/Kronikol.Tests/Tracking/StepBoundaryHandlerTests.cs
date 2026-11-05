using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// R2 of plans/STEP_PHASE_PLAN.md (5.6): what the handler has seen of a test's steps is the test's, not the handler's. The handler
/// asks its fetcher only while no framework step of the test has decided a phase and this client has not passed the boundary.
/// </summary>
[Collection("DiagramsFetcher")]
public class StepBoundaryHandlerTests
{
    public StepBoundaryHandlerTests() => TestPhaseContext.Reset();

    private static string NewTestId() => $"step-boundary-handler-{Guid.NewGuid():N}";

    private sealed class Answers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private sealed class Client(Func<string> testId, Func<string?> stepType) : IDisposable
    {
        private readonly HttpMessageInvoker _invoker = new(new TestTrackingMessageHandler(new TestTrackingMessageHandlerOptions
        {
            CallerName = "Caller",
            FixedNameForReceivingService = "Svc",
            CurrentTestInfoFetcher = () => ("Test", testId()),
            CurrentStepTypeFetcher = stepType,
        })
        { InnerHandler = new Answers() });

        public Task Call(string path = "call") =>
            _invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"http://example.test/{path}"), CancellationToken.None);

        public void Dispose() => _invoker.Dispose();
    }

    private static RequestResponseLog[] Logs(string id) => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == id).ToArray();

    private static int Markers(string id) => Logs(id).Count(l => l.IsActionStart);

    private static int? Adapter(string id, string? keyword) =>
        StepCollector.StartFrame(id, keyword, "a step", phaseKeyword: keyword);

    // F4: of two scenarios on one handler, the second had no boundary.
    [Fact]
    public async Task A_handler_shared_by_two_tests_draws_each_ones_boundary()
    {
        var first = NewTestId();
        var second = NewTestId();
        var id = first;
        var step = "GIVEN";
        using var client = new Client(() => id, () => step);

        await client.Call();
        step = "WHEN";
        await client.Call();

        id = second;
        step = "GIVEN";
        await client.Call();
        step = "WHEN";
        await client.Call();

        Assert.Equal(1, Markers(first));
        Assert.Equal(1, Markers(second));
    }

    [Fact]
    public async Task A_test_whose_framework_steps_set_its_phase_never_asks_the_fetcher()
    {
        var id = NewTestId();
        var asked = 0;
        using var client = new Client(() => id, () =>
        {
            asked++;
            return "GIVEN";
        });

        var given = Adapter(id, "GIVEN");
        await client.Call();
        StepCollector.CompleteFrame(id, given, passed: true);

        Assert.Equal(0, asked);
    }

    // review/H21.
    [Fact]
    public async Task A_woven_keyword_does_not_switch_a_custom_fetcher_off()
    {
        var id = NewTestId();
        var asked = 0;
        using var client = new Client(() => id, () =>
        {
            asked++;
            return "GIVEN";
        });

        StepCollector.StartStep(id, "Given", "woven", null, null);
        await client.Call();
        StepCollector.CompleteStep(id, passed: true);

        Assert.Equal(1, asked);
    }

    // review5/L01: on the [Obsolete] LightBDD path the fetcher is each client's only source of a phase.
    [Fact]
    public async Task A_second_client_on_a_test_with_no_decorator_still_asks_its_fetcher()
    {
        var id = NewTestId();
        var step = "GIVEN";
        var askedSecond = 0;
        using var first = new Client(() => id, () => step);
        using var second = new Client(() => id, () =>
        {
            askedSecond++;
            return step;
        });

        await first.Call();
        step = "WHEN";
        await first.Call();
        await second.Call();

        Assert.Equal(1, askedSecond);
        Assert.Equal(1, Markers(id));
    }

    // T4b: a client past its boundary has nothing to ask, and on the [Obsolete] LightBDD path an answer would write the phase back.
    [Fact]
    public async Task A_client_past_its_boundary_no_longer_asks_its_fetcher()
    {
        var id = NewTestId();
        var step = "GIVEN";
        var asked = 0;
        using var client = new Client(() => id, () =>
        {
            asked++;
            return step;
        });

        await client.Call();
        step = "WHEN";
        await client.Call();
        step = "GIVEN";
        await client.Call();

        Assert.Equal(2, asked);
        Assert.Equal(1, Markers(id));
    }

    // review1/R19: a When nested under a framework's Given is a label here too, so the handler draws nothing from it and every call reads Setup. While
    // the handler read the word, the first call read Action and the rest Setup, and TrackDuringSetup = false kept the one and dropped the rest.
    [Fact]
    public async Task A_When_nested_under_a_frameworks_Given_is_a_label_to_the_handler()
    {
        var id = NewTestId();
        using var client = new Client(() => id, () => "WHEN");

        var given = Adapter(id, "Given");
        var when = Adapter(id, "When");
        await client.Call("one");
        await client.Call("two");
        StepCollector.CompleteFrame(id, when, passed: true);
        StepCollector.CompleteFrame(id, given, passed: true);

        Assert.Equal(0, Markers(id));
        Assert.All(Logs(id).Where(l => l.Uri?.AbsolutePath is "/one" or "/two"), l => Assert.Equal(TestPhase.Setup, l.Phase));
    }

    // review2/N04: as a call does not fail for want of a step type, nor for want of a test id.
    [Fact]
    public async Task A_fetcher_with_no_test_id_does_not_fail_the_call()
    {
        using var client = new Client(() => null!, () => "GIVEN");
        var path = $"no-test-id-{Guid.NewGuid():N}";

        await client.Call(path);

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.Uri?.AbsolutePath == $"/{path}").ToArray();
        Assert.NotEmpty(logs);
        Assert.All(logs, l => Assert.Equal(TestIdentityScope.UnknownTestId, l.TestId));
    }

    // review2/F5: the boundary was logged after the lock, so another client's first call past it could be logged ahead of it, in about 1 test in 100.
    [Fact]
    public async Task Clients_in_parallel_at_the_first_act_log_no_call_ahead_of_the_boundary()
    {
        var late = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 500), new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (_, _) =>
        {
            var id = NewTestId();
            var step = "GIVEN";
            var clients = Enumerable.Range(0, 4).Select(_ => new Client(() => id, () => step)).ToArray();
            foreach (var client in clients)
                await client.Call("setup");
            step = "WHEN";
            await Task.WhenAll(clients.Select(client => Task.Run(() => client.Call("act"))));
            foreach (var client in clients)
                client.Dispose();

            var logs = Logs(id);
            var boundary = Array.FindIndex(logs, l => l.IsActionStart);
            var firstAct = Array.FindIndex(logs, l => l.Uri?.AbsolutePath == "/act");
            if (boundary < 0 || firstAct < boundary)
                late.Add(id);
        });

        Assert.Empty(late);
    }

    // review5/L07: it draws no boundary from what a fetcher saw.
    [Fact]
    public async Task A_woven_When_under_a_fetchers_Given_draws_no_boundary()
    {
        var id = NewTestId();
        using var client = new Client(() => id, () => "GIVEN");

        await client.Call();
        StepCollector.StartStep(id, "When", "woven", null, null);
        StepCollector.CompleteStep(id, passed: true);

        Assert.Equal(0, Markers(id));
    }

    // F16: LightBDD's fetcher throws outside a scenario. A handler that had drawn a boundary stopped asking, which hid it.
    [Fact]
    public async Task A_fetcher_that_throws_outside_a_scenario_does_not_fail_the_call()
    {
        var id = NewTestId();
        using var client = new Client(() => id, () => throw new InvalidOperationException("not in a scenario"));

        await client.Call();

        Assert.Contains(Logs(id), l => l.Type == RequestResponseType.Response);
    }

    // review6/F2: a shared handler now asks again for each new test, of a fetcher written for one.
    [Fact]
    public async Task A_fetcher_that_throws_anything_on_a_later_test_does_not_fail_the_call()
    {
        var first = NewTestId();
        var second = NewTestId();
        var id = first;
        var step = "GIVEN";
        using var client = new Client(() => id, () => id == second ? throw new NullReferenceException() : step);

        await client.Call();
        step = "WHEN";
        await client.Call();

        id = second;
        await client.Call();

        Assert.Equal(1, Markers(first));
        Assert.Contains(Logs(second), l => l.Type == RequestResponseType.Response);
    }

    // review6/F1.
    [Fact]
    public async Task A_second_clients_first_call_past_the_boundary_reads_Action()
    {
        var id = NewTestId();
        var step = "GIVEN";
        using var first = new Client(() => id, () => step);
        using var second = new Client(() => id, () => step);

        await first.Call("first-setup");
        await second.Call("second-setup");
        step = "WHEN";
        await first.Call("first-act");
        await second.Call("second-act");

        var call = Assert.Single(Logs(id), l => l.Type == RequestResponseType.Request && l.Uri?.AbsolutePath == "/second-act");
        Assert.Equal(TestPhase.Action, call.Phase);
        Assert.Equal(1, Markers(id));
    }
}
