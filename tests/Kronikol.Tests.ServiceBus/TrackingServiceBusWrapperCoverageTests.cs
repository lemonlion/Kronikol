using Azure.Messaging.ServiceBus;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Tests.Packaging;
using Kronikol.Tracking;

namespace Kronikol.Tests.ServiceBus;

/// <summary>
/// The tracking clients derive from the Service Bus clients through their constructors for mocking, so a member they
/// leave to the base runs against an empty client: against the emulator, CreateProcessor(String), CreateRuleManager and
/// the dead-letter overload with a reason and properties threw NullReferenceException through a wrapper held as the
/// SDK type, and a batch send and a dead-letter with properties went through undrawn (<c>plans/CLIENT_FLOORS_PLAN.md</c>
/// R2b). Each of these reaches the client the wrapper holds, and the guard facts hold the wrappers to the members known
/// to come back, on whichever release the run resolves.
/// </summary>
[Collection("TrackingComponentRegistry")]
public class TrackingServiceBusWrapperCoverageTests : IDisposable
{
    private const string FakeConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=key;SharedAccessKey=dGVzdA==";

    private readonly string _testId = Guid.NewGuid().ToString();
    private readonly ServiceBusClient _realClient = new(FakeConnectionString);

    public TrackingServiceBusWrapperCoverageTests() => TrackingComponentRegistry.Clear();

    public void Dispose()
    {
        _realClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        TrackingComponentRegistry.Clear();
    }

    private ServiceBusTrackingOptions Options() => new()
    {
        ServiceName = "TestBus",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("Test", _testId),
    };

