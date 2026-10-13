using Microsoft.AspNetCore.Http;
using Kronikol.Tests.Tracking;
using Kronikol.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Extensions.BigQuery;

namespace Kronikol.Tests.BigQuery;

public class BigQueryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBigQueryTestTracking_Registers_BigQueryTracker()
    {
        var services = new ServiceCollection();

        services.AddBigQueryTestTracking();

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetService<BigQueryTracker>();
        Assert.NotNull(tracker);
    }

    [Fact]
    public void AddBigQueryTestTracking_Registers_As_Singleton()
    {
        var services = new ServiceCollection();

        services.AddBigQueryTestTracking();

        var descriptor = services.Single(d => d.ServiceType == typeof(BigQueryTracker));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddBigQueryTestTracking_Applies_Options()
    {
        var services = new ServiceCollection();

        services.AddBigQueryTestTracking(opts =>
        {
            opts.ServiceName = "MyBQ";
            opts.Verbosity = BigQueryTrackingVerbosity.Summarised;
        });

        var provider = services.BuildServiceProvider();
        var tracker = provider.GetRequiredService<BigQueryTracker>();
        Assert.Contains("MyBQ", tracker.ComponentName);
    }

    [Fact]
    public void AddBigQueryTestTracking_Returns_ServiceCollection_For_Chaining()
    {
        var services = new ServiceCollection();

        var result = services.AddBigQueryTestTracking();

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
        services.AddBigQueryTestTracking(o => o.HttpContextAccessor = RequestHeaderAccessor.For("Options Test", optionsTestId));

        var tracker = services.BuildServiceProvider().GetRequiredService<BigQueryTracker>();
        tracker.LogRequest(new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "my-project", null), "SELECT 1");

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
        services.AddBigQueryTestTracking();

        var tracker = services.BuildServiceProvider().GetRequiredService<BigQueryTracker>();
        tracker.LogRequest(new BigQueryOperationInfo(BigQueryOperation.Query, "query", null, "my-project", null), "SELECT 1");

        Assert.Contains(RequestResponseLogger.RequestAndResponseLogs,
            l => l.TestId == containerTestId && l.AttributionSource == AttributionSource.RequestHeader);
    }
}
