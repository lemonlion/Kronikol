using System.Net;
using Kronikol.Extensions.CloudStorage;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.CloudStorage;

/// <summary>
/// #125 renamed what the handler records, and history crosses the rename by reading each call under its earlier rules as
/// it was recorded then (<see cref="RequestResponseLog.RecordedBefore9"/>, plans/EMULATOR_URL_SHAPES_PLAN.md section 7).
/// These facts hold both halves of that against what the published 5.27.2 package recorded
/// (<see cref="RecordedBefore9Record"/>): the frozen copy of its classifier still records the same, and the new
/// handler's calls read under rules 6 to 8 exactly as that package's did.
/// </summary>
public class RecordedBefore9Tests
{
    public static TheoryData<string, string, string, string, string> Record()
    {
        var data = new TheoryData<string, string, string, string, string>();
        foreach (var line in RecordedBefore9Record.Lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\t');
            data.Add(f[0], f[1], f[2], f[3], f[4]);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Record))]
    public void The_frozen_classifier_records_what_the_published_package_recorded(string verbosity, string method, string url, string label, string uri)
    {
        var v = Enum.Parse<CloudStorageTrackingVerbosity>(verbosity);
        var op = CloudStorageOperationClassifierBefore9.Classify(new HttpRequestMessage(new HttpMethod(method), url));

        if (label == "-")
        {
            Assert.Equal((CloudStorageTrackingVerbosity.Summarised, CloudStorageOperation.Other), (v, op.Operation));
            return;
        }
        Assert.Equal(label, CloudStorageOperationClassifierBefore9.GetDiagramLabel(op, v));
        Assert.Equal(uri, CloudStorageOperationClassifierBefore9.BuildCleanUri(op).ToString());
    }

    [Theory]
    [MemberData(nameof(Record))]
    public async Task History_rules_6_to_8_read_each_call_as_the_published_package_recorded_it(string verbosity, string method, string url, string label, string uri)
    {
        var logs = await SendThroughTheHandler(Enum.Parse<CloudStorageTrackingVerbosity>(verbosity), method, url, new Answering());
        var request = Assert.Single(logs, l => l.Type == RequestResponseType.Request);

        RequestResponseLog[] then = label == "-"
            ? []
            : [
                request with { Method = label, Uri = new Uri(uri), RecordedBefore9 = null },
                logs.Single(l => l.Type == RequestResponseType.Response) with { Method = label, Uri = new Uri(uri), RecordedBefore9 = null }
            ];

        foreach (var rule in new[] { 6, 7, 8 })
            Assert.Equal(Lines(InteractionShape.Calls(then, null, rule)), Lines(InteractionShape.Calls(logs, null, rule)));
    }

    [Fact]
    public async Task Rule_9_reads_the_call_as_it_is_recorded_now()
    {
        var logs = await SendThroughTheHandler(CloudStorageTrackingVerbosity.Detailed, "GET", "http://localhost:4443/b/orders", new Answering());

        Assert.Equal(["TestCaller>CloudStorage GetBucket → orders /orders 200"], Lines(InteractionShape.Calls(logs, null, 9)));
        Assert.Equal(["TestCaller>CloudStorage Other / 200"], Lines(InteractionShape.Calls(logs, null, 8)));
    }

    [Fact]
    public async Task A_send_that_throws_is_answered_and_rules_6_to_8_read_it_unanswered()
    {
        var logs = await SendThroughTheHandler(CloudStorageTrackingVerbosity.Detailed, "GET",
            "https://storage.googleapis.com/storage/v1/b/orders", new Throwing(), expectThrow: true);

        var response = Assert.Single(logs, l => l.Type == RequestResponseType.Response);
        Assert.Equal("!HttpRequestException", response.StatusCode!.Value?.ToString());
        Assert.Equal(["TestCaller>CloudStorage GetBucket → orders /orders !HttpRequestException"], Lines(InteractionShape.Calls(logs, null, 9)));
        // 5.27.2 read a bucket whose name starts with o as a list of its objects (F12), and so does rule 8.
        Assert.Equal(["TestCaller>CloudStorage ListObjects → orders /orders -"], Lines(InteractionShape.Calls(logs, null, 8)));
    }

    private static async Task<RequestResponseLog[]> SendThroughTheHandler(CloudStorageTrackingVerbosity verbosity, string method, string url,
        HttpMessageHandler inner, bool expectThrow = false)
    {
        var testId = Guid.NewGuid().ToString();
        var options = new CloudStorageTrackingMessageHandlerOptions
        {
            ServiceName = "CloudStorage",
            CallerName = "TestCaller",
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("recorded before 9", testId),
        };
        using var invoker = new HttpMessageInvoker(new CloudStorageTrackingMessageHandler(options, inner));
        var send = invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), CancellationToken.None);
        if (expectThrow)
            await Assert.ThrowsAsync<HttpRequestException>(() => send);
        else
            await send;
        return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
    }

    private static string[] Lines(IEnumerable<ShapeCall> calls) => calls.Select(c => c.ToString()).ToArray();

    private sealed class Answering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (localhost:4443)");
    }
}
