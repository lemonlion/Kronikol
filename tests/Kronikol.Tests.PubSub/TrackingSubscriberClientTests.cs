using Google.Cloud.PubSub.V1;
using Kronikol.Extensions.PubSub;

namespace Kronikol.Tests.PubSub;

/// <summary>
/// Stopping the tracking subscriber stops the client it wraps, by each overload the client has. From 3.35.0 the client's
/// own <c>StopAsync(TimeSpan)</c> goes through <c>StopAsync(ShutdownOptions, CancellationToken)</c>, whose base throws,
/// so a wrapper that left that overload to the base threw <see cref="NotImplementedException"/> on both
/// (<c>plans/CLIENT_FLOORS_PLAN.md</c> R2a, found by the real client against the Pub/Sub emulator).
/// </summary>
public class TrackingSubscriberClientTests
{
    private static PubSubTrackingOptions Options() => new()
    {
        ServiceName = "PubSub",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("My PubSub Test", Guid.NewGuid().ToString()),
    };

    [Fact]
    public async Task Stopping_with_shutdown_options_stops_the_inner_client_with_them()
    {
        var inner = new RecordingSubscriberClient();
        var subscriber = new TrackingSubscriberClient(inner, Options());
        var shutdown = new SubscriberClient.ShutdownOptions { Mode = SubscriberClient.ShutdownMode.NackImmediately };

        await subscriber.StopAsync(shutdown, TestContext.Current.CancellationToken);

        Assert.Same(shutdown, Assert.Single(inner.Stops));
    }

    [Fact]
    public async Task Stopping_with_a_timeout_stops_the_inner_client()
    {
        var inner = new RecordingSubscriberClient();
        var subscriber = new TrackingSubscriberClient(inner, Options());

#pragma warning disable CS0618 // obsolete from 3.35.0, and the overload most suites written before it call
        await subscriber.StopAsync(TimeSpan.FromSeconds(5));
#pragma warning restore CS0618

        Assert.Single(inner.Stops);
    }

    [Fact]
    public async Task Stopping_with_a_cancellation_token_stops_the_inner_client()
    {
        var inner = new RecordingSubscriberClient();
        var subscriber = new TrackingSubscriberClient(inner, Options());

        await subscriber.StopAsync(TestContext.Current.CancellationToken);

        Assert.Single(inner.Stops);
    }

    /// <summary>A subscriber that records each stop, by whichever overload reached it.</summary>
    private sealed class RecordingSubscriberClient : SubscriberClient
    {
        public List<ShutdownOptions?> Stops { get; } = [];

        public override SubscriptionName SubscriptionName { get; } = new("p", "my-subscription");

        public override Task StartAsync(Func<PubsubMessage, CancellationToken, Task<Reply>> handlerAsync) => Task.CompletedTask;

        public override Task StopAsync(ShutdownOptions shutdownOptions, CancellationToken cancellationToken)
        {
            Stops.Add(shutdownOptions);
            return Task.CompletedTask;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            Stops.Add(null);
            return Task.CompletedTask;
        }
    }
}
