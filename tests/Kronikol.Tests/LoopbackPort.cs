using System.Net;
using System.Net.Sockets;

namespace Kronikol.Tests;

/// <summary>
/// Loopback ports for the listeners tests start on a port of their own choosing, never the same one twice in a process.
/// Binding port 0 and letting it go does not reserve the number: Linux can offer it to the next probe at once, so two
/// tests running at the same time each started a listener on one port, and the second failed with "conflicts with an
/// existing registration on the machine". Linked into the test projects that start listeners this way.
/// </summary>
internal static class LoopbackPort
{
    private static readonly HashSet<int> HandedOut = [];

    public static int Free() => Free(Probe);

    internal static int Free(Func<int> probe)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var port = probe();
            lock (HandedOut)
            {
                if (HandedOut.Add(port))
                    return port;
            }
        }

        throw new InvalidOperationException("The OS offered only loopback ports this process has handed out already.");
    }

    /// <summary>
    /// Binds something that keeps its port for as long as it lives (a socket that never listens, a raw TCP stub) to
    /// port 0, on a port this process has not handed out. The OS can give it a port <see cref="Free()"/> has just
    /// handed to a listener not started yet, which then could not start: such a port is let go at once and the
    /// binding tried again.
    /// </summary>
    internal static T Hold<T>(Func<T> bind, Func<T, int> portOf, Action<T> release)
    {
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var holder = bind();
            lock (HandedOut)
            {
                if (HandedOut.Add(portOf(holder)))
                    return holder;
            }

            release(holder);
        }

        throw new InvalidOperationException("The OS offered only loopback ports this process has handed out already.");
    }

    /// <summary>A started listener on a loopback port this process has not handed out (see <see cref="Hold"/>).</summary>
    internal static TcpListener Listening() => Hold(
        () =>
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return listener;
        },
        listener => ((IPEndPoint)listener.LocalEndpoint).Port,
        listener => listener.Stop());

    /// <summary>
    /// A socket bound to a loopback port this process has not handed out and never listening, so a connection to it is
    /// refused (see <see cref="Hold"/>).
    /// </summary>
    internal static Socket Closed() => Hold(
        () =>
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return socket;
        },
        socket => ((IPEndPoint)socket.LocalEndPoint!).Port,
        socket => socket.Dispose());

    private static int Probe()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
