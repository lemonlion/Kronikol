using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

// Clears the process-wide TrackingComponentRegistry, which other classes read: they share one collection.
[Collection("TestCorrelationStore")]
public class MongoDbServiceCollectionExtensionsTests : IDisposable
{
    public MongoDbServiceCollectionExtensionsTests()
    {
        TrackingComponentRegistry.Clear();
    }

    public void Dispose()
    {
        TrackingComponentRegistry.Clear();
    }

    [Fact]
    public void AddMongoDbTestTracking_Registers_MongoDbTrackingSubscriber()
    {
        var services = new ServiceCollection();

        services.AddMongoDbTestTracking();

        var provider = services.BuildServiceProvider();
        var subscriber = provider.GetService<MongoDbTrackingSubscriber>();
        Assert.NotNull(subscriber);
    }

    [Fact]
    public void AddMongoDbTestTracking_Registers_As_Singleton()
    {
        var services = new ServiceCollection();

        services.AddMongoDbTestTracking();

        var descriptor = services.Single(d => d.ServiceType == typeof(MongoDbTrackingSubscriber));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void AddMongoDbTestTracking_Applies_Options()
    {
        var services = new ServiceCollection();

        services.AddMongoDbTestTracking(opts =>
        {
            opts.ServiceName = "MyMongo";
            opts.Verbosity = MongoDbTrackingVerbosity.Summarised;
        });

        var provider = services.BuildServiceProvider();
        var subscriber = provider.GetRequiredService<MongoDbTrackingSubscriber>();
        Assert.Contains("MyMongo", subscriber.ComponentName);
    }

    [Fact]
    public void AddMongoDbTestTracking_Returns_ServiceCollection_For_Chaining()
    {
        var services = new ServiceCollection();

        var result = services.AddMongoDbTestTracking();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddMongoDbTestTracking_prefers_the_options_accessor_to_the_containers()
    {
        var optionsTestId = Guid.NewGuid().ToString();
        var containerTestId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(HeaderAccessor.For("Container Test", containerTestId));
        services.AddMongoDbTestTracking(o => o.HttpContextAccessor = HeaderAccessor.For("Options Test", optionsTestId));

        var subscriber = services.BuildServiceProvider().GetRequiredService<MongoDbTrackingSubscriber>();
        subscriber.OnCommandStarted(HeaderAccessor.FindStarted());

        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == optionsTestId);
        Assert.Equal(AttributionSource.RequestHeader, log.AttributionSource);
        Assert.DoesNotContain(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == containerTestId);
    }

    [Fact]
    public void AddMongoDbTestTracking_uses_the_containers_accessor_when_the_options_carry_none()
    {
        var containerTestId = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(HeaderAccessor.For("Container Test", containerTestId));
        services.AddMongoDbTestTracking();

        var subscriber = services.BuildServiceProvider().GetRequiredService<MongoDbTrackingSubscriber>();
        subscriber.OnCommandStarted(HeaderAccessor.FindStarted());

        Assert.True(subscriber.HasHttpContextAccessor);
        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.TestId == containerTestId);
        Assert.Equal(AttributionSource.RequestHeader, log.AttributionSource);
    }
}
