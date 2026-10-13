using Google.Cloud.PubSub.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;

namespace Kronikol.Extensions.PubSub;

/// <summary>
/// Extension methods for registering PubSub test tracking via dependency injection.
/// </summary>
public static class PubSubServiceCollectionExtensions
{
    /// <summary>
    /// Decorates all existing <see cref="PublisherClient"/> registrations with
    /// <see cref="TrackingPublisherClient"/> and all existing <see cref="SubscriberClient"/>
    /// registrations with <see cref="TrackingSubscriberClient"/> for test diagram tracking.
    /// Also registers a singleton <see cref="PubSubTracker"/>.
    /// <para>No-op when no matching registrations exist.</para>
    /// </summary>
    public static IServiceCollection AddPubSubTestTracking(
        this IServiceCollection services,
        Action<PubSubTrackingOptions>? configure = null)
    {
        var options = new PubSubTrackingOptions();
        configure?.Invoke(options);

        services.AddSingleton(sp => new PubSubTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()));

        services.DecorateAll<PublisherClient>((sp, inner) =>
        {
            options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();
            return new TrackingPublisherClient(inner, options, options.HttpContextAccessor);
        });

        services.DecorateAll<SubscriberClient>((sp, inner) =>
        {
            options.HttpContextAccessor ??= sp.GetService<IHttpContextAccessor>();
            return new TrackingSubscriberClient(inner, options, options.HttpContextAccessor);
        });

        return services;
    }
}
