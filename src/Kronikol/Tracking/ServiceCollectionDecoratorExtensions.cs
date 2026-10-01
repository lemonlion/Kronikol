using Microsoft.Extensions.DependencyInjection;

namespace Kronikol.Tracking;

/// <summary>
/// Extension methods for decorating existing service registrations in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionDecoratorExtensions
{
    /// <summary>
    /// Wraps <b>all</b> existing registrations of <typeparamref name="TService"/> with a decorator.
    /// Each original registration is removed and replaced with a decorated version that preserves
    /// the original <see cref="ServiceLifetime"/>. A keyed registration stays keyed, under its own key.
    /// <para>No-op when no matching registrations exist.</para>
    /// </summary>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="decoratorFactory">
    /// Factory that receives the <see cref="IServiceProvider"/> and the original (inner) service instance,
    /// and returns the decorated wrapper.
    /// </param>
    public static IServiceCollection DecorateAll<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, TService> decoratorFactory)
        where TService : class
    {
        var descriptors = services
            .Where(d => d.ServiceType == typeof(TService))
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);

            // A keyed registration stays keyed, under its own key: its implementation is read from the Keyed*
            // members, since the unkeyed ones refuse a keyed descriptor.
            if (descriptor.IsKeyedService)
            {
                var keyedInner = CreateKeyedInnerFactory(descriptor, typeof(TService));
                services.Add(new ServiceDescriptor(
                    typeof(TService),
                    descriptor.ServiceKey,
                    (sp, key) => decoratorFactory(sp, (TService)keyedInner(sp, key)),
                    descriptor.Lifetime));
                continue;
            }

            var innerFactory = CreateInnerFactory<TService>(descriptor);

            services.Add(new ServiceDescriptor(
                typeof(TService),
                sp => decoratorFactory(sp, innerFactory(sp)),
                descriptor.Lifetime));
        }

        return services;
    }

    /// <summary>
    /// Scans the <see cref="IServiceCollection"/> for all closed-generic registrations that match
    /// <paramref name="openGenericServiceType"/> (e.g. <c>typeof(IRepository&lt;&gt;)</c>), and replaces
    /// each with <paramref name="openGenericDecoratorType"/> (e.g. <c>typeof(LoggingRepository&lt;&gt;)</c>).
    /// <para>
    /// The decorator's constructor must accept the inner service as its first parameter.
    /// Any additional constructor parameters are resolved from DI via <see cref="ActivatorUtilities"/>.
    /// </para>
    /// <para>A keyed registration stays keyed, under its own key.</para>
    /// <para>No-op when no matching registrations exist.</para>
    /// </summary>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="openGenericServiceType">The open generic service type to scan for (e.g. <c>typeof(IFoo&lt;&gt;)</c>).</param>
    /// <param name="openGenericDecoratorType">The open generic decorator type to wrap with (e.g. <c>typeof(DecoratingFoo&lt;&gt;)</c>).</param>
    public static IServiceCollection DecorateAllOpen(
        this IServiceCollection services,
        Type openGenericServiceType,
        Type openGenericDecoratorType)
    {
        var descriptors = services
            .Where(d => d.ServiceType.IsGenericType &&
                        d.ServiceType.GetGenericTypeDefinition() == openGenericServiceType)
            .ToList();

        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);

            var serviceType = descriptor.ServiceType;
            var typeArgs = serviceType.GetGenericArguments();
            var closedDecoratorType = openGenericDecoratorType.MakeGenericType(typeArgs);

            if (descriptor.IsKeyedService)
            {
                var keyedInner = CreateKeyedInnerFactory(descriptor, serviceType);
                services.Add(new ServiceDescriptor(
                    serviceType,
                    descriptor.ServiceKey,
                    (sp, key) => ActivatorUtilities.CreateInstance(sp, closedDecoratorType, keyedInner(sp, key)),
                    descriptor.Lifetime));
                continue;
            }

            var innerFactory = CreateInnerFactory(descriptor, serviceType);

            services.Add(new ServiceDescriptor(
                serviceType,
                sp =>
                {
                    var inner = innerFactory(sp);
                    return ActivatorUtilities.CreateInstance(sp, closedDecoratorType, inner);
                },
                descriptor.Lifetime));
        }

        return services;
    }

    private static Func<IServiceProvider, TService> CreateInnerFactory<TService>(ServiceDescriptor descriptor)
        where TService : class
    {
        if (descriptor.ImplementationFactory is not null)
            return sp => (TService)descriptor.ImplementationFactory(sp);

        if (descriptor.ImplementationInstance is not null)
            return _ => (TService)descriptor.ImplementationInstance;

        if (descriptor.ImplementationType is not null)
        {
            var implType = descriptor.ImplementationType;
            return sp => (TService)ActivatorUtilities.CreateInstance(sp, implType);
        }

        throw new InvalidOperationException(
            $"Cannot create inner factory for service {descriptor.ServiceType.Name}: " +
            "descriptor has no ImplementationFactory, ImplementationInstance, or ImplementationType.");
    }

    private static Func<IServiceProvider, object> CreateInnerFactory(ServiceDescriptor descriptor, Type serviceType)
    {
        if (descriptor.ImplementationFactory is not null)
            return descriptor.ImplementationFactory;

        if (descriptor.ImplementationInstance is not null)
            return _ => descriptor.ImplementationInstance;

        if (descriptor.ImplementationType is not null)
        {
            var implType = descriptor.ImplementationType;
            return sp => ActivatorUtilities.CreateInstance(sp, implType);
        }

        throw new InvalidOperationException(
            $"Cannot create inner factory for service {serviceType.Name}: " +
            "descriptor has no ImplementationFactory, ImplementationInstance, or ImplementationType.");
    }

    private static Func<IServiceProvider, object?, object> CreateKeyedInnerFactory(ServiceDescriptor descriptor, Type serviceType)
    {
        if (descriptor.KeyedImplementationFactory is { } factory)
            return factory;

        if (descriptor.KeyedImplementationInstance is { } instance)
            return (_, _) => instance;

        if (descriptor.KeyedImplementationType is { } implType)
            return (sp, _) => ActivatorUtilities.CreateInstance(sp, implType);

        throw new InvalidOperationException(
            $"Cannot create inner factory for keyed service {serviceType.Name}: " +
            "descriptor has no KeyedImplementationFactory, KeyedImplementationInstance, or KeyedImplementationType.");
    }
}
