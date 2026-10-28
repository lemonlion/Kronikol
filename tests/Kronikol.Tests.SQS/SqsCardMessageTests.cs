using System.Net;
using System.Text;
using Kronikol.Extensions.SQS;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.SQS;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the handler
/// records, for each operation at each verbosity, reads as its operation. A send publishes; a receive consumes; deletes,
/// visibility changes and the queue management calls are no message. At Raw the handler records the HTTP request itself,
/// whose verb and endpoint say nothing of the direction or the queue.
/// </summary>
public class SqsCardMessageTests : IDisposable
{
    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private static readonly Dictionary<SqsOperation, string> Directions = new()
    {
        [SqsOperation.SendMessage] = "publishes",
        [SqsOperation.SendMessageBatch] = "publishes",
        [SqsOperation.ReceiveMessage] = "consumes",
        [SqsOperation.DeleteMessage] = "no message",
        [SqsOperation.DeleteMessageBatch] = "no message",
        [SqsOperation.ChangeMessageVisibility] = "no message",
        [SqsOperation.ChangeMessageVisibilityBatch] = "no message",
        [SqsOperation.CreateQueue] = "no message",
        [SqsOperation.DeleteQueue] = "no message",
        [SqsOperation.GetQueueUrl] = "no message",
        [SqsOperation.GetQueueAttributes] = "no message",
        [SqsOperation.SetQueueAttributes] = "no message",
        [SqsOperation.PurgeQueue] = "no message",
        [SqsOperation.ListQueues] = "no message",
        [SqsOperation.Other] = "no direction"
    };

    private readonly Answer _answer = new();
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => _answer.Dispose();

    public static TheoryData<SqsOperation, SqsTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<SqsOperation, SqsTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<SqsOperation>())
            foreach (var verbosity in Enum.GetValues<SqsTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private async Task<RequestResponseLog?> Track(SqsOperation operation, SqsTrackingVerbosity verbosity)
    {
        using var invoker = new HttpMessageInvoker(new SqsTrackingMessageHandler(new SqsTrackingMessageHandlerOptions
        {
            ServiceName = "SQS",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        }, _answer));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://sqs.eu-west-1.amazonaws.com/123456789012/orders");
        // an operation the classifier does not know is Other
        request.Headers.Add("X-Amz-Target", "AmazonSQS." + (operation == SqsOperation.Other ? "TagQueue" : operation.ToString()));
        request.Content = new StringContent("""{"QueueUrl": "https://sqs.eu-west-1.amazonaws.com/123456789012/orders", "MessageBody": "{}"}""",
            Encoding.UTF8, "application/x-amz-json-1.0");
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        return RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Each_call_the_handler_records_reads_as_its_operation(SqsOperation operation, SqsTrackingVerbosity verbosity)
    {
        var call = await Track(operation, verbosity);
        if (call is null)
        {
            // at Summarised the handler records no call it cannot name
            Assert.Equal((SqsOperation.Other, SqsTrackingVerbosity.Summarised), (operation, verbosity));
            return;
        }

        Assert.Equal(verbosity == SqsTrackingVerbosity.Raw ? "no direction" : Directions[operation], TrackedMessages.Direction(call));
        if (verbosity != SqsTrackingVerbosity.Raw && Directions[operation] != "no message")
            Assert.Equal("orders", TrackedMessages.Topic(call));
    }

    [Fact]
    public async Task A_send_recorded_as_its_http_request_names_no_queue()
    {
        var call = (await Track(SqsOperation.SendMessage, SqsTrackingVerbosity.Raw))!;

        Assert.Null(TrackedMessages.Topic(call));
        Assert.False(TrackedMessages.EmptyTarget(call));
    }
}
