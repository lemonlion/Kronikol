using Azure.Messaging.ServiceBus;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Tracking;

namespace Kronikol.Tests.ServiceBus;

/// <summary>
/// A phase's own verbosity decides what the sender and receiver record of a message. The content was read only when the
/// handler's base <see cref="ServiceBusTrackingOptions.Verbosity"/> showed it, so a Detailed phase of a Summarised handler
/// had none, whether the call was made in that phase or before any phase was known.
/// </summary>
[Collection("TrackingComponentRegistry")]
public class MessageContentTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private ServiceBusTrackingOptions MakeOptions() => new()
    {
        ServiceName = "TestBus",
        CallerName = "TestCaller",
        Verbosity = ServiceBusTrackingVerbosity.Summarised,
        ActionVerbosity = ServiceBusTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = () => ("My Service Bus Test", _testId),
    };

    private RequestResponseLog Logged(RequestResponseType type) =>
        Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == _testId && l.Type == type);

    private TrackingServiceBusSender Sender()
    {
        var options = MakeOptions();
        return new TrackingServiceBusSender(new FakeSender(), new ServiceBusTracker(options), options);
    }

    private TrackingServiceBusReceiver Receiver()
    {
        var options = MakeOptions();
        return new TrackingServiceBusReceiver(new FakeReceiver(), new ServiceBusTracker(options), options);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_sender_shows_the_message()
    {
        TestPhaseContext.Reset();

        await Sender().SendMessageAsync(new ServiceBusMessage("hello"), TestContext.Current.CancellationToken);

        var request = Logged(RequestResponseType.Request);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("hello", request.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_send_in_a_detailed_action_phase_of_a_summarised_sender_shows_the_message()
    {
        TestPhaseContext.Current = TestPhase.Action;
        try
        {
            await Sender().SendMessageAsync(new ServiceBusMessage("hello"), TestContext.Current.CancellationToken);
        }
        finally
        {
            TestPhaseContext.Reset();
        }

        Assert.Equal("hello", Logged(RequestResponseType.Request).Content);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_sender_shows_a_batch_s_count()
    {
        TestPhaseContext.Reset();

        await Sender().SendMessagesAsync([new ServiceBusMessage("a"), new ServiceBusMessage("b")], TestContext.Current.CancellationToken);

        var request = Logged(RequestResponseType.Request);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("[2 messages]", request.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_receiver_shows_the_message()
    {
        TestPhaseContext.Reset();

        await Receiver().ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);

        var response = Logged(RequestResponseType.Response);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("hello", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_receiver_shows_how_many_messages_it_received()
    {
        TestPhaseContext.Reset();

        await Receiver().ReceiveMessagesAsync(2, cancellationToken: TestContext.Current.CancellationToken);

        var response = Logged(RequestResponseType.Response);
        Assert.Null(response.Content);
        Assert.Null(response.SetupVariant!.Content);
        Assert.Equal("[2 messages received]", response.ActionVariant!.Content);
    }

    [Fact]
    public async Task A_detailed_phase_of_a_summarised_receiver_shows_why_it_dead_lettered_a_message()
    {
        TestPhaseContext.Reset();

        await Receiver().DeadLetterMessageAsync(FakeReceiver.Message(), "expired", cancellationToken: TestContext.Current.CancellationToken);

        var request = Logged(RequestResponseType.Request);
        Assert.Null(request.Content);
        Assert.Null(request.SetupVariant!.Content);
        Assert.Equal("Reason: expired", request.ActionVariant!.Content);
    }

    private sealed class FakeSender : ServiceBusSender
    {
        public override string EntityPath => "orders";

        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override Task SendMessagesAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeReceiver : ServiceBusReceiver
    {
        public static ServiceBusReceivedMessage Message() =>
            ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString("hello"), messageId: "m1");

        public override string EntityPath => "orders";

        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override Task<ServiceBusReceivedMessage?> ReceiveMessageAsync(
            TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
            => Task.FromResult<ServiceBusReceivedMessage?>(Message());

        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
            int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>([Message(), Message()]);

        public override Task DeadLetterMessageAsync(
            ServiceBusReceivedMessage message, string? deadLetterReason = null, string? deadLetterErrorDescription = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
