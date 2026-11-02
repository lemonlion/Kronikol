using System.Net;
using System.Text;
using Kronikol.Extensions.EventBridge;
using Kronikol.Tests.CallGraph;
using Kronikol.Tracking;

namespace Kronikol.Tests.EventBridge;

/// <summary>
/// plans/SERVICE_CARD_PLAN.md 5.1.7, this package's row of the service card's message table: every call the handler
/// records, for each operation at each verbosity, reads as its operation. Putting events publishes; EventBridge delivers
/// through a rule's target, so nothing here consumes; the rule, target, bus, archive, replay and tag calls are no message.
/// The bus is the URI's authority. At Raw the handler records the HTTP request itself, whose verb and endpoint say
/// nothing of the direction or the bus.
/// </summary>
public class EventBridgeCardMessageTests : IDisposable
{
    private sealed class Answer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private static string Expected(EventBridgeOperation operation) => operation switch
    {
        EventBridgeOperation.PutEvents or EventBridgeOperation.PutPartnerEvents => "publishes",
        EventBridgeOperation.Other => "no direction",
        _ => "no message"
    };

    private readonly Answer _answer = new();
    private readonly string _testId = Guid.NewGuid().ToString();

    public void Dispose() => _answer.Dispose();

    public static TheoryData<EventBridgeOperation, EventBridgeTrackingVerbosity> Calls()
    {
        var calls = new TheoryData<EventBridgeOperation, EventBridgeTrackingVerbosity>();
        foreach (var operation in Enum.GetValues<EventBridgeOperation>())
            foreach (var verbosity in Enum.GetValues<EventBridgeTrackingVerbosity>())
                calls.Add(operation, verbosity);
        return calls;
    }

    private async Task<RequestResponseLog?> Track(EventBridgeOperation operation, EventBridgeTrackingVerbosity verbosity)
    {
        using var invoker = new HttpMessageInvoker(new EventBridgeTrackingMessageHandler(new EventBridgeTrackingMessageHandlerOptions
        {
            ServiceName = "EventBridge",
            CallerName = TrackedMessages.Subject,
            Verbosity = verbosity,
            // the tag calls and the bus listing are left out by default; a suite can record them
            ExcludedOperations = [],
            CurrentTestInfoFetcher = () => ("Order pancakes", _testId)
        }, _answer));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://events.eu-west-1.amazonaws.com/");
        // an operation the classifier does not know is Other
        request.Headers.Add("X-Amz-Target", "AWSEvents." + (operation == EventBridgeOperation.Other ? "UpdateEndpoint" : operation.ToString()));
        request.Content = new StringContent("""{"Entries": [{"EventBusName": "orders-bus", "Source": "breakfast", "DetailType": "OrderPlaced", "Detail": "{}"}]}""",
            Encoding.UTF8, "application/x-amz-json-1.1");
        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);
        return RequestResponseLogger.RequestAndResponseLogs.LastOrDefault(l => l.TestId == _testId && l.Type == RequestResponseType.Request);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Each_call_the_handler_records_reads_as_its_operation(EventBridgeOperation operation, EventBridgeTrackingVerbosity verbosity)
    {
        var call = await Track(operation, verbosity);
        if (call is null)
        {
            // at Summarised the handler records no call it cannot name
            Assert.Equal((EventBridgeOperation.Other, EventBridgeTrackingVerbosity.Summarised), (operation, verbosity));
            return;
        }

        Assert.Equal(verbosity == EventBridgeTrackingVerbosity.Raw ? "no direction" : Expected(operation), TrackedMessages.Direction(call));
    }

    [Theory]
    [InlineData(EventBridgeTrackingVerbosity.Detailed, "orders-bus")]
    [InlineData(EventBridgeTrackingVerbosity.Summarised, "orders-bus")]
    [InlineData(EventBridgeTrackingVerbosity.Raw, null)]
    public async Task Putting_events_names_the_bus_where_the_handler_writes_it(EventBridgeTrackingVerbosity verbosity, string? bus)
    {
        var call = (await Track(EventBridgeOperation.PutEvents, verbosity))!;

        Assert.Equal(bus, TrackedMessages.Topic(call));
        Assert.False(TrackedMessages.EmptyTarget(call));
    }

    // plans/CALLS_AND_EVENTS_PLAN.md 6.7: an event exactly when the call moves a message, at every verbosity, and a response
    // carries its request's kind. The oracle is this class's own reading of the operation, not MessageTable: the table
    // cannot read a call recorded as its HTTP request or under its transport's address.
    [Theory]
    [MemberData(nameof(Calls))]
    public async Task A_call_is_an_event_exactly_when_it_moves_a_message(EventBridgeOperation operation, EventBridgeTrackingVerbosity verbosity)
    {
        var call = await Track(operation, verbosity);
        if (call is null)
            return; // at Summarised the handler records no call it cannot name

        var expected = Expected(operation) == "publishes" ? RequestResponseMetaType.Event : RequestResponseMetaType.Default;
        Assert.Equal(expected, call.MetaType);
        Assert.Equal(expected, RequestResponseLogger.RequestAndResponseLogs.Last(l =>
            l.TestId == _testId && l.RequestResponseId == call.RequestResponseId && l.Type == RequestResponseType.Response).MetaType);
    }
}
