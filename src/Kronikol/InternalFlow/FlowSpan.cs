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
/// <param name="Name">What the span did: an <see cref="System.Diagnostics.Activity"/>'s display name, or the method of the RPC it stands for when nobody named it, an OTLP span's name.</param>
/// <param name="Source">Who recorded it, the diagram's swimlane: the <see cref="ActivitySource"/>'s name, an OTLP span's instrumentation scope, or, for an activity made without a source, the name taken from its operation.</param>
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
            NameOf(activity),
            SpanSource.Of(activity),
            activity.StartTimeUtc,
            activity.Duration)
        {
            Activity = activity,
        };
    }

    /// <summary>
    /// What a span is called: its display name, unless nobody named it (the display name is its operation name), in which
    /// case the method of the call it stands for, when it carries one: grpc-dotnet's <c>grpc.method</c>, or OpenTelemetry's
    /// <c>rpc.service</c> and <c>rpc.method</c>. grpc-dotnet names no span, so every gRPC step read
    /// <c>Grpc.Net.Client.GrpcOut</c> (#138).
    /// </summary>
    private static string NameOf(Activity activity)
    {
        var name = activity.DisplayName ?? activity.OperationName;
        if (!string.Equals(name, activity.OperationName, StringComparison.Ordinal))
            return name;
        if (activity.GetTagItem("grpc.method") is string { Length: > 0 } grpcMethod)
            return grpcMethod;
        if (activity.GetTagItem("rpc.service") is string { Length: > 0 } service
            && activity.GetTagItem("rpc.method") is string { Length: > 0 } method)
            return $"{service}/{method}";
        return name;
    }
}
