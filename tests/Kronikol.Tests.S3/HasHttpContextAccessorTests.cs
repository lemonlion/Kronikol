using Kronikol.Extensions.S3;
using Kronikol.Tracking;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Tests.S3;

/// <summary>
/// The component reports the accessor it holds, so the diagnostic page's HttpContextAccessor column counts it
/// (plans/GRPC_IDENTITY_PROPAGATION_PLAN.md section 4.4, T29). Until this release it took one and reported none.
/// </summary>
public class HasHttpContextAccessorTests
{
    [Fact]
    public void HasHttpContextAccessor_reports_the_accessor_from_the_constructor_or_the_options()
    {
        IHttpContextAccessor accessor = new StubAccessor();

        Assert.True(((ITrackingComponent)new S3TrackingMessageHandler(new S3TrackingMessageHandlerOptions(), null, accessor)).HasHttpContextAccessor);
        Assert.True(((ITrackingComponent)new S3TrackingMessageHandler(new S3TrackingMessageHandlerOptions { HttpContextAccessor = new StubAccessor() })).HasHttpContextAccessor);
        Assert.False(((ITrackingComponent)new S3TrackingMessageHandler(new S3TrackingMessageHandlerOptions())).HasHttpContextAccessor);
    }

    private sealed class StubAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
