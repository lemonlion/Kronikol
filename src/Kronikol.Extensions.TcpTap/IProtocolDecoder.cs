using System.Net;
using Kronikol.Tracking;

namespace Kronikol.Extensions.TcpTap;

/// <summary>Which way a tapped byte segment was travelling.</summary>
public enum TapDirection
{
    /// <summary>From the tapped service to the server (a command).</summary>
    ClientToServer,

    /// <summary>From the server back to the tapped service (a reply).</summary>
    ServerToClient,
}

/// <summary>
/// Decodes a copy of one tapped TCP connection's bytes into <see cref="TapInteraction"/>s.
/// </summary>
/// <remarks>
/// <para>One instance per connection, created by <see cref="TcpTapOptions.DecoderFactory"/>. Methods are
/// called from a single decoder task in wire order, never from the byte pumps — a slow or broken decoder can
/// therefore never delay or corrupt forwarding (invariant D3).</para>
/// <para>A decoder that throws is caught and counted by the tap, and forwarding continues regardless. A
/// <see cref="Protocols.TapProtocolException"/> with <see cref="Protocols.TapProtocolException.Recoverable"/>
/// set — the stream is the right protocol but the decoder lost its place — makes the tap call
/// <see cref="TryReset"/> (when <see cref="TcpTapOptions.ResyncAfterOverflow"/> allows) and keep decoding;
/// any other exception stops decoding that one connection. Every such event is counted, logged and reported
/// through <see cref="TcpTapOptions.OnCaptureDegraded"/> and <see cref="TcpTap.Diagnostics"/>.</para>
/// </remarks>
public interface IProtocolDecoder : IDisposable
{
    /// <summary>A segment of bytes the service sent to the server. Already forwarded when this is called.</summary>
    void OnClientToServer(ReadOnlySpan<byte> data, DateTimeOffset timestamp);

    /// <summary>A segment of bytes the server sent back. Already forwarded when this is called.</summary>
    void OnServerToClient(ReadOnlySpan<byte> data, DateTimeOffset timestamp);

    /// <summary>Both directions have closed; flush anything still pending.</summary>
    void OnConnectionClosed();

    /// <summary>
    /// Clears all per-connection state after a recoverable protocol error so decoding can resume at the next
    /// message boundary. Returns false when the decoder cannot (the default) or has given up trying, in which
    /// case the tap disables decoding for the connection.
    /// </summary>
    bool TryReset() => false;
}

/// <summary>
/// One decoded command/reply exchange, ready to become the request and response halves of a diagram arrow.
/// </summary>
/// <param name="Method">The arrow label (e.g. <c>Get (Hit)</c>, <c>Find ← Trial</c>).</param>
/// <param name="RequestMethod">The label for the request half when it differs (Redis omits hit/miss on the request); null = same as <paramref name="Method"/>.</param>
/// <param name="Uri">The resource URI (<c>redis://db0/key</c>, <c>mongodb:///db/coll</c>).</param>
/// <param name="RequestContent">Note text on the request arrow, or null.</param>
/// <param name="ResponseContent">Note text on the response arrow, or null.</param>
/// <param name="StatusCode">Status stamped on the response half.</param>
/// <param name="RequestTimestamp">When the command's first byte was seen.</param>
/// <param name="ResponseTimestamp">When the reply's last byte was seen.</param>
public sealed record TapInteraction(
    OneOf<HttpMethod, string> Method,
    OneOf<HttpMethod, string>? RequestMethod,
    Uri Uri,
    string? RequestContent,
    string? ResponseContent,
    OneOf<HttpStatusCode, string>? StatusCode,
    DateTimeOffset RequestTimestamp,
    DateTimeOffset ResponseTimestamp)
{
    /// <summary>
    /// Optional pseudo-headers stamped on both halves — a decoder's capture-quality annotations, such as
    /// <c>x-kronikol-capture: resynced</c> on the first interaction after a decoder reset. Null = none.
    /// </summary>
    public (string Key, string? Value)[]? Headers { get; init; }
}
