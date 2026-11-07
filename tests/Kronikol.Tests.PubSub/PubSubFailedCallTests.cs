using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Kronikol.Extensions.PubSub;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.PubSub;

/// <summary>
/// A Pub/Sub call that throws (plans/FAILED_CALLS_PLAN.md F5, F14, §5.7): a publish, a batch and a subscriber's handler are each
/// answered with the exception's type where the status would be and its message chain as the error, the message kept as the note
/// as a failure was drawn before, and the exception reaches the caller as it was thrown.
/// </summary>
public class PubSubFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private PubSubTrackingOptions Options() => new()
    {
        ServiceName = "PubSub",
        CallerName = "Caller",
        CurrentTestInfoFetcher = () => ("Orders are published", _testId),
    };

    private static InvalidOperationException Refused() =>
        new("Status(StatusCode=\"Unavailable\", Detail=\"failed to connect to all addresses\")", new IOException("Connection refused"));

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_publish_that_throws_is_answered_with_the_exceptions_type_and_its_message_kept_as_the_note()
    {
        var thrown = Refused();
        var publisher = new TrackingPublisherClient(new ThrowingPublisher(thrown), Options());

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }));

        FailedCallContract.AssertUntouched(caught, thrown);
        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(thrown.Message, answer.Content);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_batch_that_throws_is_answered_with_the_exceptions_type()
    {
        var thrown = Refused();
        var publisher = new TrackingPublisherClient(new ThrowingPublisher(thrown), Options());

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(
            [new PubsubMessage { Data = ByteString.CopyFromUtf8("a") }, new PubsubMessage { Data = ByteString.CopyFromUtf8("b") }]));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_subscribers_handler_that_throws_is_answered_where_it_left_the_receive_alone()
    {
        var thrown = new InvalidOperationException("The order could not be stored.");
        var subscriber = new TrackingSubscriberClient(new OneMessageSubscriber(), Options());

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => subscriber.StartAsync((_, _) =>
        {
            FailedCallContract.Throw(thrown);
            return Task.FromResult(SubscriberClient.Reply.Ack);
        }));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public async Task A_publish_that_throws_is_drawn_at_every_phases_verbosity_with_the_note_each_drew()
    {
        var options = Options();
        options.SetupVerbosity = PubSubTrackingVerbosity.Summarised;
        options.ActionVerbosity = PubSubTrackingVerbosity.Detailed;
        var thrown = Refused();
        var before = TestPhaseContext.Current;
        TestPhaseContext.Current = TestPhase.Unknown;
        try
        {
            var publisher = new TrackingPublisherClient(new ThrowingPublisher(thrown), options);
            await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }));
        }
        finally
        {
            TestPhaseContext.Current = before;
        }

        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Null(answer.SetupVariant!.Content);
        Assert.Equal(thrown.Message, answer.ActionVariant!.Content);
    }

    [Fact]
    public async Task History_rules_before_12_read_a_failure_as_they_did_with_no_status()
    {
        var publisher = new TrackingPublisherClient(new ThrowingPublisher(Refused()), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }));

        Assert.Equal("!InvalidOperationException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public async Task A_publish_that_succeeds_is_answered_as_before()
    {
        var publisher = new TrackingPublisherClient(new FakePublisher(), Options());

        Assert.Equal("message-1", await publisher.PublishAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }));

        var answer = Assert.Single(Logs(), l => l.Type == RequestResponseType.Response);
        Assert.Null(answer.StatusCode);
        Assert.Null(answer.Error);
    }

    private sealed class ThrowingPublisher(Exception exception) : PublisherClient
    {
        public override TopicName TopicName { get; } = new("p", "orders");

        public override Task<string> PublishAsync(PubsubMessage message)
        {
            FailedCallContract.Throw(exception);
            return Task.FromResult("never");
        }
    }

    private sealed class FakePublisher : PublisherClient
    {
        public override TopicName TopicName { get; } = new("p", "orders");

        public override Task<string> PublishAsync(PubsubMessage message) => Task.FromResult("message-1");
    }

    private sealed class OneMessageSubscriber : SubscriberClient
    {
        public override SubscriptionName SubscriptionName { get; } = new("p", "orders-sub");

        public override Task StartAsync(Func<PubsubMessage, CancellationToken, Task<Reply>> handlerAsync) =>
            handlerAsync(new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") }, CancellationToken.None);
    }
}
