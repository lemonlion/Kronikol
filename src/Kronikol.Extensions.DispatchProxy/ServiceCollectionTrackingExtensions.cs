using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kronikol.Tracking;

namespace Kronikol.Extensions.DispatchProxy;

/// <summary>
/// Provides extension methods for enabling DispatchProxy integration with test tracking.
/// </summary>
public static class ServiceCollectionTrackingExtensions
{
    /// <summary>
    /// Replaces the existing registration for <typeparamref name="TService"/> with a
    /// <see cref="TrackingProxy{T}"/> that wraps the given implementation and records
    /// interactions for diagrams.
    /// </summary>
    public static IServiceCollection ReplaceWithTracked<TService>(
        this IServiceCollection services,
        TService implementation,
        TrackingProxyOptions options) where TService : class
    {
        var proxy = TrackingProxy<TService>.Create(implementation, options);

        // Remove existing registrations; a keyed registration of the same type is another service, and stays.
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(TService) && !services[i].IsKeyedService)
                services.RemoveAt(i);
        }

        services.AddSingleton(proxy);
        return services;
    }

    /// <summary>
    /// Replaces the existing registration for <typeparamref name="TService"/> with a
    /// <see cref="TrackingProxy{T}"/> that wraps the given implementation.
    /// Simplified overload using just service name.
    /// </summary>
    public static IServiceCollection ReplaceWithTracked<TService>(
        this IServiceCollection services,
        TService implementation,
        string serviceName,
        Func<(string Name, string Id)>? testInfoFetcher = null,
        TrackingLogMode logMode = TrackingLogMode.Immediate,
        IHttpContextAccessor? httpContextAccessor = null,
        string? dependencyCategory = null) where TService : class
    {
        return services.ReplaceWithTracked(implementation, new TrackingProxyOptions
        {
            ServiceName = serviceName,
            CurrentTestInfoFetcher = testInfoFetcher,
            LogMode = logMode,
            HttpContextAccessor = httpContextAccessor,
            DependencyCategory = dependencyCategory
        });
    }
}