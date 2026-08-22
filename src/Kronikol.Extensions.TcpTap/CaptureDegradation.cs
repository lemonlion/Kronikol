namespace Kronikol.Extensions.TcpTap;

/// <summary>What kind of capture loss a <see cref="CaptureDegradation"/> reports. Forwarding is never affected by any of them.</summary>
public enum CaptureDegradationKind
{
    /// <summary>A bulk payload longer than the capture cap was streamed past; the interaction was still recorded, with a preview and the length on the wire.</summary>
    OversizePayloadSkipped,

    /// <summary>The decoder lost its place (undecoded-bytes cap, pending-queue overflow, a protocol error after a healthy start) and was reset; it re-arms at the next command boundary. The first interaction after it is stamped <c>[resynchronised — pairing uncertain]</c>.</summary>
    DecoderReset,

    /// <summary>The decoder gave up on a connection for good (the bytes are not the protocol, or resync is off / failed repeatedly); nothing on that connection is recorded from then on.</summary>
    DecodingDisabled,

    /// <summary>The decode queue of a connection was full and a segment was dropped (reported at most once per connection per minute); interactions on it may be missing or mis-paired.</summary>
    SegmentsDropped,

    /// <summary>A connection closed while a command was unanswered or a message was only partly received; its last interaction(s) were not recorded.</summary>
    ConnectionClosedMidMessage,
}

/// <summary>
/// One capture-loss event on a tap, handed to <see cref="TcpTapOptions.OnCaptureDegraded"/> as it happens so a host
/// can flag the tap (a dashboard's services table, a warning log) before the report is generated. The same facts are
/// available after the fact as counters on <see cref="TcpTap"/> and summarised by <see cref="TcpTap.Diagnostics"/>.
/// </summary>
/// <param name="Tap">The tap's display name (<see cref="TcpTapOptions.DisplayName"/>).</param>
/// <param name="ConnectionId">The tap-local connection the event happened on (1-based), or 0 when it is tap-wide.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Detail">A one-line description, safe to print.</param>
public sealed record CaptureDegradation(string Tap, long ConnectionId, CaptureDegradationKind Kind, string Detail)
{
    /// <inheritdoc />
    public override string ToString() => $"[{Tap}] connection {ConnectionId}: {Kind} — {Detail}";
}
