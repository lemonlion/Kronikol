using System.Net;
using Kronikol.Extensions.CloudStorage;
using Kronikol.History;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.CloudStorage;

/// <summary>
/// A call through the Cloud Storage handler that throws (plans/FAILED_CALLS_PLAN.md F3, §5.7): a send that throws was answered since #125 (rule 9),
/// and a response whose body breaks off as it is read is now answered too, where it left the request alone; either way the
/// exception reaches the caller as it was thrown.
/// </summary>
public class CloudStorageFailedCallTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] Logs() => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    private CloudStorageTrackingMessageHandlerOptions Options() => new() { ServiceName = "Cloud Storage", CallerName = "Caller", CurrentTestInfoFetcher = () => ("Orders are kept", _testId) };

    private static HttpRequestMessage Request(string operation) => operation switch
    {
        "Upload" => new(HttpMethod.Post, "https://storage.googleapis.com/upload/storage/v1/b/my-bucket/o?uploadType=multipart")
        {
            Content = new StringContent("file-content", System.Text.Encoding.UTF8, "application/octet-stream")
        },
        "Download" => new(HttpMethod.Get, "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt?alt=media"),
        _ => new(HttpMethod.Delete, "https://storage.googleapis.com/storage/v1/b/my-bucket/o/file.txt"),
    };

    public static TheoryData<string> Operations() => ["Upload", "Download", "Delete"];

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_send_that_throws_is_answered_with_the_exceptions_type_and_left_as_it_was(string operation)
    {
        var thrown = FailedCallContract.Refused();
        using var invoker = new HttpMessageInvoker(new CloudStorageTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(thrown)));

        var caught = await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request(operation), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        var (request, answer) = FailedCallContract.AssertAnswered(Logs(), thrown);
        Assert.Equal(request.Method.Value, answer.Method.Value);
        Assert.Equal(request.Uri, answer.Uri);
        Assert.Null(answer.Content);
        Assert.Empty(answer.Headers);
    }

    [Theory]
    [Trait(FailedCallContract.Trait, FailedCallContract.Value)]
    [MemberData(nameof(Operations))]
    public async Task A_response_whose_body_breaks_off_is_answered_with_the_exceptions_type(string operation)
    {
        var thrown = new InvalidOperationException("The response body broke off.");
        using var invoker = new HttpMessageInvoker(new CloudStorageTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(thrown))));

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request(operation), TestContext.Current.CancellationToken));

        FailedCallContract.AssertUntouched(caught, thrown);
        FailedCallContract.AssertAnswered(Logs(), thrown);
    }

    [Fact]
    public async Task History_reads_a_send_that_threw_as_each_rule_recorded_it()
    {
        using var invoker = new HttpMessageInvoker(new CloudStorageTrackingMessageHandler(Options(), new FailedCallContract.ThrowingHandler(FailedCallContract.Refused())));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request("Download"), TestContext.Current.CancellationToken));

        foreach (var rule in InteractionShape.KeptVersions)
            Assert.Equal(rule >= 9 ? "!HttpRequestException" : null, Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }

    [Fact]
    public async Task History_rules_before_12_read_a_body_that_broke_off_as_no_answer_as_they_recorded_none()
    {
        using var invoker = new HttpMessageInvoker(new CloudStorageTrackingMessageHandler(Options(),
            new FailedCallContract.AnsweringHandler(() => FailedCallContract.BrokenBody(new InvalidOperationException("The response body broke off.")))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request("Download"), TestContext.Current.CancellationToken));

        foreach (var rule in InteractionShape.KeptVersions)
            Assert.Equal(rule >= 12 ? "!InvalidOperationException" : null, Assert.Single(InteractionShape.Calls(Logs(), null, rule)).Status);
    }
}
