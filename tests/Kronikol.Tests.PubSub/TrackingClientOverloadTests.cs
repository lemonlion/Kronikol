using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Kronikol.Extensions.PubSub;
using Kronikol.Tests.Packaging;
using Kronikol.Tracking;
using Encoding = System.Text.Encoding;

namespace Kronikol.Tests.PubSub;

/// <summary>
/// The overloads the Pub/Sub clients keep virtual and the tracking clients leave to the base come back to an overload the
/// tracking client overrides, so each publish is tracked and sent once, and a subscriber started with a
/// <see cref="SubscriptionHandler"/> handles its messages on the tracked path. The tracking clients derive from the
/// clients through their constructors for mocking, so an overload whose base did not come back would run against an
/// empty client, as <c>StopAsync(ShutdownOptions, CancellationToken)</c> did (<c>plans/CLIENT_FLOORS_PLAN.md</c> F8).
/// </summary>
public class TrackingClientOverloadTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private PubSubTrackingOptions Options() => new()
    {
        ServiceName = "PubSub",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("My PubSub Test", _testId),
    };

    private int Logged(RequestResponseType type) =>
        RequestResponseLogger.RequestAndResponseLogs.Count(l => l.TestId == _testId && l.Type == type);

    public static TheoryData<string> PublishOverloads =>
    [
        "text", "text, encoding", "bytes", "ByteString", "IMessage",
        "key, text, encoding", "key, bytes", "key, ByteString", "key, IMessage",
    ];

    [Theory]
    [MemberData(nameof(PublishOverloads))]
    public async Task Each_publish_overload_sends_one_tracked_message_through_the_client_it_wraps(string overload)
    {
        var inner = new RecordingPublisherClient();
        var publisher = new TrackingPublisherClient(inner, Options(), null);

        var id = overload switch
        {
            "text" => await publisher.PublishAsync("hello"),
            "text, encoding" => await publisher.PublishAsync("hello", Encoding.UTF8),
            "bytes" => await publisher.PublishAsync(Encoding.UTF8.GetBytes("hello")),
            "ByteString" => await publisher.PublishAsync(ByteString.CopyFromUtf8("hello")),
            "IMessage" => await publisher.PublishAsync(new StringValue { Value = "hello" }),
            "key, text, encoding" => await publisher.PublishAsync("k", "hello", Encoding.UTF8),
            "key, bytes" => await publisher.PublishAsync("k", Encoding.UTF8.GetBytes("hello")),
            "key, ByteString" => await publisher.PublishAsync("k", ByteString.CopyFromUtf8("hello")),
            "key, IMessage" => await publisher.PublishAsync("k", new StringValue { Value = "hello" }),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };

        Assert.Equal("message-1", id);
        var sent = Assert.Single(inner.Sent);
        Assert.Equal(overload.StartsWith("key", StringComparison.Ordinal) ? "k" : "", sent.OrderingKey);
        Assert.Equal(1, Logged(RequestResponseType.Request));
        Assert.Equal(1, Logged(RequestResponseType.Response));
    }

    [Fact]
    public async Task A_subscriber_started_with_a_SubscriptionHandler_handles_on_the_tracked_path()
    {
        var subscriber = new TrackingSubscriberClient(new OneMessageSubscriberClient(), Options());

        await subscriber.StartAsync(new AckingHandler());

        Assert.Equal(1, Logged(RequestResponseType.Request));
        Assert.Equal(1, Logged(RequestResponseType.Response));
    }

    /// <summary>What the publisher may leave to its base: each overload comes back to the one it overrides, as
    /// <see cref="Each_publish_overload_sends_one_tracked_message_through_the_client_it_wraps"/> shows.</summary>
    private static readonly Dictionary<string, string> PublisherLeavesToBase = new()
    {
        ["PublisherClient.PublishAsync(Byte[])"] = "bytes",
        ["PublisherClient.PublishAsync(ByteString)"] = "ByteString",
        ["PublisherClient.PublishAsync(IMessage)"] = "IMessage",
        ["PublisherClient.PublishAsync(String, Byte[])"] = "key, bytes",
        ["PublisherClient.PublishAsync(String, ByteString)"] = "key, ByteString",
        ["PublisherClient.PublishAsync(String, Encoding)"] = "text, text encoding",
        ["PublisherClient.PublishAsync(String, IMessage)"] = "key, IMessage",
        ["PublisherClient.PublishAsync(String, String, Encoding)"] = "key, text, encoding",
    };

    /// <summary>What the subscriber may leave to its base: StopAsync(TimeSpan) comes back to
    /// StopAsync(ShutdownOptions, CancellationToken), as TrackingSubscriberClientTests shows.</summary>
    private static readonly Dictionary<string, string> SubscriberLeavesToBase = new()
    {
        ["SubscriberClient.StopAsync(TimeSpan)"] = "Stopping_with_a_timeout_stops_the_inner_client",
    };

    [Fact]
    public void The_publisher_leaves_to_its_base_only_overloads_shown_to_come_back() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingPublisherClient), PublisherLeavesToBase);

    [Fact]
    public void The_subscriber_leaves_to_its_base_only_overloads_shown_to_come_back() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingSubscriberClient), SubscriberLeavesToBase);

    /// <summary>A publisher that records each message it is handed.</summary>
    private sealed class RecordingPublisherClient : PublisherClient
    {
        public List<PubsubMessage> Sent { get; } = [];

        public override TopicName TopicName { get; } = new("p", "my-topic");

        public override Task<string> PublishAsync(PubsubMessage message)
        {
            Sent.Add(message);
            return Task.FromResult("message-1");
        }
    }

    /// <summary>A subscriber that hands its handler one message as it starts.</summary>
    private sealed class OneMessageSubscriberClient : SubscriberClient
    {
        public override SubscriptionName SubscriptionName { get; } = new("p", "my-subscription");

        public override Task StartAsync(Func<PubsubMessage, CancellationToken, Task<Reply>> handlerAsync) =>
            handlerAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }, CancellationToken.None);
    }

    private sealed class AckingHandler : SubscriptionHandler
    {
        public override Task<SubscriberClient.Reply> HandleMessage(PubsubMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(SubscriberClient.Reply.Ack);
    }
}
