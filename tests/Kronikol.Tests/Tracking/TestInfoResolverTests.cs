using Microsoft.AspNetCore.Http;
using Kronikol.Constants;
using Kronikol.Tracking;

namespace Kronikol.Tests.Tracking;

[Collection("DiagramsFetcher")]
public class TestInfoResolverTests
{
    [Fact]
    public void ResolveWithSource_says_which_level_answered()
    {
        Assert.Equal(AttributionSource.TestContext, TestInfoResolver.ResolveWithSource(null, () => ("T", "t-1"))!.Value.Source);

        using (TestIdentityScope.Begin("S", "s-1"))
            Assert.Equal(AttributionSource.Scope, TestInfoResolver.ResolveWithSource(null, (Func<(string, string)>?)null)!.Value.Source);

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext!.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "H";
        accessor.HttpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "h-1";
        var fromHeaders = TestInfoResolver.ResolveWithSource(accessor, () => ("T", "t-1"))!.Value;
        Assert.Equal(("h-1", AttributionSource.RequestHeader, true), (fromHeaders.Id, fromHeaders.Source, fromHeaders.IsAttributed));
    }

    [Fact]
    public void A_detached_flow_resolves_to_no_scenario_unless_a_scope_names_one()
    {
        // A web host built inside a test starts its hosted services under the test's context. Detached,
        // that inherited context counts for nothing; a scope a message carried still does.
        Func<(string, string)> inherited = () => ("Inherited", "inherited-1");

        using (TestIdentityScope.Detach())
        {
            Assert.Null(TestInfoResolver.Resolve(null, inherited));
            Assert.Null(TestInfoResolver.ResolveWithSource(null, inherited));

            using (TestIdentityScope.Begin("Correlated", "c-1"))
            {
                var who = TestInfoResolver.ResolveWithSource(null, inherited)!.Value;
                Assert.Equal(("c-1", AttributionSource.Scope), (who.Id, who.Source));
            }
        }

        Assert.Equal("inherited-1", TestInfoResolver.Resolve(null, inherited)!.Value.Id);
    }

    [Fact]
    public void A_detached_flow_working_on_a_scenarios_document_resolves_to_that_scenario_as_document_flow()
    {
        // Between the claim of a scenario's outbox row and the status update, the dispatch call names no
        // document; the flow's open window names the scenario. A scope a message carried still wins.
        using (TestIdentityScope.Detach())
        {
            DocumentOwnership.AfterWrite(new TestIdentity("Pay", "s-1", AttributionSource.DocumentOwner));

            var who = TestInfoResolver.ResolveWithSource(null, () => ("Inherited", "inherited-1"))!.Value;
            Assert.Equal(("s-1", AttributionSource.DocumentFlow, true), (who.Id, who.Source, who.IsAttributed));

            using (TestIdentityScope.Begin("Correlated", "c-1"))
                Assert.Equal(AttributionSource.Scope, TestInfoResolver.ResolveWithSource(null, (Func<(string, string)>?)null)!.Value.Source);
        }
    }

    [Fact]
    public void Background_capture_hands_back_the_unknown_identity_with_its_reason()
    {
        RequestResponseLogger.CaptureBackground = true;
        try
        {
            var none = TestInfoResolver.ResolveWithSource(null, (Func<(string, string)>?)null)!.Value;
            Assert.Equal((TestIdentityScope.UnknownTestId, AttributionSource.None, false), (none.Id, none.Source, none.IsAttributed));

            using (TestIdentityScope.Detach())
            {
                Assert.Equal(AttributionSource.Detached, TestInfoResolver.ResolveWithSource(null, () => ("Inherited", "inherited-1"))!.Value.Source);
                Assert.Equal(TestIdentityScope.UnknownTestId, TestInfoResolver.Resolve(null, () => ("Inherited", "inherited-1"))!.Value.Id);
            }
        }
        finally
        {
            RequestResponseLogger.CaptureBackground = false;
        }
    }

    [Fact]
    public void Returns_null_when_both_accessor_and_delegate_are_null()
    {
        var result = TestInfoResolver.Resolve(null, (Func<(string, string)>?)null);

        Assert.Null(result);
    }

    [Fact]
    public void Returns_delegate_result_when_no_http_context()
    {
        var result = TestInfoResolver.Resolve(null, () => ("TestName", "test-id-123"));

        Assert.NotNull(result);
        Assert.Equal("TestName", result.Value.Name);
        Assert.Equal("test-id-123", result.Value.Id);
    }

