using System.Net;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Kronikol.Extensions.DynamoDB;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.DynamoDB;

/// <summary>
/// A call through the DynamoDB handler that throws (plans/FAILED_CALLS_PLAN.md F1, F2, §5.7): whatever the operation, and whether the
/// send or the read of its body throws, the call is answered with the exception's type and why, and the exception reaches the caller
/// as it was thrown.
/// </summary>
public class DynamoDbFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private DynamoDbTrackingMessageHandlerOptions Options() => new() { ServiceName = "DynamoDB", CallerName = "Caller", CurrentTestInfoFetcher = () => ("Orders are handled", _testId) };

    private static HttpRequestMessage Request(string operation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://dynamodb.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "DynamoDB_20120810." + operation);
        request.Content = new StringContent("""{"TableName": "Users", "Item": {"id": {"S": "123"}}, "Key": {"id": {"S": "123"}}}""", System.Text.Encoding.UTF8, "application/x-amz-json-1.0");
        return request;
    }

    public static TheoryData<string> Operations() => ["PutItem", "GetItem", "Query", "CreateTable", "DeleteItem"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_call_that_throws_is_answered_with_the_exceptions_type_and_left_as_it_was(string operation)
    {
        var thrown = FailedCallContract.Refused();
        using var invoker = new HttpMessageInvoker(new DynamoDbTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)));

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
        using var invoker = new HttpMessageInvoker(new DynamoDbTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(thrown))));

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request("PutItem"), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_call_through_the_SDK_client_that_throws_is_answered()
    {
        var thrown = FailedCallContract.Refused();
        using var client = new AmazonDynamoDBClient(new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret"), new AmazonDynamoDBConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            MaxErrorRetry = 0,
            HttpClientFactory = new Factory(new DynamoDbTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)))
        });

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = new() { ["id"] = new AttributeValue { S = "123" } } }, TestContext.Current.CancellationToken));

        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    private sealed class Factory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
    {
        public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task History_rules_before_12_read_the_answer_as_none_as_they_recorded_none()
    {
        using var invoker = new HttpMessageInvoker(new DynamoDbTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(FailedCallContract.Refused())));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request("PutItem"), TestContext.Current.CancellationToken));

        Assert.Equal("!HttpRequestException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }
}
