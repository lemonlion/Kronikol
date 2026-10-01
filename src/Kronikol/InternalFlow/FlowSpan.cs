using System.Diagnostics;

namespace Kronikol.InternalFlow;

/// <summary>
/// A span as internal flow reads it, whatever recorded it: an <see cref="System.Diagnostics.Activity"/> of this process,
/// or a span another process exported (OpenTelemetry's JSON, read by <c>Kronikol.Extensions.Otlp</c>'s
/// <c>OtlpSpan.ToFlowSpan()</c>). The popups, the flame charts, the whole-test flows and the component flows are drawn
/// from these seven fields and nothing else.
/// </summary>
/// <remarks>
/// <para>An <see cref="System.Diagnostics.Activity"/> cannot stand for a foreign span: its span id is generated and
/// cannot be set, and starting one is seen by every listener in the process. So a span ingested from another language
/// is one of these, and an in-process one becomes one when the report is generated (<see cref="From"/>), with the same
/// ids, name, source and times, so what is drawn from it does not change.</para>
/// <para>Ids are lowercase hex, as <see cref="ActivityTraceId.ToString"/> and <see cref="ActivitySpanId.ToString"/>
/// write them; a root span's <see cref="ParentSpanId"/> is null or all zeros.</para>
/// </remarks>
/// <param name="TraceId">The W3C trace id, 32 lowercase hex digits.</param>
/// <param name="SpanId">The span id, 16 lowercase hex digits.</param>
/// <param name="ParentSpanId">The parent's span id, or null (or all zeros) for a root.</param>
/// <param name="Name">What the span did: an <see cref="System.Diagnostics.Activity"/>'s display name, an OTLP span's name.</param>
/// <param name="Source">Who recorded it, the diagram's swimlane: the <see cref="ActivitySource"/>'s name, an OTLP span's instrumentation scope.</param>
/// <param name="StartTimeUtc">When it started, UTC.</param>
/// <param name="Duration">How long it ran.</param>
/// <param name="Service">The service that recorded it, when the exporter said (OTLP's <c>service.name</c>); null in-process.</param>
public sealed record FlowSpan(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Source,
    DateTime StartTimeUtc,
    TimeSpan Duration,
    string? Service = null)
{
    /// <summary>The in-process span this one was made from, which a segment hands back as <see cref="InternalFlowSegment.Spans"/>.</summary>
    internal Activity? Activity { get; private init; }

    /// <summary>The span an <see cref="System.Diagnostics.Activity"/> stands for, as internal flow has always read it.</summary>
    public static FlowSpan From(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        return new FlowSpan(
            activity.TraceId.ToString(),
            activity.SpanId.ToString(),
            activity.ParentSpanId.ToString(),
            activity.DisplayName ?? activity.OperationName,
            activity.Source.Name,
            activity.StartTimeUtc,
            activity.Duration)
        {
            Activity = activity,
        };
    }
}
