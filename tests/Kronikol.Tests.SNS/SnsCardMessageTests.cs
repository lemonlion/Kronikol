using System.Net;
using System.Text;
using Kronikol.Extensions.SNS;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.SNS;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the handler
/// records, for each operation at each verbosity, reads as its operation. A publish publishes; SNS delivers through the
/// subscriber's own transport, so nothing here consumes; subscriptions and the topic management calls are no message. At
/// Raw the handler records the HTTP request itself, whose verb and endpoint say nothing of the direction or the topic.
/// </summary>
public class SnsCardMessageTests : IDisposable
{
    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private static readonly Dictionary<SnsOperation, string> Directions = new()
    {
        [SnsOperation.Publish] = "publishes",
        [SnsOperation.PublishBatch] = "publishes",
        [SnsOperation.Subscribe] = "no message",
        [SnsOperation.Unsubscribe] = "no message",
        [SnsOperation.CreateTopic] = "no message",
        [SnsOperation.DeleteTopic] = "no message",
        [SnsOperation.ListTopics] = "no message",
        [SnsOperation.ListSubscriptions] = "no message",
        [SnsOperation.ListSubscriptionsByTopic] = "no message",
        [SnsOperation.GetTopicAttributes] = "no message",
        [SnsOperation.SetTopicAttributes] = "no message",
        [SnsOperation.ConfirmSubscription] = "no message",
        [SnsOperation.Other] = "no direction"
    };

    private readonly Answer _answer = new();
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => _answer.Dispose();

    public static TheoryData<SnsOperation, SnsTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<SnsOperation, SnsTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<SnsOperation>())
            foreach (var verbosity in Enum.GetValues<SnsTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private async Task<RequestResponseLog?> Track(SnsOperation operation, SnsTrackingVerbosity verbosity)
    {
        using var invoker = new HttpMessageInvoker(new SnsTrackingMessageHandler(new SnsTrackingMessageHandlerOptions
        {
            ServiceName = "SNS",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        }, _answer));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.eu-west-1.amazonaws.com/");
        // an operation the classifier does not know is Other
        request.Headers.Add("X-Amz-Target", "AmazonSimpleNotificationService." + (operation == SnsOperation.Other ? "TagResource" : operation.ToString()));
        request.Content = new StringContent("""{"TopicArn": "arn:aws:sns:eu-west-1:123456789012:orders", "Message": "{}"}""",
            Encoding.UTF8, "application/x-amz-json-1.0");
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        return RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Each_call_the_handler_records_reads_as_its_operation(SnsOperation operation, SnsTrackingVerbosity verbosity)
    {
        var call = await Track(operation, verbosity);
        if (call is null)
        {
            // at Summarised the handler records no call it cannot name
            Assert.Equal((SnsOperation.Other, SnsTrackingVerbosity.Summarised), (operation, verbosity));
            return;
        }

        Assert.Equal(verbosity == SnsTrackingVerbosity.Raw ? "no direction" : Directions[operation], TrackedMessages.Direction(call));
        if (verbosity != SnsTrackingVerbosity.Raw && Directions[operation] != "no message")
            Assert.Equal("orders", TrackedMessages.Topic(call));
    }

    [Fact]
    public async Task A_publish_recorded_as_its_http_request_names_no_topic()
    {
        var call = (await Track(SnsOperation.Publish, SnsTrackingVerbosity.Raw))!;

        Assert.Null(TrackedMessages.Topic(call));
        Assert.False(TrackedMessages.EmptyTarget(call));
    }
}
