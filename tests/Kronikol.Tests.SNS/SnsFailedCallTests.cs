using System.Net;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Kronikol.Extensions.SNS;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.SNS;

/// <summary>
/// A call through the SNS handler that throws (plans/FAILED_CALLS_PLAN.md F1, F2, §5.7): whatever the operation, and whether the
/// send or the read of its body throws, the call is answered with the exception's type and why, and the exception reaches the caller
/// as it was thrown.
/// </summary>
public class SnsFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private SnsTrackingMessageHandlerOptions Options() => new() { ServiceName = "SNS", CallerName = "Caller", CurrentTestInfoFetcher = () => ("Orders are handled", _testId) };

    private static HttpRequestMessage Request(string operation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://sns.us-east-1.amazonaws.com/");
        request.Headers.Add("X-Amz-Target", "AmazonSimpleNotificationService." + operation);
        request.Content = new StringContent("""{"TopicArn": "arn:aws:sns:us-east-1:123456789012:orders", "Name": "orders", "Message": "hello", "Protocol": "sqs"}""", System.Text.Encoding.UTF8, "application/x-amz-json-1.0");
        return request;
    }

    public static TheoryData<string> Operations() => ["Publish", "Subscribe", "DeleteTopic", "CreateTopic"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_call_that_throws_is_answered_with_the_exceptions_type_and_left_as_it_was(string operation)
    {
        var thrown = FailedCallContract.Refused();
        using var invoker = new HttpMessageInvoker(new SnsTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)));

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
        using var invoker = new HttpMessageInvoker(new SnsTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(thrown))));

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request("Publish"), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    public async Task A_call_through_the_SDK_client_that_throws_is_answered()
    {
        var thrown = FailedCallContract.Refused();
        using var client = new AmazonSimpleNotificationServiceClient(new Amazon.Runtime.BasicAWSCredentials("AKIDEXAMPLE", "secret"), new AmazonSimpleNotificationServiceConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            MaxErrorRetry = 0,
            HttpClientFactory = new Factory(new SnsTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)))
        });

        await Assert.ThrowsAnyAsync<Exception>(() => client.PublishAsync(new PublishRequest { TopicArn = "arn:aws:sns:us-east-1:123456789012:orders", Message = "hello" }, TestContext.Current.CancellationToken));

        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    private sealed class Factory(HttpMessageHandler handler) : Amazon.Runtime.HttpClientFactory
    {
        public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task History_rules_before_12_read_the_answer_as_none_as_they_recorded_none()
    {
        using var invoker = new HttpMessageInvoker(new SnsTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(FailedCallContract.Refused())));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request("Publish"), TestContext.Current.CancellationToken));

        Assert.Equal("!HttpRequestException", Assert.Single(InteractionShape.Calls(Logs(), null, 12)).Status);
        foreach (var rule in InteractionShape.KeptVersions.Where(v => v < 12))
            Assert.Null(Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }
}
