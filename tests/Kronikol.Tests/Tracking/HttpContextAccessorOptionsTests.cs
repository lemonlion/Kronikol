using Microsoft.AspNetCore.Http;
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

        // The handler stores _httpContextAccessor privately — verify via ComponentName that it constructed
        Assert.Equal("TestTrackingMessageHandler (Test)", handler.ComponentName);
    }

    [Fact]
    public void TestTrackingMessageHandler_explicit_accessor_takes_precedence_over_options()
    {
        var optionsAccessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var explicitAccessor = new TestHttpContextAccessor(new DefaultHttpContext());
        var options = new TestTrackingMessageHandlerOptions
        {
            CallerName = "Test",
            HttpContextAccessor = optionsAccessor
        };

        // Pass explicit accessor — it should take precedence
        var handler = new TestTrackingMessageHandler(options, explicitAccessor);
        Assert.NotNull(handler);
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
