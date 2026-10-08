using global::MongoDB.Driver;

namespace Kronikol.Extensions.MongoDB;

/// <summary>
/// Provides extension methods for configuring MongoDB client options to enable test tracking.
/// </summary>
public static class MongoClientSettingsExtensions
{
    /// <summary>
    /// Adds MongoDB command tracking for test diagrams: a <see cref="MongoDbTrackingSubscriber"/> built from
    /// <paramref name="options"/> subscribes to the client's command events, after any
    /// <see cref="MongoClientSettings.ClusterConfigurator"/> already set. In a host, set
    /// <see cref="MongoDbTrackingOptions.HttpContextAccessor"/> to the host's accessor, so that a command run while the
    /// host serves a request lands in the scenario the request's headers name.
    /// </summary>
    /// <param name="settings">The settings the client will be built from.</param>
    /// <param name="options">The tracking options, <see cref="MongoDbTrackingOptions.HttpContextAccessor"/> included.</param>
    /// <returns>The same <paramref name="settings"/>.</returns>
    public static MongoClientSettings WithTestTracking(
        this MongoClientSettings settings,
        MongoDbTrackingOptions options)
    {
        var subscriber = new MongoDbTrackingSubscriber(options);

        var existingConfigurator = settings.ClusterConfigurator;
        settings.ClusterConfigurator = builder =>
        {
            existingConfigurator?.Invoke(builder);
            subscriber.Subscribe(builder);
        };

        return settings;
    }
}