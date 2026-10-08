using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.MongoDB;

/// <summary>
/// Provides extension methods for configuring MongoDB dependency tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class MongoDbServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="MongoDbTrackingSubscriber"/>. It records nothing until a client subscribes it:
    /// <c>settings.ClusterConfigurator = cb =&gt; sp.GetRequiredService&lt;MongoDbTrackingSubscriber&gt;().Subscribe(cb)</c>.
    /// It reads request headers through <see cref="MongoDbTrackingOptions.HttpContextAccessor"/> when that is set, and
    /// otherwise through the container's <see cref="IHttpContextAccessor"/>, if one is registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the tracking options.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddMongoDbTestTracking(
        this IServiceCollection services,
        Action<MongoDbTrackingOptions>? configure = null)
    {
        var options = new MongoDbTrackingOptions();
        configure?.Invoke(options);

        services.AddSingleton(sp => new MongoDbTrackingSubscriber(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()));

        return services;
    }
}