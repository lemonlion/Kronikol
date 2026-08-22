namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>
/// The bytes on a tapped connection could not be decoded. The tap catches this, counts it and — when
/// <see cref="Recoverable"/> and <see cref="TcpTapOptions.ResyncAfterOverflow"/> allow — resets the decoder
/// so it re-arms at the next message boundary; otherwise it stops decoding that one connection. Forwarding is
/// never affected either way.
/// </summary>
public class TapProtocolException : Exception
{
    /// <summary>Creates a non-recoverable protocol exception (the stream is not the protocol at all).</summary>
    public TapProtocolException(string message) : base(message)
    {
    }

    /// <summary>Creates a protocol exception, stating whether the decoder can be reset and resume.</summary>
    public TapProtocolException(string message, bool recoverable) : base(message) => Recoverable = recoverable;

    /// <summary>
    /// True when the stream is the right protocol but the decoder lost its place (the undecoded-bytes cap was hit, the
    /// pending queue overflowed) — a reset at the next command boundary can resume capture. False when the bytes are not
    /// the protocol at all, in which case the tap disables decoding for the connection.
    /// </summary>
    public bool Recoverable { get; init; }
}
