using Azure.Messaging.ServiceBus;
using Kronikol.Constants;
using Kronikol.Extensions.ServiceBus;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.ServiceBus;

/// <summary>
/// A message sent while a host serves a request is logged under the request's scenario, and carries that scenario, so
/// the receiver that handles it lands in the same one.
/// </summary>
[Collection("TrackingComponentRegistry")]
public class TrackingServiceBusSenderIdentityTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private ServiceBusTrackingOptions MakeOptions() => new()
    {
        ServiceName = "TestBus",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("My Service Bus Test", _testId),
    };

    [Fact]
    public async Task SendMessageAsync_stamps_the_scenario_the_send_is_logged_under_when_the_tracker_was_given_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        var tracker = new ServiceBusTracker(options, RequestHeaderAccessor.For("Header Test", headerTestId));
        var sender = new TrackingServiceBusSender(new FakeSender(), tracker, options);
        var message = new ServiceBusMessage("hello");

        await sender.SendMessageAsync(message, TestContext.Current.CancellationToken);

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
        Assert.Equal("Header Test", message.ApplicationProperties[TestTrackingMessageHeaders.TestName]);
        Assert.Equal(headerTestId, message.ApplicationProperties[TestTrackingMessageHeaders.TestId]);
    }

    [Fact]
    public async Task SendMessageAsync_stamps_the_requests_scenario_when_the_options_carry_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var sender = new TrackingServiceBusSender(new FakeSender(), new ServiceBusTracker(options), options);
        var message = new ServiceBusMessage("hello");

        await sender.SendMessageAsync(message, TestContext.Current.CancellationToken);

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
        Assert.Equal(headerTestId, message.ApplicationProperties[TestTrackingMessageHeaders.TestId]);
    }

    private sealed class FakeSender : ServiceBusSender
    {
        public override string EntityPath => "orders";

        public override string FullyQualifiedNamespace => "test.servicebus.windows.net";

        public override Task SendMessageAsync(ServiceBusMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
