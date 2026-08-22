namespace Kronikol.Extensions.TcpTap.Protocols;

/// <summary>
/// The bytes on a tapped connection could not be decoded. The tap catches this, counts it, and stops decoding
/// that one connection — forwarding is never affected.
/// </summary>
public class TapProtocolException(string message) : Exception(message);
