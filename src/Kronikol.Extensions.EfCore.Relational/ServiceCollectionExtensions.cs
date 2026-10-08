using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Extensions.EfCore.Relational;

/// <summary>
/// Provides extension methods for configuring Entity Framework Core dependency tracking on <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="SqlTrackingInterceptor"/> singleton that can later be resolved
    /// by <see cref="DbContextOptionsBuilderExtensions.WithSqlTestTracking(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder, IServiceProvider)"/>.
    /// When <see cref="SqlTrackingInterceptorOptions.HttpContextAccessor"/> is set, or else an
    /// <see cref="IHttpContextAccessor"/> is registered in the container, the interceptor
    /// will automatically read test identity from HTTP request headers (set by TTD's tracking
    /// client), enabling SQL tracking inside server-side HTTP request pipelines.
    /// </summary>
    public static IServiceCollection AddSqlTestTracking(
        this IServiceCollection services,
        SqlTrackingInterceptorOptions options)
    {
        services.AddSingleton(sp =>
        {
            var httpContextAccessor = options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>();
            return new SqlTrackingInterceptor(options, httpContextAccessor);
        });
        return services;
    }

    /// <summary>
    /// Removes all service registrations related to a <see cref="DbContext"/> type, including
    /// <c>DbContextOptions&lt;TContext&gt;</c>, <c>DbContextOptions</c>, the context itself,
    /// and <c>IDbContextOptionsConfiguration&lt;TContext&gt;</c> (an internal EF Core type
    /// that survives a simple <c>RemoveAll&lt;DbContextOptions&lt;T&gt;&gt;()</c>).
    /// <para>
    /// Call this in <c>ConfigureTestServices</c> before re-registering the DbContext with
    /// a tracking interceptor to ensure no production configuration callbacks survive.
    /// </para>
    /// </summary>
    public static IServiceCollection RemoveDbContext<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        var toRemove = services
            .Where(d =>
                d.ServiceType == typeof(TContext) ||
                d.ServiceType == typeof(DbContextOptions<TContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                (d.ServiceType.IsGenericType &&
                 d.ServiceType.GetGenericArguments().Contains(typeof(TContext))))
            .ToList();

        foreach (var descriptor in toRemove)
            services.Remove(descriptor);

        return services;
    }
}