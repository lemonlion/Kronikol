using System.Net;
using Kronikol.Extensions.BlobStorage;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.BlobStorage;

/// <summary>
/// #125's R2 renamed what the handler records, and history crosses the rename by reading each call under its earlier
/// rules as it was recorded then (<see cref="RequestResponseLog.RecordedBefore11"/>, plans/EMULATOR_URL_SHAPES_PLAN.md
/// section 7). These facts hold both halves of that against what the published 5.27.8 package recorded
/// (<see cref="RecordedBefore11Record"/>): the frozen copy of its classifier still records the same, and the new
/// handler's calls read under rules 6 to 10 exactly as that package's did.
/// </summary>
public class RecordedBefore11Tests
{
    public static TheoryData<string, string, string, string, string> Record()
    {
        var data = new TheoryData<string, string, string, string, string>();
        foreach (var line in RecordedBefore11Record.Lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\t');
            data.Add(f[0], f[1], f[2], f[3], f[4]);
        }
        return data;
    }

    // The rows on Azure's own endpoint that the published package recorded: what #125's R2 must not move.
    public static TheoryData<string, string, string, string, string> AzureRecord()
    {
        var data = new TheoryData<string, string, string, string, string>();
        foreach (var line in RecordedBefore11Record.Lines.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\t');
            var (url, label) = (f[2], f[3]);
            if (label != "-" && new Uri(url).Host.EndsWith(".blob.core.windows.net", StringComparison.Ordinal))
                data.Add(f[0], f[1], url, label, f[4]);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AzureRecord))]
    public async Task A_call_on_Azure_is_recorded_as_the_published_package_recorded_it_and_carries_no_mark(string verbosity, string method, string url,
        string label, string uri)
    {
        var logs = await SendThroughTheHandler(Enum.Parse<BlobTrackingVerbosity>(verbosity), method, url, new Answering());

        var request = Assert.Single(logs, l => l.Type == RequestResponseType.Request);
        Assert.Equal((label, uri), (request.Method.Value?.ToString(), request.Uri.ToString()));
        Assert.All(logs, l => Assert.Null(l.RecordedBefore11));
    }

    [Theory]
    [MemberData(nameof(Record))]
    public void The_frozen_classifier_records_what_the_published_package_recorded(string verbosity, string method, string url, string label, string uri)
    {
        var v = Enum.Parse<BlobTrackingVerbosity>(verbosity);
        var op = BlobOperationClassifierBefore11.Classify(new HttpRequestMessage(new HttpMethod(method), url));

        if (label == "-")
        {
            Assert.Equal((BlobTrackingVerbosity.Summarised, BlobOperation.Other), (v, op.Operation));
            return;
        }
        Assert.Equal(label, BlobOperationClassifierBefore11.GetDiagramLabel(op, v));
        Assert.Equal(uri, BlobOperationClassifierBefore11.BuildCleanUri(new Uri(url), op, v).ToString());
    }

    [Theory]
    [MemberData(nameof(Record))]
    public async Task History_rules_6_to_10_read_each_call_as_the_published_package_recorded_it(string verbosity, string method, string url, string label, string uri)
    {
        var logs = await SendThroughTheHandler(Enum.Parse<BlobTrackingVerbosity>(verbosity), method, url, new Answering());
        var request = Assert.Single(logs, l => l.Type == RequestResponseType.Request);

        // The call as 5.27.8 recorded it: its label and clean URI, answered as the stub answers, or no call at all.
        RequestResponseLog[] then = label == "-"
            ? []
            : [
                request with { Method = label, Uri = new Uri(uri), RecordedBefore11 = null },
                logs.Single(l => l.Type == RequestResponseType.Response) with { Method = label, Uri = new Uri(uri), RecordedBefore11 = null }
            ];

        foreach (var rule in new[] { 6, 7, 8, 9, 10 })
            Assert.Equal(Lines(InteractionShape.Calls(then, null, rule)), Lines(InteractionShape.Calls(logs, null, rule)));
    }

    [Fact]
    public async Task Rule_11_reads_the_call_as_it_is_recorded_now()
    {
        var logs = await SendThroughTheHandler(BlobTrackingVerbosity.Detailed, "GET", "http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json", new Answering());

        Assert.Equal(["TestCaller>BlobStorage Download /orders/order-{n}.json 200"], Lines(InteractionShape.Calls(logs, null, 11)));
        Assert.Equal(["TestCaller>BlobStorage Download /devstoreaccount1/orders/order-{n}.json 200"], Lines(InteractionShape.Calls(logs, null, 10)));
    }

    [Fact]
    public async Task A_call_recorded_as_it_was_carries_no_mark()
    {
        var logs = await SendThroughTheHandler(BlobTrackingVerbosity.Detailed, "GET", "https://shop.blob.core.windows.net/orders/order-1.json", new Answering());

        Assert.All(logs, l => Assert.Null(l.RecordedBefore11));
    }

    [Fact]
    public async Task Raw_carries_no_mark()
    {
        var logs = await SendThroughTheHandler(BlobTrackingVerbosity.Raw, "GET", "http://127.0.0.1:10000/devstoreaccount1/orders/order-1.json", new Answering());

        Assert.All(logs, l => Assert.Null(l.RecordedBefore11));
    }

    [Fact]
    public async Task A_send_that_throws_is_answered_and_rules_6_to_10_read_it_unanswered()
    {
        var logs = await SendThroughTheHandler(BlobTrackingVerbosity.Detailed, "GET",
            "https://shop.blob.core.windows.net/orders/order-1.json", new Throwing(), expectThrow: true);

        var response = Assert.Single(logs, l => l.Type == RequestResponseType.Response);
        Assert.Equal("!HttpRequestException", response.StatusCode!.Value?.ToString());
        Assert.Contains("refused", response.Error);
        Assert.Equal(["TestCaller>BlobStorage Download /orders/order-{n}.json !HttpRequestException"], Lines(InteractionShape.Calls(logs, null, 11)));
        Assert.Equal(["TestCaller>BlobStorage Download /orders/order-{n}.json -"], Lines(InteractionShape.Calls(logs, null, 10)));
    }

    private static async Task<RequestResponseLog[]> SendThroughTheHandler(BlobTrackingVerbosity verbosity, string method, string url,
        HttpMessageHandler inner, bool expectThrow = false)
    {
        var testId = Guid.NewGuid().ToString();
        var options = new BlobTrackingMessageHandlerOptions
        {
            ServiceName = "BlobStorage",
            CallerName = "TestCaller",
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("recorded before 10", testId),
        };
        using var invoker = new HttpMessageInvoker(new BlobTrackingMessageHandler(options, inner));
        var send = invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), CancellationToken.None);
        if (expectThrow)
            await Assert.ThrowsAsync<HttpRequestException>(() => send);
        else
            (await send).Dispose();
        return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
    }

    private static string[] Lines(IEnumerable<ShapeCall> calls) => calls.Select(c => c.ToString()).ToArray();

    private sealed class Answering : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") });
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (shop.blob.core.windows.net:443)");
    }
}