    private RequestResponseLog[] Logged(RequestResponseType type) =>
        RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId && l.Type == type).ToArray();

    private ServiceBusClient Client() => new TrackingServiceBusClient(_realClient, Options());

    [Fact]
    public async Task A_processor_for_a_queue_comes_from_the_client_the_wrapper_holds()
    {
        await using var processor = Client().CreateProcessor("orders");

        Assert.Equal("orders", processor.EntityPath);
    }

    [Fact]
    public async Task A_processor_for_a_subscription_comes_from_the_client_the_wrapper_holds()
    {
        await using var processor = Client().CreateProcessor("orders", "billing");

        Assert.Equal("orders/Subscriptions/billing", processor.EntityPath);
    }

    [Fact]
    public async Task A_rule_manager_comes_from_the_client_the_wrapper_holds()
    {
        await using var rules = Client().CreateRuleManager("orders", "billing");

        Assert.Equal("orders/Subscriptions/billing", rules.SubscriptionPath);
    }

    [Fact]
    public async Task Disposing_the_sender_disposes_the_sender_it_holds()
    {
        var inner = new RecordingSender();
        ServiceBusSender sender = new TrackingServiceBusSender(inner, new ServiceBusTracker(Options()), Options());

        await sender.DisposeAsync();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task Disposing_the_receiver_disposes_the_receiver_it_holds()
    {
        var inner = new RecordingReceiver();
        ServiceBusReceiver receiver = new TrackingServiceBusReceiver(inner, new ServiceBusTracker(Options()), Options());

        await receiver.DisposeAsync();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task A_batch_send_is_drawn_as_a_send_of_its_messages()
    {
        var inner = new RecordingSender();
        ServiceBusSender sender = new TrackingServiceBusSender(inner, new ServiceBusTracker(Options()), Options());
        var store = new List<ServiceBusMessage>();
        using var batch = ServiceBusModelFactory.ServiceBusMessageBatch(1024, store);
        batch.TryAddMessage(new ServiceBusMessage("one"));
        batch.TryAddMessage(new ServiceBusMessage("two"));

        await sender.SendMessagesAsync(batch);

        Assert.Same(batch, inner.SentBatch);
        var request = Assert.Single(Logged(RequestResponseType.Request));
        Assert.Contains("2", request.Method.Value?.ToString());
        Assert.Single(Logged(RequestResponseType.Response));
    }

    [Fact]
    public async Task A_dead_letter_with_properties_is_drawn_as_a_dead_letter()
    {
        var inner = new RecordingReceiver();
        ServiceBusReceiver receiver = new TrackingServiceBusReceiver(inner, new ServiceBusTracker(Options()), Options());
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "m1");

        await receiver.DeadLetterMessageAsync(message, new Dictionary<string, object> { ["why"] = "test" });

        Assert.Equal(["properties"], inner.DeadLetters);
        Assert.Single(Logged(RequestResponseType.Request));
        Assert.Single(Logged(RequestResponseType.Response));
    }

    [Fact]
    public async Task A_dead_letter_with_properties_and_a_reason_is_drawn_and_reaches_the_receiver_it_holds()
    {
        var inner = new RecordingReceiver();
        ServiceBusReceiver receiver = new TrackingServiceBusReceiver(inner, new ServiceBusTracker(Options()), Options());
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "m1");

        await receiver.DeadLetterMessageAsync(message, new Dictionary<string, object> { ["why"] = "test" }, "a reason", "a description");

        Assert.Equal(["properties, a reason"], inner.DeadLetters);
        Assert.Single(Logged(RequestResponseType.Request));
        Assert.Single(Logged(RequestResponseType.Response));
    }

    [Fact]
    public async Task Receiving_as_a_stream_comes_back_to_the_tracked_receive()
    {
        var inner = new RecordingReceiver();
        ServiceBusReceiver receiver = new TrackingServiceBusReceiver(inner, new ServiceBusTracker(Options()), Options());
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await foreach (var message in receiver.ReceiveMessagesAsync(cancel.Token))
        {
            Assert.Equal("m1", message.MessageId);
            break;
        }

        Assert.NotEmpty(Logged(RequestResponseType.Request));
    }

    /// <summary>What the client may leave to its base, and why.</summary>
    private static readonly Dictionary<string, string> ClientLeavesToBase = new()
    {
        ["ServiceBusClient.GetMessageSessionsAsync(String, CancellationToken)"] = "new in 7.21.0, past the floor: call it on Inner",
        ["ServiceBusClient.GetMessageSessionsAsync(String, DateTimeOffset, CancellationToken)"] = "new in 7.21.0, past the floor: call it on Inner",
        ["ServiceBusClient.GetMessageSessionsAsync(String, String, CancellationToken)"] = "new in 7.21.0, past the floor: call it on Inner",
        ["ServiceBusClient.GetMessageSessionsAsync(String, String, DateTimeOffset, CancellationToken)"] = "new in 7.21.0, past the floor: call it on Inner",
    };

    /// <summary>What the receiver may leave to its base, and the test that shows it coming back.</summary>
    private static readonly Dictionary<string, string> ReceiverLeavesToBase = new()
    {
        ["ServiceBusReceiver.ReceiveMessagesAsync(CancellationToken)"] = nameof(Receiving_as_a_stream_comes_back_to_the_tracked_receive),
    };

    [Fact]
    public void The_client_leaves_to_its_base_only_the_members_this_class_names() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingServiceBusClient), ClientLeavesToBase);

    [Fact]
    public void The_sender_leaves_nothing_to_its_base() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingServiceBusSender), new Dictionary<string, string>());

    [Fact]
    public void The_receiver_leaves_to_its_base_only_the_members_this_class_names() =>
        WrapperCoverage.AssertLeavesOnly(typeof(TrackingServiceBusReceiver), ReceiverLeavesToBase);

    /// <summary>A sender that records the batch it is handed and whether it was disposed.</summary>
    private sealed class RecordingSender : ServiceBusSender
    {
        public ServiceBusMessageBatch? SentBatch { get; private set; }
        public bool Disposed { get; private set; }

        public override string EntityPath => "orders";
        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override Task SendMessagesAsync(ServiceBusMessageBatch messageBatch, CancellationToken cancellationToken = default)
        {
            SentBatch = messageBatch;
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A receiver that hands out one message and records each dead-letter and whether it was disposed.</summary>
    private sealed class RecordingReceiver : ServiceBusReceiver
    {
        public List<string> DeadLetters { get; } = [];
        public bool Disposed { get; private set; }

        public override string EntityPath => "orders";
        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
            int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>([ServiceBusModelFactory.ServiceBusReceivedMessage(messageId: "m1")]);

        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, IDictionary<string, object>? propertiesToModify = null,
            CancellationToken cancellationToken = default)
        {
            DeadLetters.Add("properties");
            return Task.CompletedTask;
        }

        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, IDictionary<string, object> propertiesToModify,
            string deadLetterReason, string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        {
            DeadLetters.Add($"properties, {deadLetterReason}");
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
