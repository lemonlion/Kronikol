using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Kronikol.Extensions.EventHubs;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.EventHubs;

/// <summary>
/// An Event Hubs call that throws (plans/FAILED_CALLS_PLAN.md F6, F14, §5.7): a send, a read that fails as it reads, and a read
/// whose reader cannot be made are each answered with the exception's type where the status would be and its message chain as the
/// error, the message kept as the note as a failure was drawn before, and the exception reaches the caller as it was thrown.
/// </summary>
public class EventHubsFailedCallTests
{
    private const string ConnectionString =
        "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=orders";

    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private EventHubsTrackingOptions Options() => new()
    {
        ServiceName = "EventHubs",
        CallerName = "Caller",
        CurrentTestInfoFetcher = () => ("Orders are streamed", _testId),
    };

    private static EventHubsException Unreachable() =>
        new(isTransient: true, "orders", "The connection to the hub was refused.", EventHubsException.FailureReason.ServiceCommunicationProblem);

    public static TheoryData<string> Sends() => ["events", "events with options", "batch"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Sends))]
    public async Task A_send_that_throws_is_answered_with_the_exceptions_type_and_its_message_kept_as_the_note(string overload)
    {
        var thrown = Unreachable();
        EventHubProducerClient producer = new TrackingEventHubProducerClient(new ThrowingProducer(thrown), Options());

        var caught = await Assert.ThrowsAsync<EventHubsException>(() => overload switch
        {
            "events" => producer.SendAsync([new EventData("hello")], TestContext.Current.CancellationToken),
            "events with options" => producer.SendAsync([new EventData("hello")], new SendEventOptions { PartitionId = "0" }, TestContext.Current.CancellationToken),
            _ => producer.SendAsync(EventHubsModelFactory.EventDataBatch(1024, []), TestContext.Current.CancellationToken),
        });

        FailedCallContract.AssertUntouched(caught, thrown);
        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(thrown.Message, answer.Content);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_read_that_fails_as_it_reads_is_answered_with_the_exceptions_type()
    {
        var thrown = Unreachable();
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new FailingConsumer(thrown, beforeItReads: false), Options());

        var caught = await Assert.ThrowsAsync<EventHubsException>(async () =>
        {
            await foreach (var _ in consumer.ReadEventsAsync(TestContext.Current.CancellationToken)) { }
        });

        FailedCallContract.AssertUntouched(caught, thrown);
        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(thrown.Message, answer.Content);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_read_whose_reader_cannot_be_made_is_answered_where_it_left_the_request_alone()
    {
        var thrown = new ObjectDisposedException("EventHubConsumerClient", "The consumer has been closed.");
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new FailingConsumer(thrown, beforeItReads: true), Options());

        var caught = await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await foreach (var _ in consumer.ReadEventsAsync(TestContext.Current.CancellationToken)) { }
        });

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public async Task History_rules_before_12_read_a_failure_as_they_did_with_no_status()
    {
        EventHubProducerClient producer = new TrackingEventHubProducerClient(new ThrowingProducer(Unreachable()), Options());

        await Assert.ThrowsAsync<EventHubsException>(() => producer.SendAsync([new EventData("hello")], TestContext.Current.CancellationToken));

        Assert.Equal("!EventHubsException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    private sealed class ThrowingProducer(Exception exception) : EventHubProducerClient(ConnectionString)
    {
        public override Task SendAsync(IEnumerable<EventData> eventBatch, CancellationToken cancellationToken = default) => Fail();

        public override Task SendAsync(IEnumerable<EventData> eventBatch, SendEventOptions sendOptions, CancellationToken cancellationToken = default) => Fail();

        public override Task SendAsync(EventDataBatch eventBatch, CancellationToken cancellationToken = default) => Fail();

        private async Task Fail()
        {
            await Task.Yield();
            FailedCallContract.Throw(exception);
        }
    }

    /// <summary>A consumer whose read fails: as it reads, or, as a closed client's does, before it has a reader to read with.</summary>
    private sealed class FailingConsumer(Exception exception, bool beforeItReads) : EventHubConsumerClient("$Default", ConnectionString)
    {
        public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default)
        {
            if (beforeItReads)
                FailedCallContract.Throw(exception);
            return Failing();
        }

        private async IAsyncEnumerable<PartitionEvent> Failing()
        {
            await Task.Yield();
            FailedCallContract.Throw(exception);
            yield break;
        }
    }
}
