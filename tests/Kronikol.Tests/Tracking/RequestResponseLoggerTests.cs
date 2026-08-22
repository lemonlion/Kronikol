using System.Net;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class RequestResponseLoggerTests
{
    private readonly string _testId = Guid.NewGuid().ToString();

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    [Fact]
    public void LogPair_creates_request_and_response_entries()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Blob Upload",
            uri: new Uri("https://blob.core.windows.net/container/file.json"),
            serviceName: "Blob Storage",
            callerName: "My API");

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.Equal(RequestResponseType.Request, logs[0].Type);
        Assert.Equal(RequestResponseType.Response, logs[1].Type);
    }

    [Fact]
    public void LogPair_shares_trace_id_and_request_response_id()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Cache Get",
            uri: new Uri("redis://cache/key"),
            serviceName: "Redis",
            callerName: "My API");

        var logs = GetLogsFromThisTest();
        Assert.Equal(logs[0].TraceId, logs[1].TraceId);
        Assert.Equal(logs[0].RequestResponseId, logs[1].RequestResponseId);
    }

    [Fact]
    public void LogPair_sets_method_and_service_names()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Send",
            uri: new Uri("mock://application/IMediator/Send"),
            serviceName: "Application",
            callerName: "My API");

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l =>
        {
            Assert.Equal("Send", l.Method.Value?.ToString());
            Assert.Equal("Application", l.ServiceName);
            Assert.Equal("My API", l.CallerName);
        });
    }

    [Fact]
    public void LogPair_sets_request_and_response_content()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Upload",
            uri: new Uri("https://blob.core.windows.net/c/f"),
            serviceName: "Blob",
            callerName: "API",
            requestContent: """{"name":"test"}""",
            responseContent: """{"status":"ok"}""");

        var logs = GetLogsFromThisTest();
        Assert.Equal("""{"name":"test"}""", logs.First(l => l.Type == RequestResponseType.Request).Content);
        Assert.Equal("""{"status":"ok"}""", logs.First(l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public void LogPair_sets_status_code_on_response_only()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Get",
            uri: new Uri("https://api.example.com/data"),
            serviceName: "External API",
            callerName: "My API",
            statusCode: HttpStatusCode.NotFound);

        var logs = GetLogsFromThisTest();
        Assert.Null(logs.First(l => l.Type == RequestResponseType.Request).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, logs.First(l => l.Type == RequestResponseType.Response).StatusCode!.Value);
    }

    [Fact]
    public void LogPair_sets_timestamps_on_both_entries()
    {
        var before = DateTimeOffset.UtcNow;

        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Query",
            uri: new Uri("cosmos://db/container"),
            serviceName: "Cosmos",
            callerName: "API");

        var after = DateTimeOffset.UtcNow;

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l =>
        {
            Assert.NotNull(l.Timestamp);
            Assert.InRange(l.Timestamp!.Value, before, after);
        });
    }

    [Fact]
    public void LogPair_accepts_HttpMethod()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: HttpMethod.Post,
            uri: new Uri("https://api.example.com/items"),
            serviceName: "Items API",
            callerName: "My API");

        var logs = GetLogsFromThisTest();
        Assert.Equal(HttpMethod.Post, logs[0].Method.Value);
    }

    [Fact]
    public void LogPair_defaults_to_not_tracking_ignore()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Op",
            uri: new Uri("mock://svc/op"),
            serviceName: "Svc",
            callerName: "Caller");

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l => Assert.False(l.TrackingIgnore));
    }

    // ─── MaxContentLength ───────────────────────────────────

    [Fact]
    public void Log_MaxContentLength_Null_NoTruncation()
    {
        RequestResponseLogger.MaxContentLength = null;
        var longContent = new string('x', 50_000);

        RequestResponseLogger.Log(MakeLog(longContent));

        var log = GetLogsFromThisTest().Single();
        Assert.Equal(50_000, log.Content!.Length);
    }

    [Fact]
    public void Log_MaxContentLength_TruncatesContent()
    {
        RequestResponseLogger.MaxContentLength = 100;
        try
        {
            var longContent = new string('x', 500);

            RequestResponseLogger.Log(MakeLog(longContent));

            var log = GetLogsFromThisTest().Single();
            Assert.True(log.Content!.Length < 500);
            Assert.StartsWith(new string('x', 100), log.Content);
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    [Fact]
    public void Log_MaxContentLength_AddsMarker()
    {
        RequestResponseLogger.MaxContentLength = 50;
        try
        {
            var longContent = new string('y', 200);

            RequestResponseLogger.Log(MakeLog(longContent));

            var log = GetLogsFromThisTest().Single();
            Assert.Contains("…truncated", log.Content!);
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    [Fact]
    public void Log_MaxContentLength_ShortContentUnchanged()
    {
        RequestResponseLogger.MaxContentLength = 100;
        try
        {
            RequestResponseLogger.Log(MakeLog("short"));

            var log = GetLogsFromThisTest().Single();
            Assert.Equal("short", log.Content);
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    [Fact]
    public void Log_MaxContentLength_NullContentUnchanged()
    {
        RequestResponseLogger.MaxContentLength = 100;
        try
        {
            RequestResponseLogger.Log(MakeLog(null));

            var log = GetLogsFromThisTest().Single();
            Assert.Null(log.Content);
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    [Fact]
    public void LogPair_MaxContentLength_TruncatesBothRequestAndResponse()
    {
        RequestResponseLogger.MaxContentLength = 20;
        try
        {
            var longReq = new string('a', 200);
            var longRes = new string('b', 200);

            RequestResponseLogger.LogPair(
                testName: "My Test",
                testId: _testId,
                method: "Op",
                uri: new Uri("mock://svc/op"),
                serviceName: "Svc",
                callerName: "Caller",
                requestContent: longReq,
                responseContent: longRes);

            var logs = GetLogsFromThisTest();
            Assert.All(logs.Where(l => l.Content is not null), l =>
            {
                Assert.True(l.Content!.Length < 200);
                Assert.Contains("…truncated", l.Content);
            });
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    [Fact]
    public void Log_MaxContentLength_MarkerShowsOriginalSize()
    {
        RequestResponseLogger.MaxContentLength = 30;
        try
        {
            var content = new string('z', 5000);

            RequestResponseLogger.Log(MakeLog(content));

            var log = GetLogsFromThisTest().Single();
            Assert.Contains("5000", log.Content!);
        }
        finally
        {
            RequestResponseLogger.MaxContentLength = null;
        }
    }

    // ─── LogPair with auto-resolving test info ────────────

    [Fact]
    public void LogPair_auto_resolving_overload_uses_provided_fetcher()
    {
        RequestResponseLogger.LogPair(
            method: "Cache Set",
            uri: new Uri("redis://cache/key"),
            serviceName: "Redis",
            callerName: "API",
            testInfoFetcher: () => ("Auto Test", _testId));

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l =>
        {
            Assert.Equal("Auto Test", l.TestName);
            Assert.Equal(_testId, l.TestId);
        });
    }

    [Fact]
    public void LogPair_auto_resolving_overload_sets_method_and_names()
    {
        RequestResponseLogger.LogPair(
            method: "Blob Upload",
            uri: new Uri("https://blob.core.windows.net/c/f"),
            serviceName: "Blob Storage",
            callerName: "My API",
            testInfoFetcher: () => ("T", _testId));

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l =>
        {
            Assert.Equal("Blob Upload", l.Method.Value?.ToString());
            Assert.Equal("Blob Storage", l.ServiceName);
            Assert.Equal("My API", l.CallerName);
        });
    }

    [Fact]
    public void LogPair_auto_resolving_overload_sets_content()
    {
        RequestResponseLogger.LogPair(
            method: "Op",
            uri: new Uri("mock://svc/op"),
            serviceName: "Svc",
            callerName: "Caller",
            testInfoFetcher: () => ("T", _testId),
            requestContent: "req-body",
            responseContent: "res-body");

        var logs = GetLogsFromThisTest();
        Assert.Equal("req-body", logs.First(l => l.Type == RequestResponseType.Request).Content);
        Assert.Equal("res-body", logs.First(l => l.Type == RequestResponseType.Response).Content);
    }

    [Fact]
    public void LogPair_auto_resolving_overload_sets_status_code()
    {
        RequestResponseLogger.LogPair(
            method: "Get",
            uri: new Uri("https://api.example.com/data"),
            serviceName: "API",
            callerName: "Caller",
            testInfoFetcher: () => ("T", _testId),
            statusCode: HttpStatusCode.NotFound);

        var logs = GetLogsFromThisTest();
        Assert.Equal(HttpStatusCode.NotFound,
            logs.First(l => l.Type == RequestResponseType.Response).StatusCode!.Value);
    }

    [Fact]
    public void LogPair_auto_resolving_overload_skips_when_fetcher_is_null()
    {
        var uniqueTestId = Guid.NewGuid().ToString();
        RequestResponseLogger.LogPair(
            method: "Op",
            uri: new Uri("mock://svc/op"),
            serviceName: "Svc",
            callerName: "Caller",
            testInfoFetcher: null);

        // Should not throw, but produces no logs (or logs with null identity)
        // The important thing is it doesn't crash
    }

    [Fact]
    public void LogPair_auto_resolving_overload_uses_TestIdentityScope_when_no_fetcher()
    {
        using (TestIdentityScope.Begin("Scoped Test", _testId))
        {
            RequestResponseLogger.LogPair(
                method: "Background Op",
                uri: new Uri("mock://svc/bg"),
                serviceName: "Svc",
                callerName: "Caller",
                testInfoFetcher: null);
        }

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Scoped Test", l.TestName));
    }

    // ─── LogPair dependencyCategory ─────────────────────────

    [Fact]
    public void LogPair_sets_dependency_category_on_both_entries()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Blob Upload",
            uri: new Uri("https://blob.core.windows.net/c/f"),
            serviceName: "Blob Storage",
            callerName: "My API",
            dependencyCategory: DependencyCategories.BlobStorage);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal(DependencyCategories.BlobStorage, l.DependencyCategory));
    }

    [Fact]
    public void LogPair_dependency_category_defaults_to_null()
    {
        RequestResponseLogger.LogPair(
            testName: "My Test",
            testId: _testId,
            method: "Op",
            uri: new Uri("mock://svc/op"),
            serviceName: "Svc",
            callerName: "Caller");

        var logs = GetLogsFromThisTest();
        Assert.All(logs, l => Assert.Null(l.DependencyCategory));
    }

    [Fact]
    public void LogPair_auto_resolving_overload_sets_dependency_category()
    {
        RequestResponseLogger.LogPair(
            method: "Query",
            uri: new Uri("cosmos://db/container"),
            serviceName: "Cosmos",
            callerName: "API",
            testInfoFetcher: () => ("T", _testId),
            dependencyCategory: DependencyCategories.CosmosDB);

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal(DependencyCategories.CosmosDB, l.DependencyCategory));
    }

    // ─── LogPair UnknownIdentity fallthrough ─────────────────────

    [Fact]
    public void LogPair_auto_resolving_overload_falls_through_to_TestIdentityScope_when_fetcher_returns_UnknownIdentity()
    {
        using (TestIdentityScope.Begin("Scoped Test", _testId))
        {
            RequestResponseLogger.LogPair(
                method: "Op",
                uri: new Uri("mock://svc/op"),
                serviceName: "Svc",
                callerName: "Caller",
                testInfoFetcher: () => TestIdentityScope.UnknownIdentity);
        }

        var logs = GetLogsFromThisTest();
        Assert.Equal(2, logs.Length);
        Assert.All(logs, l => Assert.Equal("Scoped Test", l.TestName));
    }

    private RequestResponseLog MakeLog(string? content) => new(
        "My Test", _testId, "Op", content, new Uri("mock://svc/op"),
        [], "Svc", "Caller", RequestResponseType.Request,
        Guid.NewGuid(), Guid.NewGuid(), false);
}
