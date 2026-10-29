using System.Runtime.CompilerServices;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Kronikol.Extensions.EventHubs;
using Kronikol.Tests.Packaging;
using Kronikol.Tracking;

namespace Kronikol.Tests.EventHubs;

/// <summary>
/// The tracking clients derive from the Event Hubs clients. Built through the constructors for mocking, the members they
/// cannot override, the names and <c>IsClosed</c>, read an empty client: against the emulator, a wrapper held as the
/// SDK type threw NullReferenceException for its names, had no identifier, said it was open after it was disposed, threw
/// on the <c>ReadEventsAsync</c> overload it left to its base, and drew no response for a read its caller stopped early or
/// that failed (<c>plans/CLIENT_FLOORS_PLAN.md</c> R2b). The guard facts hold the wrappers to the members known to come
/// back to an override, on whichever release the run resolves.
/// </summary>
public class TrackingEventHubsWrapperCoverageTests
{
    private const string ConnectionString =
        "Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=dGVzdA==;EntityPath=my-hub";

    private readonly string _testId = Guid.NewGuid().ToString();

    private EventHubsTrackingOptions Options(EventHubsTrackingVerbosity verbosity = EventHubsTrackingVerbosity.Detailed) => new()
    {
        ServiceName = "EventHubs",
        CallerName = "TestCaller",
        Verbosity = verbosity,
        CurrentTestInfoFetcher = () => ("My EventHubs Test", _testId),
    };

