using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.AtlasDataApi;

/// <summary>
/// Provides extension methods for configuring MongoDB Atlas Data API dependency tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class AtlasDataApiServiceCollectionExtensions
{
    public static IServiceCollection AddAtlasDataApiTestTracking(
        this IServiceCollection services,
        Action<AtlasDataApiTrackingMessageHandlerOptions>? configure = null)
    {
        var options = new AtlasDataApiTrackingMessageHandlerOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton(sp =>
            new AtlasDataApiTrackingMessageHandler(options, httpContextAccessor: options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()));

        return services;
    }
}