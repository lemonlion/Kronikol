using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.Bigtable;

/// <summary>
/// Provides extension methods for configuring Google Cloud Bigtable dependency tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class BigtableServiceCollectionExtensions
{
    public static IServiceCollection AddBigtableTestTracking(
        this IServiceCollection services,
        Action<BigtableTrackingOptions>? configure = null)
    {
        var options = new BigtableTrackingOptions();
        configure?.Invoke(options);

        services.AddSingleton(sp => new BigtableTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()));

        return services;
    }
}