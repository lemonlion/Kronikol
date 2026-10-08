using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Extensions.Spanner;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Spanner;

public class SpannerServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSpannerTestTracking_Registers_SpannerTracker()
    {
        var services = new ServiceCollection();

        services.AddSpannerTestTracking();

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetService<SpannerTracker>();
        Assert.NotNull(tracker);
    }

    [Fact]
    public void AddSpannerTestTracking_Registers_As_Singleton()
    {
        var services = new ServiceCollection();

        services.AddSpannerTestTracking();

        var descriptor = services.Single(d => d.ServiceType == typeof(SpannerTracker));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddSpannerTestTracking_Applies_Options()
    {
        var services = new ServiceCollection();

        services.AddSpannerTestTracking(opts =>
        {
            opts.ServiceName = "MySpanner";
            opts.Verbosity = SpannerTrackingVerbosity.Summarised;
        });

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetRequiredService<SpannerTracker>();
        Assert.Contains("MySpanner", tracker.ComponentName);
    }

    [Fact]
    public void AddSpannerTestTracking_Returns_ServiceCollection_For_Chaining()
    {
        var services = new ServiceCollection();

        var result = services.AddSpannerTestTracking();

        Assert.Same(services, result);
    }

    // ─── HttpContextAccessor: the options' first, then the container's ───

    [Fact]
    public void Registration_prefers_the_options_accessor_to_the_containers()
    {
        var optionsTestId = Guid.NewGuid().ToString();
        var containerTestId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(RequestHeaderAccessor.For("Container Test", containerTestId));
        services.AddSpannerTestTracking(o => o.HttpContextAccessor = RequestHeaderAccessor.For("Options Test", optionsTestId));

        var tracker = services.BuildServiceProvider().GetRequiredService<SpannerTracker>();
        tracker.LogRequest(new SpannerOperationInfo(SpannerOperation.Query, "Users"), "SELECT * FROM Users");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == optionsTestId && l.AttributionSource == AttributionSource.RequestHeader);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == containerTestId);
    }

    [Fact]
    public void Registration_uses_the_containers_accessor_when_the_options_carry_none()
    {
        var containerTestId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(RequestHeaderAccessor.For("Container Test", containerTestId));
        services.AddSpannerTestTracking();

        var tracker = services.BuildServiceProvider().GetRequiredService<SpannerTracker>();
        tracker.LogRequest(new SpannerOperationInfo(SpannerOperation.Query, "Users"), "SELECT * FROM Users");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == containerTestId && l.AttributionSource == AttributionSource.RequestHeader);
    }
}
