using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;

namespace Kronikol.Tracking;

/// <summary>
/// DI extension methods for registering HTTP and message tracking in the service collection.
/// </summary>
public static class ServiceCollectionHelper
{
    public static IServiceCollection TrackDependenciesForDiagrams(IServiceCollection services, TestTrackingMessageHandlerOptions options)
    {
        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, TrackingHttpMessageHandlerBuilderFilter>();

        return services;
    }

    public static IServiceCollection TrackMessagesForDiagrams(
        this IServiceCollection services,
        string callingServiceName,
        JsonSerializerOptions? serializerOptions = null,
        Func<(string Name, string Id)>? testInfoFallback = null)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(sp => new MessageTracker(
            sp.GetRequiredService<IHttpContextAccessor>(),
            callingServiceName,
            serializerOptions,
            testInfoFallback));

        return services;
    }

    public static IServiceCollection TrackMessagesForDiagrams(
        this IServiceCollection services,
        MessageTrackerOptions options)
    {
        if (options.UseHttpContextCorrelation)
        {
            services.AddHttpContextAccessor();
            services.AddSingleton(sp => new MessageTracker(options, sp.GetRequiredService<IHttpContextAccessor>()));
        }
        else
        {
            services.AddSingleton(new MessageTracker(options));
        }

        return services;
    }

    /// <summary>
    /// Registers <see cref="TestTrackingContextStartupFilter"/> which injects middleware
    /// that propagates test-tracking HTTP headers into <see cref="TestIdentityScope.Current"/>.
    /// This enables test identity to flow into background tasks (Task.Run, fire-and-forget)
    /// via AsyncLocal, so that <see cref="TestTrackingMessageHandler"/> can track HTTP calls
    /// made from those background threads.
    /// <para>Also detaches every hosted service from the test that built the host
    /// (<see cref="DetachHostedServicesFromTestIdentity"/>): the same execution context that carries the
    /// identity into a test's own background work would otherwise carry it into every loop the host runs,
    /// for as long as the host lives.</para>
    /// </summary>
    public static IServiceCollection AddTestTrackingContextPropagation(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, TestTrackingContextStartupFilter>());
        services.DetachHostedServicesFromTestIdentity();
        return services;
    }

    /// <summary>
    /// Re-registers every <see cref="IHostedService"/> so that it starts, runs and stops in a detached flow
    /// (<see cref="TestIdentityScope.Detach"/>).
    /// </summary>
    /// <remarks>
    /// <para>A host built inside a test inherits the test's execution context, and so does every hosted
    /// service the host starts: a background loop would resolve the test's identity for every call it
    /// makes, for as long as the host lives, so one scenario's report carried the host's housekeeping and
    /// its call set changed with the timing of the loop. Detached, the loop's calls belong to no scenario
    /// (and are dropped, or kept as background when <see cref="RequestResponseLogger.CaptureBackground"/>
    /// is on), while a message that names its scenario still reaches it: <see cref="TestIdentityScope.Begin"/>
    /// and <see cref="TestIdentityScope.SetFromMessage"/> win inside a detached flow.</para>
    /// <para>Wraps the registrations present when it is called, so call it after the hosted services are
    /// registered: <c>WebApplicationFactory.ConfigureTestServices</c> runs after the application's own
    /// registrations. <see cref="AddTestTrackingContextPropagation"/> calls it. Calling it twice wraps
    /// once; keyed registrations are left alone.</para>
    /// </remarks>
    public static IServiceCollection DetachHostedServicesFromTestIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType != typeof(IHostedService) || descriptor.IsKeyedService)
                continue;
            if (descriptor.ImplementationFactory?.Target is DetachedRegistration)
                continue;
            var registration = new DetachedRegistration(descriptor);
            services[i] = ServiceDescriptor.Describe(typeof(IHostedService), registration.Create, descriptor.Lifetime);
        }
        return services;
    }

    /// <summary>The original registration, resolved as it would have been and wrapped.</summary>
    private sealed class DetachedRegistration(ServiceDescriptor original)
    {
        public object Create(IServiceProvider provider)
        {
            var inner = original.ImplementationInstance as IHostedService
                ?? (original.ImplementationFactory is { } factory
                    ? (IHostedService)factory(provider)
                    : (IHostedService)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!));
            return new DetachedHostedService(inner);
        }
    }
}
