using System.Net;
using Kronikol.Extensions.StorageQueues;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.StorageQueues;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the handler
/// records, for each operation at each verbosity, reads as its operation. A send publishes; a receive or a peek consumes;
/// deletes, updates, clearing and the queue management calls are no message. At Raw the handler records the HTTP request
/// itself, whose verb and path (ending in <c>messages</c>) say nothing of the direction or the queue.
/// </summary>
public class StorageQueueCardMessageTests : IDisposable
{
    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
    }

    private const string Account = "https://breakfast.queue.core.windows.net";

    // Each operation as the queue service's REST interface names it: a verb, a path and a query.
    private static readonly Dictionary<StorageQueueOperation, (string Method, string Path, string Direction)> Operations = new()
    {
        [StorageQueueOperation.SendMessage] = ("POST", "/orders/messages", "publishes"),
        [StorageQueueOperation.ReceiveMessages] = ("GET", "/orders/messages", "consumes"),
        [StorageQueueOperation.PeekMessages] = ("GET", "/orders/messages?peekonly=true", "consumes"),
        [StorageQueueOperation.DeleteMessage] = ("DELETE", "/orders/messages/message-1?popreceipt=a", "no message"),
        [StorageQueueOperation.UpdateMessage] = ("PUT", "/orders/messages/message-1?popreceipt=a", "no message"),
        [StorageQueueOperation.ClearMessages] = ("DELETE", "/orders/messages", "no message"),
        [StorageQueueOperation.CreateQueue] = ("PUT", "/orders", "no message"),
        [StorageQueueOperation.DeleteQueue] = ("DELETE", "/orders", "no message"),
        [StorageQueueOperation.GetProperties] = ("GET", "/orders?comp=metadata", "no message"),
        [StorageQueueOperation.SetMetadata] = ("PUT", "/orders?comp=metadata", "no message"),
        [StorageQueueOperation.ListQueues] = ("GET", "/?comp=list", "no message"),
        [StorageQueueOperation.Other] = ("HEAD", "/orders", "no direction")
    };

    private readonly Answer _answer = new();
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => _answer.Dispose();

    public static TheoryData<StorageQueueOperation, StorageQueueTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<StorageQueueOperation, StorageQueueTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<StorageQueueOperation>())
            foreach (var verbosity in Enum.GetValues<StorageQueueTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private async Task<RequestResponseLog?> Track(StorageQueueOperation operation, StorageQueueTrackingVerbosity verbosity)
    {
        using var invoker = new HttpMessageInvoker(new StorageQueueTrackingMessageHandler(new StorageQueueTrackingMessageHandlerOptions
        {
            ServiceName = "Storage Queues",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        }, _answer));
        var (method, path, _) = Operations[operation];
        using var request = new HttpRequestMessage(new HttpMethod(method), Account + path);
        if (method is "POST" or "PUT")
            request.Content = new StringContent("<QueueMessage><MessageText>{}</MessageText></QueueMessage>");
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        return RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Each_call_the_handler_records_reads_as_its_operation(StorageQueueOperation operation, StorageQueueTrackingVerbosity verbosity)
    {
        var call = await Track(operation, verbosity);
        if (call is null)
        {
            // at Summarised the handler records no call it cannot name
            Assert.Equal((StorageQueueOperation.Other, StorageQueueTrackingVerbosity.Summarised), (operation, verbosity));
            return;
        }

        var direction = Operations[operation].Direction;
        Assert.Equal(verbosity == StorageQueueTrackingVerbosity.Raw ? "no direction" : direction, TrackedMessages.Direction(call));
        if (verbosity != StorageQueueTrackingVerbosity.Raw && direction != "no message")
            Assert.Equal("orders", TrackedMessages.Topic(call));
    }

    [Fact]
    public async Task A_send_recorded_as_its_http_request_names_no_queue()
    {
        var call = (await Track(StorageQueueOperation.SendMessage, StorageQueueTrackingVerbosity.Raw))!;

        Assert.Null(TrackedMessages.Topic(call));
        Assert.False(TrackedMessages.EmptyTarget(call));
    }
}
