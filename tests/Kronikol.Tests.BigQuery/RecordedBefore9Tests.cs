using System.Net;
using Kronikol.Extensions.BigQuery;
using Kronikol.History;
using Kronikol.Tracking;

namespace Kronikol.Tests.BigQuery;

/// <summary>
/// #125 renamed what the handler records, and history crosses the rename by reading each call under its earlier rules as
/// it was recorded then (<see cref="RequestResponseLog.RecordedBefore9"/>, plans/EMULATOR_URL_SHAPES_PLAN.md section 7).
/// These facts hold the two halves of that against what the published 5.27.2 package recorded
/// (<see cref="RecordedBefore9Record"/>): the frozen copy of its classifier still records the same, and the new handler's
/// calls read under rule 8 exactly as that package's did, so no scenario reads as changed across the upgrade.
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
        var v = Enum.Parse<BigQueryTrackingVerbosity>(verbosity);
        var op = BigQueryOperationClassifierBefore9.Classify(new HttpRequestMessage(new HttpMethod(method), url));

        if (label == "-")
        {
            Assert.Equal((BigQueryTrackingVerbosity.Summarised, BigQueryOperation.Other), (v, op.Operation));
            return;
        }
        Assert.Equal(label, BigQueryOperationClassifierBefore9.GetDiagramLabel(op, v));
        Assert.Equal(uri, BigQueryOperationClassifierBefore9.BuildCleanUri(new Uri(url), op, v).ToString());
    }

    [Theory]
    [MemberData(nameof(Record))]
    public async Task History_rule_8_reads_each_call_as_the_published_package_recorded_it(string verbosity, string method, string url, string label, string uri)
    {
        var logs = await SendThroughTheHandler(Enum.Parse<BigQueryTrackingVerbosity>(verbosity), method, url);
        var request = Assert.Single(logs, l => l.Type == RequestResponseType.Request);

        // The call as 5.27.2 recorded it: its label and clean URI, answered as the stub answers, or no call at all.
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
        var logs = await SendThroughTheHandler(BigQueryTrackingVerbosity.Detailed, "GET", "http://127.0.0.1:9050/projects/shop/datasets/sales?key=k");

        Assert.Equal(["TestCaller>BigQuery Read /sales 200"], Lines(InteractionShape.Calls(logs, null, 9)));
        Assert.Equal(["TestCaller>BigQuery Other /projects/shop/datasets/sales?key 200"], Lines(InteractionShape.Calls(logs, null, 8)));
    }

    [Fact]
    public async Task A_call_recorded_as_it_was_carries_no_mark()
    {
        var logs = await SendThroughTheHandler(BigQueryTrackingVerbosity.Detailed, "POST",
            "https://bigquery.googleapis.com/bigquery/v2/projects/shop/queries?key=k");

        Assert.All(logs, l => Assert.Null(l.RecordedBefore9));
    }

    [Fact]
    public async Task Raw_carries_no_mark()
    {
        var logs = await SendThroughTheHandler(BigQueryTrackingVerbosity.Raw, "GET", "http://127.0.0.1:9050/projects/shop/datasets/sales");

        Assert.All(logs, l => Assert.Null(l.RecordedBefore9));
    }

    [Fact]
    public async Task A_send_that_throws_is_answered_and_rules_6_to_8_read_it_unanswered()
    {
        var testId = Guid.NewGuid().ToString();
        var options = new BigQueryTrackingMessageHandlerOptions
        {
            ServiceName = "BigQuery",
            CallerName = "TestCaller",
            CurrentTestInfoFetcher = () => ("failed send", testId),
        };
        using var invoker = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(options, new ThrowingHandler()));

        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Put, "http://0.0.0.0:9050/upload/bigquery/v2/projects/shop/jobs?uploadType=resumable&upload_id=u1"),
            CancellationToken.None));

        var logs = RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
        var response = Assert.Single(logs, l => l.Type == RequestResponseType.Response);
        Assert.Equal("!HttpRequestException", response.StatusCode!.Value?.ToString());
        Assert.Contains("refused", response.Error);
        Assert.Equal(["TestCaller>BigQuery Insert /job !HttpRequestException"],
            Lines(InteractionShape.Calls(logs, null, 9)));
        Assert.Equal(["TestCaller>BigQuery List /job -"], Lines(InteractionShape.Calls(logs, null, 8)));
    }

    private static async Task<RequestResponseLog[]> SendThroughTheHandler(BigQueryTrackingVerbosity verbosity, string method, string url)
    {
        var testId = Guid.NewGuid().ToString();
        var options = new BigQueryTrackingMessageHandlerOptions
        {
            ServiceName = "BigQuery",
            CallerName = "TestCaller",
            Verbosity = verbosity,
            CurrentTestInfoFetcher = () => ("recorded before 9", testId),
        };
        using var invoker = new HttpMessageInvoker(new BigQueryTrackingMessageHandler(options, new StubInnerHandler()));
        await invoker.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), CancellationToken.None);
        return RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == testId).ToArray();
    }

    private static string[] Lines(IEnumerable<ShapeCall> calls) => calls.Select(c => c.ToString()).ToArray();

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (0.0.0.0:9050)");
    }
}
