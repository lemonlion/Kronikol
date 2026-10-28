using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Kronikol.Extensions.PubSub;
using Kronikol.Tracking;

namespace Kronikol.Tests.PubSub;

/// <summary>
/// A phase's own verbosity decides what the publisher and subscriber record. Their content was made at the handler's
/// base <see cref="PubSubTrackingOptions.Verbosity"/>, so a Detailed phase of a Summarised handler had no message, a Raw
/// phase of a Detailed one no message id, batch or reply, and a Detailed phase of a Raw one the message id a Detailed
/// handler leaves out.
/// </summary>
public class MessageContentTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private PubSubTrackingOptions MakeOptions(PubSubTrackingVerbosity verbosity, PubSubTrackingVerbosity actionVerbosity) => new()
    {
        ServiceName = "PubSub",
        CallerName = "TestCaller",
        Verbosity = verbosity,
        ActionVerbosity = actionVerbosity,
        CurrentTestInfoFetcher = () => ("My PubSub Test", _testId),
    };

    private RequestResponseLog Logged(RequestResponseType type) =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == type);

    private static PubsubMessage Message(string data) => new() { Data = ByteString.CopyFromUtf8(data) };

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_publisher_shows_the_message()
    {
        TestPhaseContext.Reset();
        var publisher = new TrackingPublisherClient(new FakePublisherClient(),
            MakeOptions(PubSubTrackingVerbosity.Summarised, PubSubTrackingVerbosity.Detailed));

        await publisher.PublishAsync(Message("hello"));

        var request = Logged(RequestResponseType.Request);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("hello", request.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_publish_in_a_detailed_action_phase_of_a_summarised_publisher_shows_the_message()
    {
        var publisher = new TrackingPublisherClient(new FakePublisherClient(),
            MakeOptions(PubSubTrackingVerbosity.Summarised, PubSubTrackingVerbosity.Detailed));

        TestPhaseContext.Current = TestPhase.Action;
        try
        {
            await publisher.PublishAsync(Message("hello"));
        }
        finally
        {
            TestPhaseContext.Reset();
        }

        Assert.Equal("hello", Logged(RequestResponseType.Request).Content);
    }

    [Fact]
    public async Task A_raw_phase_of_a_detailed_publisher_shows_the_message_id()
    {
        TestPhaseContext.Reset();
        var publisher = new TrackingPublisherClient(new FakePublisherClient(),
            MakeOptions(PubSubTrackingVerbosity.Detailed, PubSubTrackingVerbosity.Raw));

        await publisher.PublishAsync(Message("hello"));

        var response = Logged(RequestResponseType.Response);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("MessageId: message-1", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_raw_publisher_leaves_out_the_message_id()
    {
        TestPhaseContext.Reset();
        var publisher = new TrackingPublisherClient(new FakePublisherClient(),
            MakeOptions(PubSubTrackingVerbosity.Raw, PubSubTrackingVerbosity.Detailed));

        await publisher.PublishAsync(Message("hello"));

        var response = Logged(RequestResponseType.Response);
        Assert.Equal("MessageId: message-1", response.Content);
        Assert.Equal("MessageId: message-1", response.SetupVariant!.Content);
        Assert.Null(response.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_raw_phase_of_a_detailed_publisher_shows_a_batch_s_messages()
    {
        TestPhaseContext.Reset();
        var publisher = new TrackingPublisherClient(new FakePublisherClient(),
            MakeOptions(PubSubTrackingVerbosity.Detailed, PubSubTrackingVerbosity.Raw));

        await publisher.PublishAsync([Message("a"), Message("b")]);

        var request = Logged(RequestResponseType.Request);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("a, b", request.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_raw_phase_of_a_summarised_subscriber_shows_the_message_and_its_reply()
    {
        TestPhaseContext.Reset();
        var subscriber = new TrackingSubscriberClient(new FakeSubscriberClient(),
            MakeOptions(PubSubTrackingVerbosity.Summarised, PubSubTrackingVerbosity.Raw));

        await subscriber.StartAsync((_, _) => Task.FromResult(SubscriberClient.Reply.Ack));

        var request = Logged(RequestResponseType.Request);
        var response = Logged(RequestResponseType.Response);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("hello", request.ActionVariant!.Content);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("Ack", response.ActionVariant!.Content);
    }

    private sealed class FakePublisherClient : PublisherClient
    {
        public override TopicName TopicName { get; } = new("p", "my-topic");

        public override Task<string> PublishAsync(PubsubMessage message) => Task.FromResult("message-1");
    }

    /// <summary>A subscriber that hands its handler one message as it starts.</summary>
    private sealed class FakeSubscriberClient : SubscriberClient
    {
        public override SubscriptionName SubscriptionName { get; } = new("p", "my-subscription");

        public override Task StartAsync(Func<PubsubMessage, CancellationToken, Task<Reply>> handlerAsync) =>
            handlerAsync(Message("hello"), CancellationToken.None);
    }
}
