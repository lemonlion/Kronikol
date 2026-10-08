using Confluent.Kafka;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Kronikol.Tracking;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Extension methods for registering Kafka test tracking via dependency injection.
/// </summary>
public static class KafkaServiceCollectionExtensions
{
    /// <summary>
    /// Decorates all existing <see cref="IProducer{TKey,TValue}"/> registrations with
    /// <see cref="TrackingKafkaProducer{TKey,TValue}"/> for test diagram tracking.
    /// <para>
    /// The <see cref="KafkaTracker"/> is created internally with the provided options and
    /// <see cref="KafkaTrackingOptions.HttpContextAccessor"/>, or, when that is null, an
    /// <see cref="IHttpContextAccessor"/> resolved from DI (if registered).
    /// </para>
    /// <para>No-op when no matching registrations exist.</para>
    /// </summary>
    public static IServiceCollection AddKafkaProducerTestTracking<TKey, TValue>(
        this IServiceCollection services,
        Action<KafkaTrackingOptions>? configure = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);

        services.DecorateAll<IProducer<TKey, TValue>>((sp, inner) =>
        {
            var tracker = new KafkaTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>());
            return new TrackingKafkaProducer<TKey, TValue>(inner, tracker, options);
        });

        return services;
    }

    /// <summary>
    /// Decorates all existing <see cref="IConsumer{TKey,TValue}"/> registrations with
    /// <see cref="TrackingKafkaConsumer{TKey,TValue}"/> for test diagram tracking.
    /// <para>
    /// The <see cref="KafkaTracker"/> is created internally with the provided options and
    /// <see cref="KafkaTrackingOptions.HttpContextAccessor"/>, or, when that is null, an
    /// <see cref="IHttpContextAccessor"/> resolved from DI (if registered).
    /// </para>
    /// <para>No-op when no matching registrations exist.</para>
    /// </summary>
    public static IServiceCollection AddKafkaConsumerTestTracking<TKey, TValue>(
        this IServiceCollection services,
        Action<KafkaTrackingOptions>? configure = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);

        services.DecorateAll<IConsumer<TKey, TValue>>((sp, inner) =>
        {
            var tracker = new KafkaTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>());
            return new TrackingKafkaConsumer<TKey, TValue>(inner, tracker, options);
        });

        return services;
    }

    /// <summary>
    /// Decorates all existing <see cref="IKafkaConsumerFactory{TKey,TValue}"/> registrations with
    /// <see cref="TrackingKafkaConsumerFactory{TKey,TValue}"/> for test diagram tracking.
    /// <para>
    /// If no <see cref="IKafkaConsumerFactory{TKey,TValue}"/> is registered, a default
    /// <see cref="KafkaConsumerFactory{TKey,TValue}"/> is added first.
    /// </para>
    /// </summary>
    public static IServiceCollection AddKafkaConsumerFactoryTestTracking<TKey, TValue>(
        this IServiceCollection services,
        Action<KafkaTrackingOptions>? configure = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IKafkaConsumerFactory<TKey, TValue>, KafkaConsumerFactory<TKey, TValue>>();

        services.DecorateAll<IKafkaConsumerFactory<TKey, TValue>>((sp, inner) =>
        {
            var tracker = new KafkaTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>());
            return new TrackingKafkaConsumerFactory<TKey, TValue>(inner, tracker, options);
        });

        return services;
    }

    /// <summary>
    /// Decorates all existing <see cref="IKafkaProducerFactory{TKey,TValue}"/> registrations with
    /// <see cref="TrackingKafkaProducerFactory{TKey,TValue}"/> for test diagram tracking.
    /// <para>
    /// If no <see cref="IKafkaProducerFactory{TKey,TValue}"/> is registered, a default
    /// <see cref="KafkaProducerFactory{TKey,TValue}"/> is added first.
    /// </para>
    /// </summary>
    public static IServiceCollection AddKafkaProducerFactoryTestTracking<TKey, TValue>(
        this IServiceCollection services,
        Action<KafkaTrackingOptions>? configure = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IKafkaProducerFactory<TKey, TValue>, KafkaProducerFactory<TKey, TValue>>();

        services.DecorateAll<IKafkaProducerFactory<TKey, TValue>>((sp, inner) =>
        {
            var tracker = new KafkaTracker(options, options.HttpContextAccessor ?? sp.GetService<IHttpContextAccessor>());
            return new TrackingKafkaProducerFactory<TKey, TValue>(inner, tracker, options);
        });

        return services;
    }
}
