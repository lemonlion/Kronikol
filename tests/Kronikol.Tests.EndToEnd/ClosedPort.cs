using System.Net;
using System.Net.Sockets;

namespace Kronikol.Tests.EndToEnd;

/// <summary>
/// A loopback port that stays closed for as long as this handle lives: bound but never listening, so a connection to it
/// is refused, and reserved, so the OS cannot hand it to a server another test starts in the meantime (a port merely
/// observed free and then released was given to a parallel stub on CI; the unit tests' <c>ClosedPort</c> says more).
/// </summary>
internal sealed class ClosedPort : IDisposable
{
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

    public int Port { get; }

    public ClosedPort()
    {
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    public void Dispose() => _socket.Dispose();
}
