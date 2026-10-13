using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.BigQuery;

/// <summary>
/// Provides extension methods for configuring Google BigQuery dependency tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class BigQueryServiceCollectionExtensions
{
    public static IServiceCollection AddBigQueryTestTracking(
        this IServiceCollection services,
        Action<BigQueryTrackingMessageHandlerOptions>? configure = null)
    {
        var options = new BigQueryTrackingMessageHandlerOptions();
        configure?.Invoke(options);

        services.AddSingleton(sp => new BigQueryTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>()));

        return services;
    }
}