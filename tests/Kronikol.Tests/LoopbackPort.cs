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

    private static int Probe()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
