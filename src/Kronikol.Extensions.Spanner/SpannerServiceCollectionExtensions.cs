using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.Spanner;

/// <summary>
/// Provides extension methods for configuring Google Cloud Spanner dependency tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class SpannerServiceCollectionExtensions
{
    public static IServiceCollection AddSpannerTestTracking(
        this IServiceCollection services,
        Action<SpannerTrackingOptions>? configure = null)
    {
        var options = new SpannerTrackingOptions();
        configure?.Invoke(options);

        services.AddSingleton(sp => new SpannerTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()));

        return services;
    }
}