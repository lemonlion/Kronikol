using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class PendingRequestResponseLogsTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    public void Enqueue_and_FlushAll_creates_paired_log_entries()
    {
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Blob Storage", "My API", "Upload",
            "request body", "response body",
            new Uri("https://blob.core.windows.net/c/f"),
            HttpStatusCode.Created));

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void FlushAll_preserves_original_timestamp()
    {
        var before = DateTimeOffset.UtcNow;

        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Get",
            null, null,
            new Uri("mock://svc/op")));

        var after = DateTimeOffset.UtcNow;

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        var logs = GetLogsForTest();
        Assert.All(logs, l =>
        {
            Assert.NotNull(l.Timestamp);
            Assert.InRange(l.Timestamp!.Value, before, after);
        });
    }

    [Fact]
    public void FlushAll_shares_trace_and_request_response_ids()
    {
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op",
            null, null,
            new Uri("mock://svc/op")));

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        var logs = GetLogsForTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void FlushAll_sets_status_code_on_response()
    {
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op",
            null, null,
            new Uri("mock://svc/op"),
            HttpStatusCode.NotFound));

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        var logs = GetLogsForTest();
        Assert.Null(logs.First(l => l.Type == RequestResponseType.Request).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logs.First(l => l.Type == RequestResponseType.Response).StatusCode!.Value);
    }

    [Fact]
    public void Count_reflects_pending_entries()
    {
        var countBefore = PendingRequestResponseLogs.Count;

        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op", null, null, new Uri("mock://svc/op")));

        Assert.Equal(countBefore + 1, PendingRequestResponseLogs.Count);

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        Assert.Equal(countBefore, PendingRequestResponseLogs.Count);
    }

    [Fact]
    public void Clear_removes_pending_entries()
    {
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op", null, null, new Uri("mock://svc/op")));

        var countBefore = PendingRequestResponseLogs.Count;
        PendingRequestResponseLogs.Clear();
        Assert.True(PendingRequestResponseLogs.Count < countBefore);
    }

    [Fact]
    public void FlushAll_with_no_pending_entries_is_noop()
    {
        PendingRequestResponseLogs.FlushAll("Test", _testId);
        Assert.Empty(GetLogsForTest());
    }

    [Fact]
    public void Enqueue_sets_activity_ids()
    {
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op", null, null, new Uri("mock://svc/op"),
            ActivityTraceId: "trace123", ActivitySpanId: "span456"));

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        var logs = GetLogsForTest();
        Assert.All(logs, l =>
        {
            Assert.Equal("trace123", l.ActivityTraceId);
            Assert.Equal("span456", l.ActivitySpanId);
        });
    }

    [Fact]
    public void Multiple_entries_flush_in_order()
    {
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc1", "Caller", "Op1", null, null, new Uri("mock://svc1/op1")));
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc2", "Caller", "Op2", null, null, new Uri("mock://svc2/op2")));

        PendingRequestResponseLogs.FlushAll("Test", _testId);

        var logs = GetLogsForTest();
        Assert.Equal(4, logs.Length);
        Assert.Equal("Svc1", logs[0].ServiceName);
        Assert.Equal("Svc1", logs[1].ServiceName);
        Assert.Equal("Svc2", logs[2].ServiceName);
        Assert.Equal("Svc2", logs[3].ServiceName);
    }
}
