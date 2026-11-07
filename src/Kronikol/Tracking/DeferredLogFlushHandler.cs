namespace Kronikol.Tracking;

/// <summary>
/// A <see cref="DelegatingHandler"/> that flushes <see cref="PendingRequestResponseLogs"/>
/// after each HTTP response. Place OUTSIDE <see cref="TestTrackingMessageHandler"/> in the
/// handler chain so it runs after the full request/response cycle.
/// </summary>
public class DeferredLogFlushHandler : DelegatingHandler
{
    private readonly Func<(string Name, string Id)> _testInfoFetcher;

    public DeferredLogFlushHandler(Func<(string Name, string Id)> testInfoFetcher)
    {
        _testInfoFetcher = testInfoFetcher;
    }

    public DeferredLogFlushHandler(TestTrackingMessageHandlerOptions options)
    {
        _testInfoFetcher = options.CurrentTestInfoFetcher
                           ?? throw new ArgumentException(
                               "CurrentTestInfoFetcher must be set on options for DeferredLogFlushHandler.",
                               nameof(options));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The calls queued during a send are its own whether it answered or threw: a send that threw (TestServer hands the
        // client an app's unhandled exception) flushed nothing, so its calls went out with the next send that succeeded,
        // under that send's test (FAILED_CALLS_PLAN.md F28).
        var threw = true;
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            threw = false;
            return response;
        }
        finally
        {
            Flush(threw);
        }
    }

    private void Flush(bool afterAThrow)
    {
        if (PendingRequestResponseLogs.Count == 0)
            return;
        try
        {
            var (testName, testId) = _testInfoFetcher();
            PendingRequestResponseLogs.FlushAll(testName, testId, afterAThrow);
        }
        catch
        {
            // Fetcher threw (e.g. no test context during startup) — skip flush.
            // Entries remain pending for the next successful invocation.
        }
    }
}
