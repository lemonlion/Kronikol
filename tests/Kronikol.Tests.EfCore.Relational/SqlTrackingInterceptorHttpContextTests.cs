using System.Data;
using Microsoft.AspNetCore.Http;
using Kronikol.Constants;
using Kronikol.Extensions.EfCore.Relational;
using Kronikol.Tracking;

namespace Kronikol.Tests.EfCore.Relational;

[Collection("TrackingComponentRegistry")]
public class SqlTrackingInterceptorHttpContextTests : IDisposable
{
    private readonly string _testId = Guid.NewGuid().ToString();

    public SqlTrackingInterceptorHttpContextTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    private RequestResponseLog[] GetLogsFromThisTest()
    {
        return RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.TestId == _testId)
            .ToArray();
    }

    private SqlTrackingInterceptorOptions MakeOptions(
        Func<(string Name, string Id)>? fetcher = null) => new()
    {
        ServiceName = "Database",
        CallerName = "TestCaller",
        Verbosity = SqlTrackingVerbosity.Detailed,
        CurrentTestInfoFetcher = fetcher ?? (() => ("My Test", _testId)),
    };

    private static StubDbCommand MakeCommand() =>
        new("SELECT [u].[Id] FROM [dbo].[Users] AS [u]", "MyDb", "localhost");

    // ─── Registry auto-registration ────────────────────────────
    // Lives in this class (not SqlTrackingInterceptorTests) because
    // TrackingComponentRegistry is a global static: every test touching it
    // must share this "TrackingComponentRegistry" collection, or a parallel
    // class's Clear() races the registration away.

    [Fact]
    public void Constructor_AutoRegistersWithTrackingComponentRegistry()
    {
        var interceptor = new SqlTrackingInterceptor(MakeOptions());

        var components = TrackingComponentRegistry.GetRegisteredComponents();
        Assert.Contains(components, c => ReferenceEquals(c, interceptor));
    }

    // ─── Exception safety ──────────────────────────────────────

    [Fact]
    public void LogCommandExecuting_DoesNotThrow_When_CurrentTestInfoFetcher_Throws()
    {
        var options = MakeOptions(fetcher: () => throw new InvalidOperationException("No scenario context"));
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeCommand();

        // Must not propagate the exception into the EF Core pipeline
        var exception = Record.Exception(() => interceptor.LogCommandExecuting(command));
        Assert.Null(exception);
    }

    [Fact]
    public void LogCommandExecuted_DoesNotThrow_When_CurrentTestInfoFetcher_Throws()
    {
        var options = MakeOptions(fetcher: () => throw new InvalidOperationException("No scenario context"));
        var interceptor = new SqlTrackingInterceptor(options);
        var command = MakeCommand();

        var exception = Record.Exception(() => interceptor.LogCommandExecuted(command));
        Assert.Null(exception);
    }

    [Fact]
    public void LogCommandExecuting_DoesNotLog_When_CurrentTestInfoFetcher_Throws()
    {
        var uniqueServiceName = $"ThrowDB-{Guid.NewGuid()}";
        var options = MakeOptions(fetcher: null);
        options.ServiceName = uniqueServiceName;
        options.CurrentTestInfoFetcher = (Func<(string Name, string Id)>)(() => throw new InvalidOperationException("No scenario context"));
        var interceptor = new SqlTrackingInterceptor(options);

        interceptor.LogCommandExecuting(MakeCommand());

        // No logs since no valid test info was available
        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.ServiceName == uniqueServiceName)
            .ToArray();
        Assert.Empty(logs);
    }

    [Fact]
    public void InvocationCount_StillIncrements_When_CurrentTestInfoFetcher_Throws()
    {
        var options = MakeOptions(fetcher: () => throw new InvalidOperationException("No scenario context"));
        var interceptor = new SqlTrackingInterceptor(options);

        interceptor.LogCommandExecuting(MakeCommand());

        // Invocation should still be counted even though logging couldn't proceed
        Assert.Equal(1, interceptor.InvocationCount);
    }

    // ─── HttpContext-based test identity ────────────────────────

    [Fact]
    public void LogCommandExecuting_Uses_HttpContext_Headers_When_Available()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HTTP Test";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = _testId;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = MakeOptions(fetcher: null);
        options.CurrentTestInfoFetcher = null;
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        interceptor.LogCommandExecuting(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("HTTP Test", logs[0].TestName);
    }

    [Fact]
    public void LogCommandExecuted_Uses_HttpContext_Headers_When_Available()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HTTP Test";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = _testId;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = MakeOptions(fetcher: null);
        options.CurrentTestInfoFetcher = null;
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        interceptor.LogCommandExecuted(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("HTTP Test", logs[0].TestName);
    }

    [Fact]
    public void HttpContext_Headers_Take_Precedence_Over_CurrentTestInfoFetcher()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "From Headers";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = _testId;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = MakeOptions(fetcher: () => ("From Fetcher", _testId));
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        interceptor.LogCommandExecuting(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("From Headers", logs[0].TestName);
    }

    [Fact]
    public void Falls_Back_To_Fetcher_When_HttpContext_Is_Null()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var options = MakeOptions(fetcher: () => ("From Fetcher", _testId));
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        interceptor.LogCommandExecuting(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("From Fetcher", logs[0].TestName);
    }

    [Fact]
    public void Falls_Back_To_Fetcher_When_HttpContext_Has_No_Headers()
    {
        var httpContext = new DefaultHttpContext();
        // No TTD headers set
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var options = MakeOptions(fetcher: () => ("From Fetcher", _testId));
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        interceptor.LogCommandExecuting(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("From Fetcher", logs[0].TestName);
    }

    [Fact]
    public void Uses_HttpContext_And_Catches_Fetcher_Exception_As_Fallback()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "From Headers";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = _testId;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        // Fetcher would throw, but headers take precedence
        var options = MakeOptions(fetcher: () => throw new InvalidOperationException());
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        interceptor.LogCommandExecuting(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("From Headers", logs[0].TestName);
    }

    [Fact]
    public void No_Log_When_HttpContext_Missing_And_Fetcher_Throws()
    {
        var uniqueServiceName = $"NoCtxDB-{Guid.NewGuid()}";
        var accessor = new HttpContextAccessor { HttpContext = null };
        var options = MakeOptions(fetcher: () => throw new InvalidOperationException());
        options.ServiceName = uniqueServiceName;
        var interceptor = new SqlTrackingInterceptor(options, accessor);

        var exception = Record.Exception(() => interceptor.LogCommandExecuting(MakeCommand()));

        Assert.Null(exception);
        var logs = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.ServiceName == uniqueServiceName)
            .ToArray();
        Assert.Empty(logs);
    }

    [Fact]
    public void No_Accessor_Provided_Falls_Back_To_Fetcher()
    {
        // Backward compatibility — no IHttpContextAccessor, just fetcher
        var options = MakeOptions(fetcher: () => ("From Fetcher", _testId));
        var interceptor = new SqlTrackingInterceptor(options);

        interceptor.LogCommandExecuting(MakeCommand());

        var logs = GetLogsFromThisTest();
        Assert.Single(logs);
        Assert.Equal("From Fetcher", logs[0].TestName);
    }
}