    private RequestResponseLog[] Logged(RequestResponseType type) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId && l.Type == type).ToArray();

    [Fact]
    public async Task A_producer_held_as_the_SDK_type_has_the_names_of_the_client_it_wraps()
    {
        await using var inner = new EventHubProducerClient(ConnectionString);
        EventHubProducerClient producer = new TrackingEventHubProducerClient(inner, Options());

        Assert.Equal(("fake.servicebus.windows.net", "my-hub"), (producer.FullyQualifiedNamespace, producer.EventHubName));
    }

    [Fact]
    public async Task A_consumer_held_as_the_SDK_type_has_the_names_of_the_client_it_wraps()
    {
        await using var inner = new EventHubConsumerClient("my-group", ConnectionString);
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(inner, Options());

        Assert.Equal(("my-group", "fake.servicebus.windows.net", "my-hub"),
            (consumer.ConsumerGroup, consumer.FullyQualifiedNamespace, consumer.EventHubName));
    }

    [Fact]
    public async Task A_producer_held_as_the_SDK_type_is_closed_when_disposed_and_so_is_the_client_it_wraps()
    {
        var inner = new EventHubProducerClient(ConnectionString);
        EventHubProducerClient producer = new TrackingEventHubProducerClient(inner, Options());

        await producer.DisposeAsync();

        Assert.Equal((true, true), (producer.IsClosed, inner.IsClosed));
    }

    [Fact]
    public async Task A_consumer_held_as_the_SDK_type_is_closed_when_disposed_and_so_is_the_client_it_wraps()
    {
        var inner = new EventHubConsumerClient("my-group", ConnectionString);
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(inner, Options());

        await consumer.DisposeAsync();

        Assert.Equal((true, true), (consumer.IsClosed, inner.IsClosed));
    }

    [Fact]
    public async Task A_producer_held_as_the_SDK_type_has_the_identifier_of_the_client_it_wraps()
    {
        await using var inner = new EventHubProducerClient(ConnectionString, new EventHubProducerClientOptions { Identifier = "my-producer" });
        EventHubProducerClient producer = new TrackingEventHubProducerClient(inner, Options());

        Assert.Equal("my-producer", producer.Identifier);
    }

    [Fact]
    public async Task A_consumer_held_as_the_SDK_type_has_the_identifier_of_the_client_it_wraps()
    {
        await using var inner = new EventHubConsumerClient("my-group", ConnectionString, new EventHubConsumerClientOptions { Identifier = "my-consumer" });
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(inner, Options());

        Assert.Equal("my-consumer", consumer.Identifier);
    }

    [Fact]
    public async Task A_producer_wrapping_a_client_built_for_mocking_is_built_and_named_unknown_as_the_SDK_type()
    {
        EventHubProducerClient producer = new TrackingEventHubProducerClient(new PartitionsOnlyProducer(), Options());

        Assert.Equal(("unknown", "unknown"), (producer.FullyQualifiedNamespace, producer.EventHubName));
        Assert.Equal(["0", "1"], await producer.GetPartitionIdsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_consumer_wrapping_a_client_built_for_mocking_is_built_and_named_unknown_as_the_SDK_type()
    {
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new PartitionsOnlyConsumer(), Options());

        Assert.Equal(("unknown", "unknown", "unknown"), (consumer.ConsumerGroup, consumer.FullyQualifiedNamespace, consumer.EventHubName));
        Assert.Equal(["0", "1"], await consumer.GetPartitionIdsAsync(TestContext.Current.CancellationToken));
    }

    public static TheoryData<string> ReadOverloads => ["now", "now, options", "earliest, options", "partition", "partition, options"];

    private static IAsyncEnumerable<PartitionEvent> Read(EventHubConsumerClient consumer, string overload, CancellationToken cancellationToken) =>
        overload switch
        {
            "now" => consumer.ReadEventsAsync(cancellationToken),
            "now, options" => consumer.ReadEventsAsync(new ReadEventOptions(), cancellationToken),
            "earliest, options" => consumer.ReadEventsAsync(true, new ReadEventOptions(), cancellationToken),
            "partition" => consumer.ReadEventsFromPartitionAsync("0", EventPosition.Earliest, cancellationToken),
            "partition, options" => consumer.ReadEventsFromPartitionAsync("0", EventPosition.Earliest, new ReadEventOptions(), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(overload)),
        };

    [Theory]
    [MemberData(nameof(ReadOverloads))]
    public async Task Each_read_reads_through_the_same_overload_of_the_client_it_wraps_and_is_drawn(string overload)
    {
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new OneEventConsumer(), Options());

        var bodies = new List<string>();
        await foreach (var partitionEvent in Read(consumer, overload, TestContext.Current.CancellationToken))
            bodies.Add(partitionEvent.Data.EventBody.ToString());

        Assert.Equal([overload], bodies);
        Assert.Single(Logged(RequestResponseType.Request));
        Assert.Single(Logged(RequestResponseType.Response));
    }

    [Theory]
    [MemberData(nameof(ReadOverloads))]
    public async Task Each_read_its_caller_stops_after_one_event_is_drawn_with_its_response(string overload)
    {
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new OneEventConsumer(), Options());

        await foreach (var _ in Read(consumer, overload, TestContext.Current.CancellationToken))
            break;

        Assert.Single(Logged(RequestResponseType.Request));
        Assert.Single(Logged(RequestResponseType.Response));
    }

    [Theory]
    [MemberData(nameof(ReadOverloads))]
    public async Task Each_read_that_fails_is_drawn_with_the_failure_as_its_response(string overload)
    {
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new OneEventConsumer(new InvalidOperationException("the hub went away")), Options());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in Read(consumer, overload, TestContext.Current.CancellationToken)) { }
        });

        Assert.Single(Logged(RequestResponseType.Request));
        Assert.Equal("the hub went away", Assert.Single(Logged(RequestResponseType.Response)).Content);
    }

    [Theory]
    [MemberData(nameof(ReadOverloads))]
    public async Task Each_read_shows_at_Raw_how_many_events_it_read(string overload)
    {
        EventHubConsumerClient consumer = new TrackingEventHubConsumerClient(new OneEventConsumer(), Options(EventHubsTrackingVerbosity.Raw));

        await foreach (var _ in Read(consumer, overload, TestContext.Current.CancellationToken)) { }

        Assert.Equal("Events: 1", Assert.Single(Logged(RequestResponseType.Response)).Content);
    }

    /// <summary>What each client may leave to its base: its DisposeAsync comes back to the CloseAsync it overrides, as
    /// the two disposal facts show.</summary>
    private static readonly Dictionary<string, string> ProducerLeavesToBase = new()
    {
        ["EventHubProducerClient.DisposeAsync()"] = nameof(A_producer_held_as_the_SDK_type_is_closed_when_disposed_and_so_is_the_client_it_wraps),
    };

    private static readonly Dictionary<string, string> ConsumerLeavesToBase = new()
    {
        ["EventHubConsumerClient.DisposeAsync()"] = nameof(A_consumer_held_as_the_SDK_type_is_closed_when_disposed_and_so_is_the_client_it_wraps),
    };

    [Fact]
    public void The_producer_leaves_to_its_base_only_what_comes_back_to_an_override() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingEventHubProducerClient), ProducerLeavesToBase);

    [Fact]
    public void The_consumer_leaves_to_its_base_only_what_comes_back_to_an_override() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingEventHubConsumerClient), ConsumerLeavesToBase);

    /// <summary>A consumer whose every read yields one event, its body the overload read, then fails if it was given a
    /// failure.</summary>
    private sealed class OneEventConsumer(Exception? failure = null) : EventHubConsumerClient("my-group", ConnectionString)
    {
        public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
            One("now");

        public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(ReadEventOptions readOptions, CancellationToken cancellationToken = default) =>
            One("now, options");

        public override IAsyncEnumerable<PartitionEvent> ReadEventsAsync(bool startReadingAtEarliestEvent, ReadEventOptions? readOptions = null,
            CancellationToken cancellationToken = default) =>
            One(startReadingAtEarliestEvent ? "earliest, options" : "latest, options");

        public override IAsyncEnumerable<PartitionEvent> ReadEventsFromPartitionAsync(string partitionId, EventPosition startingPosition,
            CancellationToken cancellationToken = default) =>
            One("partition");

        public override IAsyncEnumerable<PartitionEvent> ReadEventsFromPartitionAsync(string partitionId, EventPosition startingPosition,
            ReadEventOptions readOptions, CancellationToken cancellationToken = default) =>
            One("partition, options");

        private async IAsyncEnumerable<PartitionEvent> One(string body, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new PartitionEvent(EventHubsModelFactory.PartitionContext("0", default(LastEnqueuedEventProperties)), new EventData(body));
            if (failure is not null)
                throw failure;
        }
    }

    /// <summary>A producer built through the constructor for mocking, as a mocking library builds one: its names read a
    /// connection it does not have.</summary>
    private sealed class PartitionsOnlyProducer : EventHubProducerClient
    {
        public override Task<string[]> GetPartitionIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string[]>(["0", "1"]);
    }

    /// <summary>A consumer built through the constructor for mocking, with no consumer group.</summary>
    private sealed class PartitionsOnlyConsumer : EventHubConsumerClient
    {
        public override Task<string[]> GetPartitionIdsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string[]>(["0", "1"]);
    }
}
