using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace BreakfastProvider.Tests.Component.xUnit;

// Scratch-clone probe for plans/KAFKA_INTERCEPTION_AND_TRACE_PLAN.md S8 (never committed): writes the W3C ids of every
// KafkaPublish span the app starts (KafkaEventPublisher.cs:62) to $KAFKA_PUBLISH_PROBE, one "<trace> <span>" per line.
// It samples nothing itself (None), so which spans exist is the app's own tracing's choice.
internal static class KafkaPublishProbe
{
    private static readonly object Gate = new();
    private static ActivityListener? _listener;

    [ModuleInitializer]
    internal static void Start()
    {
        var path = Environment.GetEnvironmentVariable("KAFKA_PUBLISH_PROBE");
        if (string.IsNullOrEmpty(path))
            return;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "BreakfastProvider.Api",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ActivityStarted = activity =>
            {
                if (activity.OperationName != "KafkaPublish")
                    return;
                lock (Gate)
                    File.AppendAllText(path, $"{activity.TraceId.ToHexString()} {activity.SpanId.ToHexString()}\n");
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }
}
