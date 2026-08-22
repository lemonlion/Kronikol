namespace Kronikol.Extensions.TcpTap;

/// <summary>
/// A <see cref="TcpTap"/> that speaks RESP: point a service's Redis client at it instead of Redis, and every
/// command the service issues renders as a <c>service → redis</c> arrow — <c>Get (Hit)</c>, <c>Set</c>,
/// <c>HashGetAll</c> — with the key in the URI and the value in the note, exactly as the in-process
/// <c>Kronikol.Extensions.Redis</c> extension would render it.
/// </summary>
/// <remarks>
/// Handshake and keep-alive chatter (<c>AUTH</c>, <c>HELLO</c>, <c>PING</c>, <c>CLIENT</c>, <c>CONFIG</c>,
/// <c>INFO</c>, <c>ECHO</c>, <c>COMMAND</c>, <c>CLUSTER</c>, <c>SELECT</c>) is dropped in the decoder, so no
/// credential ever reaches a sink; <c>SELECT</c> is still followed so the database index in the URI is right.
/// A client that opens several connections (StackExchange.Redis opens an interactive and a subscription one)
/// is handled per connection.
/// </remarks>
public sealed class RedisTap : TcpTap
{
    /// <summary>Creates a Redis tap for the given options.</summary>
    public RedisTap(RedisTapOptions options) : base(options) => Options = options;

    /// <summary>The options this tap runs with.</summary>
    public new RedisTapOptions Options { get; }
}
