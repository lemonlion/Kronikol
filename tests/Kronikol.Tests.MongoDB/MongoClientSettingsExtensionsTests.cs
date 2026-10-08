using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using Kronikol.Extensions.MongoDB;
using Kronikol.Tracking;

namespace Kronikol.Tests.MongoDB;

// Reads the process-wide TrackingComponentRegistry, which MongoDbTrackingSubscriberTests and
// MongoDbServiceCollectionExtensionsTests clear: one collection keeps them from running beside each other.
[Collection("TestCorrelationStore")]
public class MongoClientSettingsExtensionsTests
{
    [Fact]
    public void WithTestTracking_ReturnsSameSettingsInstance()
    {
        var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
        var options = new MongoDbTrackingOptions();

        var result = settings.WithTestTracking(options);

        Assert.Same(settings, result);
    }

    [Fact]
    public void WithTestTracking_SetsClusterConfigurator()
    {
        var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
        var options = new MongoDbTrackingOptions();

        settings.WithTestTracking(options);

        Assert.NotNull(settings.ClusterConfigurator);
    }

    [Fact]
    public void WithTestTracking_PreservesExistingClusterConfigurator()
    {
        var settings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017");
        var earlierConfiguratorRan = false;
        settings.ClusterConfigurator = _ => earlierConfiguratorRan = true;

        settings.WithTestTracking(new MongoDbTrackingOptions());
        settings.ClusterConfigurator(new ClusterBuilder());

        Assert.True(earlierConfiguratorRan);
    }

    [Fact]
    public void WithTestTracking_passes_the_options_accessor_to_its_subscriber()
    {
        var serviceName = "MongoDB " + Guid.NewGuid();
        var headerTestId = Guid.NewGuid().ToString();
        var options = new MongoDbTrackingOptions
        {
            ServiceName = serviceName,
            CurrentTestInfoFetcher = () => ("Fetcher Test", Guid.NewGuid().ToString()),
            HttpContextAccessor = HeaderAccessor.For("Header Test", headerTestId),
        };

        MongoClientSettings.FromConnectionString("mongodb://localhost:27017").WithTestTracking(options);

        var subscriber = TrackingComponentRegistry.GetRegisteredComponents().OfType<MongoDbTrackingSubscriber>()
            .Single(s => s.ComponentName == $"MongoDbTrackingSubscriber ({serviceName})");
        Assert.True(subscriber.HasHttpContextAccessor);
        subscriber.OnCommandStarted(HeaderAccessor.FindStarted());
        var log = Assert.Single(RequestResponseLogger.RequestAndResponseLogs, l => l.ServiceName == serviceName);
        Assert.Equal(headerTestId, log.TestId);
        Assert.Equal(AttributionSource.RequestHeader, log.AttributionSource);
    }
}
