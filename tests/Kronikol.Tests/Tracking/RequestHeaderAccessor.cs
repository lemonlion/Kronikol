using Kronikol.Constants;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Tests.Tracking;

/// <summary>
/// An accessor holding one request that carries the two test-tracking headers. Each instance keeps its own request:
/// <see cref="HttpContextAccessor"/> keeps one per async flow, so two of those in one test would share it. Linked into
/// the extension test projects that check which accessor a tracker reads.
/// </summary>
internal sealed class RequestHeaderAccessor(HttpContext? httpContext) : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; } = httpContext;

    public static RequestHeaderAccessor For(string testName, string testId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestNameHeader] = testName;
        context.Request.Headers[TestTrackingHttpHeaders.CurrentTestIdHeader] = testId;
        return new RequestHeaderAccessor(context);
    }
}