    [Fact]
    public void Returns_null_when_delegate_throws()
    {
        Func<(string, string)> throwingFetcher = () => throw new InvalidOperationException("No scenario context");

        var result = TestInfoResolver.Resolve(null, throwingFetcher);

        Assert.Null(result);
    }

    [Fact]
    public void Returns_http_headers_when_present()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HeaderTest";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "header-id-456";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var result = TestInfoResolver.Resolve(accessor, () => ("DelegateTest", "delegate-id"));

        Assert.NotNull(result);
        Assert.Equal("HeaderTest", result.Value.Name);
        Assert.Equal("header-id-456", result.Value.Id);
    }

    [Fact]
    public void Prefers_http_headers_over_delegate()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "FromHeaders";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "from-headers";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var result = TestInfoResolver.Resolve(accessor, () => ("FromDelegate", "from-delegate"));

        Assert.NotNull(result);
        Assert.Equal("FromHeaders", result.Value.Name);
        Assert.Equal("from-headers", result.Value.Id);
    }

    [Fact]
    public void Falls_back_to_delegate_when_headers_missing()
    {
        var httpContext = new DefaultHttpContext(); // no test headers set
        var accessor = new FakeHttpContextAccessor(httpContext);

        var result = TestInfoResolver.Resolve(accessor, () => ("FallbackTest", "fallback-id"));

        Assert.NotNull(result);
        Assert.Equal("FallbackTest", result.Value.Name);
        Assert.Equal("fallback-id", result.Value.Id);
    }

    [Fact]
    public void Falls_back_to_delegate_when_only_name_header_present()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "OnlyName";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var result = TestInfoResolver.Resolve(accessor, () => ("Delegate", "delegate-id"));

        Assert.NotNull(result);
        Assert.Equal("Delegate", result.Value.Name);
        Assert.Equal("delegate-id", result.Value.Id);
    }

    [Fact]
    public void Falls_back_to_delegate_when_http_context_is_null()
    {
        var accessor = new FakeHttpContextAccessor(null);

        var result = TestInfoResolver.Resolve(accessor, () => ("DelegateOnly", "delegate-only"));

        Assert.NotNull(result);
        Assert.Equal("DelegateOnly", result.Value.Name);
        Assert.Equal("delegate-only", result.Value.Id);
    }

    [Fact]
    public void Returns_null_when_headers_missing_and_delegate_is_null()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new FakeHttpContextAccessor(httpContext);

        var result = TestInfoResolver.Resolve(accessor, (Func<(string, string)>?)null);

        Assert.Null(result);
    }

    [Fact]
    public void Returns_null_when_headers_missing_and_delegate_throws()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new FakeHttpContextAccessor(httpContext);
        Func<(string, string)> throwingFetcher = () => throw new InvalidOperationException();

        var result = TestInfoResolver.Resolve(accessor, throwingFetcher);

        Assert.Null(result);
    }

    [Fact]
    public void Nullable_overload_returns_delegate_result()
    {
        Func<(string, string)?> fetcher = () => ("NullableTest", "nullable-id");

        var result = TestInfoResolver.Resolve(null, fetcher);

        Assert.NotNull(result);
        Assert.Equal("NullableTest", result.Value.Name);
        Assert.Equal("nullable-id", result.Value.Id);
    }

    [Fact]
    public void Nullable_overload_returns_null_when_delegate_returns_null()
    {
        Func<(string, string)?> fetcher = () => null;

        var result = TestInfoResolver.Resolve(null, fetcher);

        Assert.Null(result);
    }

    [Fact]
    public void Nullable_overload_prefers_http_headers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HeaderWins";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "header-wins";
        var accessor = new FakeHttpContextAccessor(httpContext);
        Func<(string, string)?> fetcher = () => ("DelegateLoses", "delegate-loses");

        var result = TestInfoResolver.Resolve(accessor, fetcher);

        Assert.NotNull(result);
        Assert.Equal("HeaderWins", result.Value.Name);
        Assert.Equal("header-wins", result.Value.Id);
    }

    #region CreateHttpFallbackFetcher

    [Fact]
    public void CreateHttpFallbackFetcher_returns_fallback_when_accessor_is_null()
    {
        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(null, () => ("Fallback", "fb-id"));

        var result = fetcher();

        Assert.Equal("Fallback", result.Name);
        Assert.Equal("fb-id", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_returns_fallback_when_http_context_is_null()
    {
        var accessor = new FakeHttpContextAccessor(null);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("Fallback", "fb-id"));

        var result = fetcher();

        Assert.Equal("Fallback", result.Name);
        Assert.Equal("fb-id", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_returns_fallback_when_headers_missing()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new FakeHttpContextAccessor(httpContext);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("Fallback", "fb-id"));

        var result = fetcher();

        Assert.Equal("Fallback", result.Name);
        Assert.Equal("fb-id", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_returns_http_headers_when_present()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HttpTest";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "http-id-1";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("Fallback", "fb-id"));

        var result = fetcher();

        Assert.Equal("HttpTest", result.Name);
        Assert.Equal("http-id-1", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_prefers_http_headers_over_fallback()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "FromHeaders";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "from-headers";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("FromDelegate", "from-delegate"));

        var result = fetcher();

        Assert.Equal("FromHeaders", result.Name);
        Assert.Equal("from-headers", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_falls_back_when_only_name_header_present()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "OnlyName";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("Fallback", "fb-id"));

        var result = fetcher();

        Assert.Equal("Fallback", result.Name);
        Assert.Equal("fb-id", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_falls_back_when_only_id_header_present()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "only-id";
        var accessor = new FakeHttpContextAccessor(httpContext);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("Fallback", "fb-id"));

        var result = fetcher();

        Assert.Equal("Fallback", result.Name);
        Assert.Equal("fb-id", result.Id);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_propagates_fallback_exception_when_no_http_context()
    {
        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(null, () => throw new InvalidOperationException("No context"));

        Action act = () => fetcher();
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void CreateHttpFallbackFetcher_returns_delegate_that_reflects_changing_http_context()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new FakeHttpContextAccessor(httpContext);

        var fetcher = TestInfoResolver.CreateHttpFallbackFetcher(accessor, () => ("Fallback", "fb-id"));

        // Initially no headers — uses fallback
        var result1 = fetcher();
        Assert.Equal("Fallback", result1.Name);

        // Add headers — now uses httpContext
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "Dynamic";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "dynamic-id";
        var result2 = fetcher();
        Assert.Equal("Dynamic", result2.Name);
        Assert.Equal("dynamic-id", result2.Id);
    }

    #endregion

    #region TestIdentityScope fallback

    [Fact]
    public void Falls_back_to_TestIdentityScope_when_no_http_and_no_delegate()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            var result = TestInfoResolver.Resolve(null, (Func<(string, string)>?)null);

            Assert.NotNull(result);
            Assert.Equal("ScopeTest", result.Value.Name);
            Assert.Equal("scope-id", result.Value.Id);
        }
    }

    [Fact]
    public void Falls_back_to_TestIdentityScope_when_delegate_throws()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            Func<(string, string)> throwingFetcher = () => throw new InvalidOperationException();

            var result = TestInfoResolver.Resolve(null, throwingFetcher);

            Assert.NotNull(result);
            Assert.Equal("ScopeTest", result.Value.Name);
            Assert.Equal("scope-id", result.Value.Id);
        }
    }

    [Fact]
    public void Prefers_delegate_over_TestIdentityScope()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            var result = TestInfoResolver.Resolve(null, () => ("DelegateTest", "delegate-id"));

            Assert.NotNull(result);
            Assert.Equal("DelegateTest", result.Value.Name);
            Assert.Equal("delegate-id", result.Value.Id);
        }
    }

    [Fact]
    public void Prefers_http_headers_over_TestIdentityScope()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HttpTest";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "http-id";
        var accessor = new FakeHttpContextAccessor(httpContext);

        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            var result = TestInfoResolver.Resolve(accessor, (Func<(string, string)>?)null);

            Assert.NotNull(result);
            Assert.Equal("HttpTest", result.Value.Name);
            Assert.Equal("http-id", result.Value.Id);
        }
    }

    [Fact]
    public void Nullable_overload_falls_back_to_TestIdentityScope()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            Func<(string, string)?> fetcher = () => null;

            var result = TestInfoResolver.Resolve(null, fetcher);

            Assert.NotNull(result);
            Assert.Equal("ScopeTest", result.Value.Name);
            Assert.Equal("scope-id", result.Value.Id);
        }
    }

    [Fact]
    public void Returns_null_when_no_http_no_delegate_and_no_scope()
    {
        TestIdentityScope.Reset();
        TestIdentityScope.ClearGlobalFallback();

        var result = TestInfoResolver.Resolve(null, (Func<(string, string)>?)null);

        Assert.Null(result);
    }

    #endregion

    #region GlobalFallback resolution

    [Fact]
    public void Falls_back_to_GlobalFallback_when_no_http_no_delegate_and_no_scope()
    {
        TestIdentityScope.Reset();
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");

            var result = TestInfoResolver.Resolve(null, (Func<(string, string)>?)null);

            Assert.NotNull(result);
            Assert.Equal("GlobalTest", result.Value.Name);
            Assert.Equal("global-id", result.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void Prefers_TestIdentityScope_Current_over_GlobalFallback()
    {
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");

            using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
            {
                var result = TestInfoResolver.Resolve(null, (Func<(string, string)>?)null);

                Assert.NotNull(result);
                Assert.Equal("ScopeTest", result.Value.Name);
                Assert.Equal("scope-id", result.Value.Id);
            }
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void Prefers_delegate_over_GlobalFallback()
    {
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");

            var result = TestInfoResolver.Resolve(null, () => ("DelegateTest", "delegate-id"));

            Assert.NotNull(result);
            Assert.Equal("DelegateTest", result.Value.Name);
            Assert.Equal("delegate-id", result.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void Prefers_http_headers_over_GlobalFallback()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = "HttpTest";
        httpContext.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = "http-id";
        var accessor = new FakeHttpContextAccessor(httpContext);

        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");

            var result = TestInfoResolver.Resolve(accessor, (Func<(string, string)>?)null);

            Assert.NotNull(result);
            Assert.Equal("HttpTest", result.Value.Name);
            Assert.Equal("http-id", result.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void Falls_back_to_GlobalFallback_when_delegate_throws_and_no_scope()
    {
        TestIdentityScope.Reset();
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");
            Func<(string, string)> throwingFetcher = () => throw new InvalidOperationException();

            var result = TestInfoResolver.Resolve(null, throwingFetcher);

            Assert.NotNull(result);
            Assert.Equal("GlobalTest", result.Value.Name);
            Assert.Equal("global-id", result.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void Nullable_overload_falls_back_to_GlobalFallback()
    {
        TestIdentityScope.Reset();
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");
            Func<(string, string)?> fetcher = () => null;

            var result = TestInfoResolver.Resolve(null, fetcher);

            Assert.NotNull(result);
            Assert.Equal("GlobalTest", result.Value.Name);
            Assert.Equal("global-id", result.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    #endregion

    #region UnknownIdentity fallthrough

    [Fact]
    public void Falls_through_to_TestIdentityScope_when_delegate_returns_UnknownIdentity()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            Func<(string, string)> fetcher = () => TestIdentityScope.UnknownIdentity;

            var result = TestInfoResolver.Resolve(null, fetcher);

            Assert.NotNull(result);
            Assert.Equal("ScopeTest", result.Value.Name);
            Assert.Equal("scope-id", result.Value.Id);
        }
    }

    [Fact]
    public void Falls_through_to_GlobalFallback_when_delegate_returns_UnknownIdentity_and_no_scope()
    {
        TestIdentityScope.Reset();
        try
        {
            TestIdentityScope.SetGlobalFallback("GlobalTest", "global-id");
            Func<(string, string)> fetcher = () => TestIdentityScope.UnknownIdentity;

            var result = TestInfoResolver.Resolve(null, fetcher);

            Assert.NotNull(result);
            Assert.NotEqual(TestIdentityScope.UnknownTestId, result.Value.Id);
        }
        finally
        {
            TestIdentityScope.ClearGlobalFallback();
        }
    }

    [Fact]
    public void Does_not_return_UnknownIdentity_from_delegate()
    {
        Func<(string, string)> fetcher = () => TestIdentityScope.UnknownIdentity;

        var result = TestInfoResolver.Resolve(null, fetcher);

        // Result should either be null (no fallbacks) or from scope/globalFallback — not UnknownIdentity
        if (result is not null)
            Assert.NotEqual(TestIdentityScope.UnknownTestId, result.Value.Id);
    }

    [Fact]
    public void Nullable_overload_falls_through_when_delegate_returns_UnknownIdentity()
    {
        using (TestIdentityScope.Begin("ScopeTest", "scope-id"))
        {
            Func<(string, string)?> fetcher = () => TestIdentityScope.UnknownIdentity;

            var result = TestInfoResolver.Resolve(null, fetcher);

            Assert.NotNull(result);
            Assert.Equal("ScopeTest", result.Value.Name);
            Assert.Equal("scope-id", result.Value.Id);
        }
    }

    #endregion

    private class FakeHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}
