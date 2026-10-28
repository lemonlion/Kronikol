using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Kronikol.Extensions.EventHubs;
using Kronikol.Tracking;

namespace Kronikol.Tests.EventHubs;

/// <summary>
/// A phase's own verbosity decides what the producer and consumer record. Their content was made at the handler's base
/// <see cref="EventHubsTrackingOptions.Verbosity"/>, so a Detailed phase of a Summarised handler had no events, a Raw
/// phase of a Detailed one no event count, and a Detailed phase of a Raw one the count a Detailed handler leaves out.
/// </summary>
public class MessageContentTests
{
    private const string FakeConnectionString =
        "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=my-hub";

    private readonly string _testId = Guid.NewGuid().ToString();

    private EventHubsTrackingOptions MakeOptions(EventHubsTrackingVerbosity verbosity, EventHubsTrackingVerbosity actionVerbosity) => new()
    {
        ServiceName = "EventHubs",
        CallerName = "TestCaller",
        Verbosity = verbosity,
        ActionVerbosity = actionVerbosity,
        CurrentTestInfoFetcher = () => ("My EventHubs Test", _testId),
    };

    private RequestResponseLog Logged(RequestResponseType type) =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == type);

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_producer_shows_the_events()
    {
        TestPhaseContext.Reset();
        var producer = new TrackingEventHubProducerClient(new FakeProducer(),
            MakeOptions(EventHubsTrackingVerbosity.Summarised, EventHubsTrackingVerbosity.Detailed));

        await producer.SendAsync([new EventData("hello")], TestContext.Current.CancellationToken);

        var request = Logged(RequestResponseType.Request);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("hello", request.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_send_in_a_detailed_action_phase_of_a_summarised_producer_shows_the_events()
    {
        var producer = new TrackingEventHubProducerClient(new FakeProducer(),
            MakeOptions(EventHubsTrackingVerbosity.Summarised, EventHubsTrackingVerbosity.Detailed));

        TestPhaseContext.Current = TestPhase.Action;
        try
        {
            await producer.SendAsync([new EventData("hello")], TestContext.Current.CancellationToken);
        }
        finally
        {
            TestPhaseContext.Reset();
        }

        Assert.Equal("hello", Logged(RequestResponseType.Request).Content);
    }

    [Fact]
    public async Task A_raw_phase_of_a_detailed_consumer_shows_how_many_events_it_read()
    {
        TestPhaseContext.Reset();
        var consumer = new TrackingEventHubConsumerClient(new FakeConsumer(),
            MakeOptions(EventHubsTrackingVerbosity.Detailed, EventHubsTrackingVerbosity.Raw));

        await foreach (var _ in consumer.ReadEventsAsync(TestContext.Current.CancellationToken)) { }

        var response = Logged(RequestResponseType.Response);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("Events: 2", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_raw_consumer_leaves_out_how_many_events_it_read()
    {
        TestPhaseContext.Reset();
        var consumer = new TrackingEventHubConsumerClient(new FakeConsumer(),
            MakeOptions(EventHubsTrackingVerbosity.Raw, EventHubsTrackingVerbosity.Detailed));

        await foreach (var _ in consumer.ReadEventsAsync(new ReadEventOptions(), TestContext.Current.CancellationToken)) { }

        var response = Logged(RequestResponseType.Response);
        Assert.Equal("Events: 2", response.Content);
        Assert.Equal("Events: 2", response.SetupVariant!.Content);
        Assert.Null(response.ActionVariant!.Content);
    }

    private sealed class FakeProducer() : EventHubProducerClient(FakeConnectionString)
    {
        public override Task SendAsync(IEnumerable<EventData> eventBatch, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeConsumer() : EventHubConsumerClient(EventHubConsumerClient.DefaultConsumerGroupName, FakeConnectionString)
    {
        public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default)
            => Two();

        public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(ReadEventOptions readOptions, CancellationToken cancellationToken = default)
            => Two();

        private static async IAsyncEnumerable<PartitionEvent> Two()
        {
            await Task.Yield();
            yield return new PartitionEvent();
            yield return new PartitionEvent();
        }
    }
}
