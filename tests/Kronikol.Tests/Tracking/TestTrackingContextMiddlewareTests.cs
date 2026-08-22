using Microsoft.AspNetCore.Http;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TestTrackingContextMiddlewareTests
{
    [Fact]
    public async Task Sets_TestIdentityScope_from_request_headers()
    {
        TestIdentityScope.Reset();
        (string Name, string Id)? capturedIdentity = null;

        var middleware = new TestTrackingContextMiddleware(context =>
        {
            capturedIdentity = TestIdentityScope.Current;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "My Test";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "test-id-123";

        await middleware.InvokeAsync(httpContext);

        Assert.NotNull(capturedIdentity);
        Assert.Equal("My Test", capturedIdentity.Value.Name);
        Assert.Equal("test-id-123", capturedIdentity.Value.Id);
    }

    [Fact]
    public async Task Clears_TestIdentityScope_after_request_completes()
    {
        TestIdentityScope.Reset();

        var middleware = new TestTrackingContextMiddleware(_ => Task.CompletedTask);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "My Test";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "test-id-123";

        await middleware.InvokeAsync(httpContext);

        Assert.Null(TestIdentityScope.Current);
    }

    [Fact]
    public async Task Does_not_set_scope_when_headers_are_missing()
    {
        TestIdentityScope.Reset();
        (string Name, string Id)? capturedIdentity = null;

        var middleware = new TestTrackingContextMiddleware(context =>
        {
            capturedIdentity = TestIdentityScope.Current;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(httpContext);

        Assert.Null(capturedIdentity);
    }

    [Fact]
    public async Task Does_not_set_scope_when_only_name_header_present()
    {
        TestIdentityScope.Reset();
        (string Name, string Id)? capturedIdentity = null;

        var middleware = new TestTrackingContextMiddleware(context =>
        {
            capturedIdentity = TestIdentityScope.Current;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "My Test";

        await middleware.InvokeAsync(httpContext);

        Assert.Null(capturedIdentity);
    }

    [Fact]
    public async Task Identity_flows_into_Task_Run()
    {
        TestIdentityScope.Reset();
        (string Name, string Id)? capturedInBackground = null;

        var middleware = new TestTrackingContextMiddleware(async context =>
        {
            var task = Task.Run(() =>
            {
                capturedInBackground = TestIdentityScope.Current;
            });
            await task;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "Background Test";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "bg-id-456";

        await middleware.InvokeAsync(httpContext);

        Assert.NotNull(capturedInBackground);
        Assert.Equal("Background Test", capturedInBackground.Value.Name);
        Assert.Equal("bg-id-456", capturedInBackground.Value.Id);
    }

    [Fact]
    public async Task Calls_next_middleware_when_no_headers()
    {
        var nextCalled = false;
        var middleware = new TestTrackingContextMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext();

        await middleware.InvokeAsync(httpContext);

        Assert.True(nextCalled);
    }
}
