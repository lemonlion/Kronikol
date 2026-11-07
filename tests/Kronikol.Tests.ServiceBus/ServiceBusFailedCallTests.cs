using Azure.Messaging.ServiceBus;
using Kronikol.Extensions.ServiceBus;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.ServiceBus;

/// <summary>
/// A Service Bus call that throws (plans/FAILED_CALLS_PLAN.md F14, §5.7): a send and a receive are each answered with the
/// exception's type where the status would be and its message chain as the error, the message kept as the note as a failure was
/// drawn before, and the exception reaches the caller as it was thrown.
/// </summary>
public class ServiceBusFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private ServiceBusTrackingOptions Options() => new()
    {
        ServiceName = "ServiceBus",
        CallerName = "Caller",
        CurrentTestInfoFetcher = () => ("Orders are queued", _testId),
    };

    private static ServiceBusException Unreachable() =>
        new("The connection to the namespace was refused.", ServiceBusFailureReason.ServiceCommunicationProblem, "orders");

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_send_that_throws_is_answered_with_the_exceptions_type_and_its_message_kept_as_the_note()
    {
        var thrown = Unreachable();
        var sender = new TrackingServiceBusSender(new ThrowingSender(thrown), new ServiceBusTracker(Options()), Options());

        var caught = await Assert.ThrowsAsync<ServiceBusException>(() => sender.SendMessageAsync(new ServiceBusMessage("hello"), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        var (_, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(thrown.Message, answer.Content);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_receive_that_throws_is_answered_with_the_exceptions_type()
    {
        var thrown = Unreachable();
        var receiver = new TrackingServiceBusReceiver(new ThrowingReceiver(thrown), new ServiceBusTracker(Options()), Options());

        var caught = await Assert.ThrowsAsync<ServiceBusException>(() => receiver.ReceiveMessagesAsync(5, null, TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public async Task History_rules_before_12_read_a_failure_as_they_did_with_no_status()
    {
        var sender = new TrackingServiceBusSender(new ThrowingSender(Unreachable()), new ServiceBusTracker(Options()), Options());

        await Assert.ThrowsAsync<ServiceBusException>(() => sender.SendMessageAsync(new ServiceBusMessage("hello"), TestContext.Current.CancellationToken));

        Assert.Equal("!ServiceBusException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    private sealed class ThrowingSender(Exception exception) : ServiceBusSender
    {
        public override string EntityPath => "orders";
        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override async Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            FailedCallContract.Throw(exception);
        }
    }

    private sealed class ThrowingReceiver(Exception exception) : ServiceBusReceiver
    {
        public override string EntityPath => "orders";
        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override async Task<IReadOnlyList<ServiceBusReceivedMessage>> ReceiveMessagesAsync(
            int maxMessages, TimeSpan? maxWaitTime = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            FailedCallContract.Throw(exception);
            return [];
        }
    }
}
