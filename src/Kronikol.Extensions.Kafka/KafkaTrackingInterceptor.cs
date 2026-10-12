using System.Collections.Concurrent;
using Confluent.Kafka;
using Microsoft.AspNetCore.Http;

namespace Kronikol.Extensions.Kafka;

/// <summary>
/// Provides static/ambient interception for Kafka consumers and producers that are
/// built internally (e.g. via <c>new ConsumerBuilder&lt;TKey, TValue&gt;(...).Build()</c>)
/// rather than resolved from DI.
/// <para>
/// Call <see cref="EnableConsumerTracking{TKey,TValue}(KafkaTrackingOptions, IHttpContextAccessor?)"/> or
/// <see cref="EnableProducerTracking{TKey,TValue}(KafkaTrackingOptions, IHttpContextAccessor?)"/> in your test setup to activate
/// interception, then use the <c>.Tracked()</c> or <c>.BuildTracked()</c> extension
/// methods in production code.
/// </para>
/// </summary>
public static class KafkaTrackingInterceptor
{
    private sealed record TrackingState(KafkaTracker Tracker, KafkaTrackingOptions Options, IHttpContextAccessor? HttpContextAccessor);

    private static readonly ConcurrentDictionary<Type, TrackingState> ConsumerTracking = new();
    private static readonly ConcurrentDictionary<Type, TrackingState> ProducerTracking = new();

    /// <summary>
    /// Enables consumer tracking for <c>IConsumer&lt;TKey, TValue&gt;</c>.
    /// Any subsequent call to <c>.Tracked()</c> or <c>.BuildTracked()</c> on a matching
    /// consumer will wrap it with <see cref="TrackingKafkaConsumer{TKey,TValue}"/>.
    /// </summary>
    public static void EnableConsumerTracking<TKey, TValue>(
        KafkaTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor = null)
        => Enable(ConsumerTracking, typeof(IConsumer<TKey, TValue>), options, httpContextAccessor, $"consumer {PairName<TKey, TValue>()}");

    /// <summary>
    /// Enables consumer tracking with an options configuration action.
    /// </summary>
    public static void EnableConsumerTracking<TKey, TValue>(
        Action<KafkaTrackingOptions>? configure = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);
        EnableConsumerTracking<TKey, TValue>(options, httpContextAccessor);
    }

    /// <summary>
    /// Enables producer tracking for <c>IProducer&lt;TKey, TValue&gt;</c>.
    /// Any subsequent call to <c>.Tracked()</c> or <c>.BuildTracked()</c> on a matching
    /// producer will wrap it with <see cref="TrackingKafkaProducer{TKey,TValue}"/>.
    /// </summary>
    public static void EnableProducerTracking<TKey, TValue>(
        KafkaTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor = null)
        => Enable(ProducerTracking, typeof(IProducer<TKey, TValue>), options, httpContextAccessor, $"producer {PairName<TKey, TValue>()}");

    /// <summary>
    /// Enables producer tracking with an options configuration action.
    /// </summary>
    public static void EnableProducerTracking<TKey, TValue>(
        Action<KafkaTrackingOptions>? configure = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var options = new KafkaTrackingOptions();
        configure?.Invoke(options);
        EnableProducerTracking<TKey, TValue>(options, httpContextAccessor);
    }

    /// <summary>Disables consumer tracking for <c>IConsumer&lt;TKey, TValue&gt;</c>.</summary>
    public static void DisableConsumerTracking<TKey, TValue>()
        => ConsumerTracking.TryRemove(typeof(IConsumer<TKey, TValue>), out _);

    /// <summary>Disables producer tracking for <c>IProducer&lt;TKey, TValue&gt;</c>.</summary>
    public static void DisableProducerTracking<TKey, TValue>()
        => ProducerTracking.TryRemove(typeof(IProducer<TKey, TValue>), out _);

    /// <summary>Clears all consumer and producer tracking state.</summary>
    public static void Reset()
    {
        ConsumerTracking.Clear();
        ProducerTracking.Clear();
    }

    /// <summary>
    /// Wraps the consumer with <see cref="TrackingKafkaConsumer{TKey,TValue}"/> if tracking
    /// is enabled for <c>IConsumer&lt;TKey, TValue&gt;</c>. Returns the original if not enabled
    /// or if the consumer is already wrapped.
    /// </summary>
    public static IConsumer<TKey, TValue> WrapConsumer<TKey, TValue>(IConsumer<TKey, TValue> consumer)
    {
        if (consumer is TrackingKafkaConsumer<TKey, TValue>)
            return consumer;

        if (ConsumerTracking.TryGetValue(typeof(IConsumer<TKey, TValue>), out var state))
        {
            state.Tracker.EnsureRegistered();
            return new TrackingKafkaConsumer<TKey, TValue>(consumer, state.Tracker, state.Options);
        }

        return consumer;
    }

    /// <summary>
    /// Wraps the producer with <see cref="TrackingKafkaProducer{TKey,TValue}"/> if tracking
    /// is enabled for <c>IProducer&lt;TKey, TValue&gt;</c>. Returns the original if not enabled
    /// or if the producer is already wrapped.
    /// </summary>
    public static IProducer<TKey, TValue> WrapProducer<TKey, TValue>(IProducer<TKey, TValue> producer)
    {
        if (producer is TrackingKafkaProducer<TKey, TValue>)
            return producer;

        if (ProducerTracking.TryGetValue(typeof(IProducer<TKey, TValue>), out var state))
        {
            state.Tracker.EnsureRegistered();
            return new TrackingKafkaProducer<TKey, TValue>(producer, state.Tracker, state.Options);
        }

        return producer;
    }

    // One tracker per enabled pair and direction: enabling again with the options and accessor it holds keeps it, and a
    // tracker registers only when it first wraps a client, so neither a replaced tracker nor one for a direction the app
    // never builds is reported as registered but never invoked.
    private static void Enable(
        ConcurrentDictionary<Type, TrackingState> tracking,
        Type client,
        KafkaTrackingOptions options,
        IHttpContextAccessor? httpContextAccessor,
        string role)
    {
        tracking.AddOrUpdate(
            client,
            _ => NewState(),
            (_, held) => ReferenceEquals(held.Options, options) && ReferenceEquals(held.HttpContextAccessor, httpContextAccessor)
                ? held
                : NewState());

        TrackingState NewState() => new(new KafkaTracker(options, httpContextAccessor, role), options, httpContextAccessor);
    }

    // "<String,String>": the pair as the registry's unused-component warning names it.
    private static string PairName<TKey, TValue>() => $"<{TypeName(typeof(TKey))},{TypeName(typeof(TValue))}>";

    private static string TypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;
        var tick = type.Name.IndexOf('`');
        var name = tick < 0 ? type.Name : type.Name[..tick];
        return $"{name}<{string.Join(",", type.GetGenericArguments().Select(TypeName))}>";
    }
}
