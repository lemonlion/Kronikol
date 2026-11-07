using System.Net;
using System.Runtime.CompilerServices;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// An in-process call through <see cref="TrackingProxy{T}"/> that throws (plans/FAILED_CALLS_PLAN.md F17, F24, F28, Q2, §5.7): it
/// is answered with the exception's type, not a made-up 500, and why; the caller gets the very exception with its own stack; a
/// stream is answered when it ends or fails; and the calls a deferred proxy queued during a send that threw go to that send's test.
/// </summary>
[Collection("DiagramsFetcher")]
public class TrackingProxyFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private TrackingProxyOptions Options(TrackingLogMode mode = TrackingLogMode.Immediate) => new()
    {
        ServiceName = "Orders",
        CallerName = "Caller",
        LogMode = mode,
        CurrentTestInfoFetcher = () => ("Orders are placed", _testId),
    };

    public interface IOrders
    {
        int Place(int id);
        Task<int> PlaceAsync(int id);
        Task CancelAsync(int id);
        ValueTask<int> CountAsync();
        IAsyncEnumerable<int> Stream(int failAfter);
    }

    private sealed class Orders(Exception exception) : IOrders
    {
        public int Place(int id) => Thrown();

        public async Task<int> PlaceAsync(int id)
        {
            await Task.Yield();
            return Thrown();
        }

        public async Task CancelAsync(int id)
        {
            await Task.Yield();
            Thrown();
        }

        public async ValueTask<int> CountAsync()
        {
            await Task.Yield();
            return Thrown();
        }

        public async IAsyncEnumerable<int> Stream(int failAfter)
        {
            for (var i = 0; i < 3; i++)
            {
                await Task.Yield();
                if (i == failAfter)
                    Thrown();
                yield return i;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private int Thrown()
        {
            FailedCallContract.Throw(exception);
            return 0;
        }
    }

    private static InvalidOperationException Refused() => new("The order book is closed.", new TimeoutException("no lock in 5 s"));

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public void A_synchronous_call_that_throws_is_answered_with_the_exceptions_type_and_reaches_the_caller_with_its_own_stack()
    {
        var thrown = Refused();
        var orders = TrackingProxy<IOrders>.Create(new Orders(thrown), Options());

        var caught = Assert.Throws<InvalidOperationException>(() => orders.Place(1));

        FailedCallContract.AssertUntouched(caught, thrown);
        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal("InvalidOperationException: The order book is closed.", answer.Content);
    }

    public static TheoryData<string> AsyncCalls() => ["Task<T>", "Task", "ValueTask<T>"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(AsyncCalls))]
    public async Task An_async_call_that_throws_is_answered_with_the_exceptions_type(string shape)
    {
        var thrown = Refused();
        var orders = TrackingProxy<IOrders>.Create(new Orders(thrown), Options());

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => shape switch
        {
            "Task<T>" => orders.PlaceAsync(1),
            "Task" => orders.CancelAsync(1),
            _ => orders.CountAsync().AsTask(),
        });

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public void History_rules_before_12_read_the_made_up_500_they_recorded()
    {
        var orders = TrackingProxy<IOrders>.Create(new Orders(Refused()), Options());

        Assert.Throws<InvalidOperationException>(() => orders.Place(1));

        Assert.Equal("!InvalidOperationException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Equal("500", Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_stream_that_fails_partway_is_answered_with_the_exceptions_type_when_it_fails()
    {
        var thrown = Refused();
        var orders = TrackingProxy<IOrders>.Create(new Orders(thrown), Options());
        var read = new List<int>();

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var item in orders.Stream(failAfter: 1))
                read.Add(item);
        });

        Assert.Equal([0], read);
        Assert.Same(thrown, caught);
        FailedCallContract.AssertAnswered(Logs(), thrown);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Equal("200", Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public async Task A_stream_read_to_its_end_is_answered_then_with_how_many_items_it_gave()
    {
        var orders = TrackingProxy<IOrders>.Create(new Orders(Refused()), Options());

        await foreach (var _ in orders.Stream(failAfter: 99)) { Assert.Empty(Logs().Where(l => l.Type == RequestResponseType.Response)); }

        var answer = Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode?.Value);
        Assert.Equal("3 items", answer.Content);
        Assert.Null(answer.Error);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task The_calls_a_deferred_proxy_queued_during_a_send_that_threw_are_filed_under_that_sends_test()
    {
        PendingRequestResponseLogs.Clear();
        var otherTest = Guid.NewGuid().ToString();
        var orders = TrackingProxy<IOrders>.Create(new WorkingOrders(), Options(TrackingLogMode.Deferred));

        using (var failing = new HttpClient(new DeferredLogFlushHandler(() => ("Orders are placed", _testId)) { InnerHandler = new App(orders, throws: true) }))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.GetAsync("http://app/a", TestContext.Current.CancellationToken));
        using (var next = new HttpClient(new DeferredLogFlushHandler(() => ("The next test", otherTest)) { InnerHandler = new App(orders, throws: false) }))
            await next.GetAsync("http://app/b", TestContext.Current.CancellationToken);

        Assert.Equal(2, Logs().Length);
        Assert.Equal(2, RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId == otherTest));
        // Before #232 the first test had no such call: history's earlier rules still read none there.
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Empty(InteractionShape.Calls(Logs(), null, rule));
        Assert.Single(InteractionShape.Calls(Logs(), null, 12));
    }

    private sealed class WorkingOrders : IOrders
    {
        public int Place(int id) => id;
        public Task<int> PlaceAsync(int id) => Task.FromResult(id);
        public Task CancelAsync(int id) => Task.CompletedTask;
        public ValueTask<int> CountAsync() => ValueTask.FromResult(1);
        public IAsyncEnumerable<int> Stream(int failAfter) => AsyncEnumerable.Empty<int>();
    }

    private sealed class App(IOrders orders, bool throws) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            orders.Place(1);
            if (throws)
                throw new InvalidOperationException("the app threw");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
