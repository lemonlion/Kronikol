using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Extensions.Bigtable;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;

namespace Kronikol.Tests.Bigtable;

public class BigtableServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBigtableTestTracking_Registers_BigtableTracker()
    {
        var services = new ServiceCollection();

        services.AddBigtableTestTracking();

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetService<BigtableTracker>();
        Assert.NotNull(tracker);
    }

    [Fact]
    public void AddBigtableTestTracking_Registers_As_Singleton()
    {
        var services = new ServiceCollection();

        services.AddBigtableTestTracking();

        var descriptor = services.Single(d => d.ServiceType == typeof(BigtableTracker));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddBigtableTestTracking_Applies_Options()
    {
        var services = new ServiceCollection();

        services.AddBigtableTestTracking(opts =>
        {
            opts.ServiceName = "MyBigtable";
            opts.Verbosity = BigtableTrackingVerbosity.Summarised;
        });

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetRequiredService<BigtableTracker>();
        Assert.Contains("MyBigtable", tracker.ComponentName);
    }

    [Fact]
    public void AddBigtableTestTracking_Returns_ServiceCollection_For_Chaining()
    {
        var services = new ServiceCollection();

        var result = services.AddBigtableTestTracking();

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
        services.AddBigtableTestTracking(o => o.HttpContextAccessor = RequestHeaderAccessor.For("Options Test", optionsTestId));

        var tracker = services.BuildServiceProvider().GetRequiredService<BigtableTracker>();
        tracker.LogRequest(new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t"), "filter");

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
        services.AddBigtableTestTracking();

        var tracker = services.BuildServiceProvider().GetRequiredService<BigtableTracker>();
        tracker.LogRequest(new BigtableOperationInfo(BigtableOperation.ReadRows, "projects/p/instances/i/tables/t"), "filter");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == containerTestId && l.AttributionSource == AttributionSource.RequestHeader);
    }
}
