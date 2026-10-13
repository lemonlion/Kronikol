using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Kronikol.Constants;
using Kronikol.Extensions.PubSub;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.PubSub;

/// <summary>
/// A publish made while a host serves a request is logged under the request's scenario, and the message it sends
/// carries that scenario, so the subscriber that handles it lands in the same one.
/// </summary>
public class TrackingPublisherClientTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private PubSubTrackingOptions MakeOptions() => new()
    {
        ServiceName = "PubSub",
        CallerName = "TestCaller",
        CurrentTestInfoFetcher = () => ("My PubSub Test", _testId),
    };

    [Fact]
    public async Task PublishAsync_stamps_the_scenario_the_publish_is_logged_under_when_given_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var publisher = new TrackingPublisherClient(new FakePublisherClient(), MakeOptions(),
            RequestHeaderAccessor.For("Header Test", headerTestId));
        var message = new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") };

        await publisher.PublishAsync(message);

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
        Assert.Equal("Header Test", message.Attributes[TestTrackingMessageHeaders.TestName]);
        Assert.Equal(headerTestId, message.Attributes[TestTrackingMessageHeaders.TestId]);
    }

    [Fact]
    public async Task PublishAsync_stamps_the_requests_scenario_when_the_options_carry_an_accessor()
    {
        var headerTestId = Guid.NewGuid().ToString();
        var options = MakeOptions();
        options.HttpContextAccessor = RequestHeaderAccessor.For("Header Test", headerTestId);
        var publisher = new TrackingPublisherClient(new FakePublisherClient(), options);
        var message = new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") };

        await publisher.PublishAsync(message);

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == headerTestId);
        Assert.Equal(headerTestId, message.Attributes[TestTrackingMessageHeaders.TestId]);
    }

    private sealed class FakePublisherClient : PublisherClient
    {
        public override TopicName TopicName { get; } = new("p", "my-topic");

        public override Task<string> PublishAsync(PubsubMessage message) => Task.FromResult("message-1");
    }
}
