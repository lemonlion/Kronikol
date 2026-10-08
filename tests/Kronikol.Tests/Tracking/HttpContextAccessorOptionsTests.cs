using Microsoft.AspNetCore.Http;
using Kronikol.Constants;
using Kronikol.Extensions.AtlasDataApi;
using Kronikol.Extensions.BigQuery;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class HttpContextAccessorOptionsTests
{
    [Fact]
    public void TestTrackingMessageHandler_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var accessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var options = new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test",
            HttpContextAccessor = accessor
        };

        var handler = new TestTrackingMessageHandler(options);

        Assert.True(handler.HasHttpContextAccessor);
    }

    [Fact]
    public async Task TestTrackingMessageHandler_explicit_accessor_takes_precedence_over_options()
    {
        // Each accessor serves a request from a different scenario, so the call the handler logs says which one it read.
        // This fact asserted only that the handler was made, which holds whichever accessor wins.
        var explicitId = Guid.NewGuid().ToString();
        var optionsId = Guid.NewGuid().ToString();
        var options = new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test",
            FixedNameForReceivingService = "Precedence " + explicitId,
            HttpContextAccessor = new TestHttpContextAccessor(ServingRequestOf("Options scenario", optionsId))
        };
        using var invoker = new HttpMessageInvoker(
            new TestTrackingMessageHandler(options, new TestHttpContextAccessor(ServingRequestOf("Explicit scenario", explicitId)))
            { InnerHandler = new OkHandler() });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/precedence"), TestContext.Current.CancellationToken);

        var logged = RequestResponseLogger.RequestAndResponseLogs
            .Where(l => l.Type == RequestResponseType.Request && (l.TestId == explicitId || l.TestId == optionsId))
            .ToArray();
        Assert.Equal(["Explicit scenario"], logged.Select(l => l.TestName));
    }

    private static DefaultHttpContext ServingRequestOf(string name, string id)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = name;
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = id;
        return context;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    [Fact]
    public void TestTrackingMessageHandlerOptions_HttpContextAccessor_defaults_to_null()
    {
        var options = new TestTrackingMessageHandlerOptions();
        Assert.Null(options.HttpContextAccessor);
    }

    // ─── AtlasDataApi accessor fallback (#08) ──────────────────

    [Fact]
    public void AtlasDataApi_handler_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var accessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var options = new AtlasDataApiTrackingMessageHandlerOptions
        {
            ServiceName = "Atlas",
            HttpContextAccessor = accessor
        };

        var handler = new AtlasDataApiTrackingMessageHandler(options);

        Assert.True(handler.HasHttpContextAccessor);
    }

    [Fact]
    public void AtlasDataApi_handler_explicit_accessor_takes_precedence_over_options()
    {
        var optionsAccessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var explicitAccessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var options = new AtlasDataApiTrackingMessageHandlerOptions
        {
            ServiceName = "Atlas",
            HttpContextAccessor = optionsAccessor
        };

        var handler = new AtlasDataApiTrackingMessageHandler(options, httpContextAccessor: explicitAccessor);

        Assert.True(handler.HasHttpContextAccessor);
    }

    [Fact]
    public void AtlasDataApi_has_no_accessor_when_neither_options_nor_parameter()
    {
        var options = new AtlasDataApiTrackingMessageHandlerOptions { ServiceName = "Atlas" };

        var handler = new AtlasDataApiTrackingMessageHandler(options);

        Assert.False(handler.HasHttpContextAccessor);
    }

    // ─── BigQuery accessor fallback (#08) ──────────────────────

    [Fact]
    public void BigQuery_handler_reads_HttpContextAccessor_from_options_when_not_passed_directly()
    {
        var accessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var options = new BigQueryTrackingMessageHandlerOptions
        {
            ServiceName = "BQ",
            HttpContextAccessor = accessor
        };

        var handler = new BigQueryTrackingMessageHandler(options);

        Assert.True(handler.HasHttpContextAccessor);
    }

    [Fact]
    public void BigQuery_handler_explicit_accessor_takes_precedence_over_options()
    {
        var optionsAccessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var explicitAccessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var options = new BigQueryTrackingMessageHandlerOptions
        {
            ServiceName = "BQ",
            HttpContextAccessor = optionsAccessor
        };

        var handler = new BigQueryTrackingMessageHandler(options, httpContextAccessor: explicitAccessor);

        Assert.True(handler.HasHttpContextAccessor);
    }

    [Fact]
    public void BigQuery_has_no_accessor_when_neither_options_nor_parameter()
    {
        var options = new BigQueryTrackingMessageHandlerOptions { ServiceName = "BQ" };

        var handler = new BigQueryTrackingMessageHandler(options);

        Assert.False(handler.HasHttpContextAccessor);
    }

    private class TestHttpContextAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}
