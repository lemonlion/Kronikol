using System.Net;
using Kronikol.Extensions.AtlasDataApi;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.AtlasDataApi;

/// <summary>
/// A call through the Atlas Data API handler that throws (plans/FAILED_CALLS_PLAN.md F1, F2, §5.7): whatever the operation, and whether the
/// send or the read of its body throws, the call is answered with the exception's type and why, and the exception reaches the caller
/// as it was thrown.
/// </summary>
public class AtlasDataApiFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private AtlasDataApiTrackingMessageHandlerOptions Options() => new() { ServiceName = "Atlas", CallerName = "Caller", CurrentTestInfoFetcher = () => ("Orders are handled", _testId) };

    private static HttpRequestMessage Request(string operation) =>
        new(HttpMethod.Post, "https://data.mongodb-api.com/app/myapp/endpoint/data/v1/action/" + operation)
        {
            Content = new StringContent("""{"dataSource":"C","database":"orders_db","collection":"orders","document":{"name":"x"},"filter":{}}""",
                System.Text.Encoding.UTF8, "application/json")
        };

    public static TheoryData<string> Operations() => ["insertOne", "findOne", "updateOne", "deleteOne", "aggregate"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_call_that_throws_is_answered_with_the_exceptions_type_and_left_as_it_was(string operation)
    {
        var thrown = FailedCallContract.Refused();
        using var invoker = new HttpMessageInvoker(new AtlasDataApiTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)));

        var caught = await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request(operation), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        var (request, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(request.Method.Value, answer.Method.Value);
        Assert.Equal(request.Uri, answer.Uri);
        Assert.Null(answer.Content);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_response_whose_body_breaks_off_is_answered_with_the_exceptions_type()
    {
        var thrown = new InvalidOperationException("The response body broke off.");
        using var invoker = new HttpMessageInvoker(new AtlasDataApiTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(thrown))));

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request("insertOne"), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public async Task History_rules_before_12_read_the_answer_as_none_as_they_recorded_none()
    {
        using var invoker = new HttpMessageInvoker(new AtlasDataApiTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(FailedCallContract.Refused())));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request("insertOne"), TestContext.Current.CancellationToken));

        Assert.Equal("!HttpRequestException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }
}
