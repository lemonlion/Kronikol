using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class DeferredLogFlushHandlerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();
    private const string TestName = "DeferredFlushTest";

    private RequestResponseLog[] GetLogsForTest()
        => RequestResponseLogger.RequestAndResponseLogs.Where(l => l.TestId == _testId).ToArray();

    [Fact]
    public async Task Flushes_pending_entries_after_response()
    {
        PendingRequestResponseLogs.Clear();
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op", null, null, new Uri("mock://svc/op")));

        var handler = new DeferredLogFlushHandler(() => (TestName, _testId))
        {
            InnerHandler = new FakeInnerHandler()
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await client.GetAsync("/test", TestContext.Current.CancellationToken);

        var logs = GetLogsForTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal("Svc", logs[0].ServiceName);
    }

    [Fact]
    public async Task Does_not_flush_when_no_pending_entries()
    {
        PendingRequestResponseLogs.Clear();

        var countBefore = RequestResponseLogger.RequestAndResponseLogs
            .Count(l => l.TestId == _testId);

        var handler = new DeferredLogFlushHandler(() => (TestName, _testId))
        {
            InnerHandler = new FakeInnerHandler()
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await client.GetAsync("/test", TestContext.Current.CancellationToken);

        var countAfter = RequestResponseLogger.RequestAndResponseLogs
            .Count(l => l.TestId == _testId);

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task Flushes_multiple_pending_entries()
    {
        PendingRequestResponseLogs.Clear();
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc1", "Caller", "Op1", null, null, new Uri("mock://svc1/op1")));
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc2", "Caller", "Op2", null, null, new Uri("mock://svc2/op2")));

        var handler = new DeferredLogFlushHandler(() => (TestName, _testId))
        {
            InnerHandler = new FakeInnerHandler()
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        await client.GetAsync("/test", TestContext.Current.CancellationToken);

        var logs = GetLogsForTest();
        Assert.Equal(4, logs.Length);
    }

    [Fact]
    public void Constructor_from_options_uses_test_info_fetcher()
    {
        var options = new TestTrackingMessageHandlerOptions
        {
            CurrentTestInfoFetcher = () => (TestName, _testId)
        };

        var handler = new DeferredLogFlushHandler(options)
        {
            InnerHandler = new FakeInnerHandler()
        };

        Assert.NotNull(handler);
    }

    [Fact]
    public async Task Forwards_response_without_flushing_when_fetcher_throws()
    {
        PendingRequestResponseLogs.Clear();
        PendingRequestResponseLogs.Enqueue(new PendingLogEntry(
            "Svc", "Caller", "Op", null, null, new Uri("mock://svc/op")));

        var handler = new DeferredLogFlushHandler(() => throw new InvalidOperationException("No test context"))
        {
            InnerHandler = new FakeInnerHandler()
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var response = await client.GetAsync("/test", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(PendingRequestResponseLogs.Count > 0, "Pending logs should NOT have been flushed");
        Assert.Empty(GetLogsForTest());
    }

    private class FakeInnerHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
