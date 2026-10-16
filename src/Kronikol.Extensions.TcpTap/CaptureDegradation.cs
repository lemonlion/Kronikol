namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// What kind of capture loss a <see cref="CaptureDegradation"/> reports. Most leave forwarding untouched; three are about
/// connections the tap closed or could not carry: <see cref="StuckConnectionReaped"/>, <see cref="UpstreamConnectFailed"/>
/// and <see cref="AcceptFailed"/>.
/// </summary>
public enum CaptureDegradationKind
{
    /// <summary>A bulk payload longer than the capture cap was streamed past; the interaction was still recorded, with a preview and the length on the wire.</summary>
    OversizePayloadSkipped,

    /// <summary>The decoder lost its place (undecoded-bytes cap, pending-queue overflow, a protocol error after a healthy start) and was reset; it re-arms at the next command boundary. The first interaction after it is stamped <c>[resynchronised — pairing uncertain]</c>.</summary>
    DecoderReset,

    /// <summary>The decoder gave up on a connection for good (the bytes are not the protocol, or resync is off / failed repeatedly), or the connection never had one (<see cref="TcpTapOptions.DecoderFactory"/> threw or returned null); nothing on that connection is recorded from then on, and it keeps forwarding.</summary>
    DecodingDisabled,

    /// <summary>The decode queue of a connection was full and a segment was dropped (reported at most once per connection per minute); interactions on it may be missing or mis-paired.</summary>
    SegmentsDropped,

    /// <summary>A connection closed while a command was unanswered or a message was only partly received; its last interaction(s) were not recorded.</summary>
    ConnectionClosedMidMessage,

    /// <summary>
    /// The tap closed a connection on which a decoded command had sat unanswered for
    /// <see cref="TcpTapOptions.ReapStuckConnectionsAfter"/>, one of three limits that close a pair: the other two,
    /// <see cref="TcpTapOptions.HalfClosedTimeout"/> and <see cref="TcpTapOptions.StalledWriteTimeout"/>, are counted but
    /// not reported here, since a pair they close has lost none of its capture. It exists because a database client whose internal pipeline has wedged (commands
    /// queued forever, never timing out) recovers from a closed socket where it never recovers on its
    /// own; no healthy connection ever trips the threshold, because the servers answer in milliseconds
    /// and an idle connection has nothing unanswered.
    /// </summary>
    StuckConnectionReaped,

    /// <summary>
    /// The tap could not connect to the real server (<see cref="TcpTapOptions.ForwardHost"/>:<see cref="TcpTapOptions.ForwardPort"/>)
    /// within <see cref="TcpTapOptions.ConnectTimeout"/>, or at all, and closed the caller's connection without forwarding
    /// anything; nothing on it reached the server, so nothing on it was recorded. Raised once per connection.
    /// </summary>
    UpstreamConnectFailed,

    /// <summary>
    /// Accepting a caller failed; on Linux, a process out of file descriptors is one cause. The tap keeps listening and
    /// tries again, pausing up to a second between tries, so callers that arrive meanwhile wait in the listen queue, or find
    /// it full. Tap-wide (<see cref="CaptureDegradation.ConnectionId"/> 0), raised with the first failure in a row and then
    /// at most once a minute while they go on, for each listening socket: a tap on <c>localhost</c> has two.
    /// </summary>
    AcceptFailed,
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
    public override string ToString() =>
        $"[{Tap}] {(ConnectionId == 0 ? "tap-wide" : $"connection {ConnectionId}")}: {Kind} — {Detail}";
}
