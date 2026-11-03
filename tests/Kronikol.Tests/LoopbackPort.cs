using System.Net;
using System.Net.Sockets;

namespace Kronikol.Tests;

/// <summary>
/// Loopback ports for the listeners tests start on a port of their own choosing, never the same one twice in a process.
/// Binding port 0 and letting it go does not reserve the number: Linux can offer it to the next probe at once, so two
/// tests running at the same time each started a listener on one port, and the second failed with "conflicts with an
/// existing registration on the machine". Linked into the test projects that start listeners this way.
/// <para>
/// Nor does it within a machine: two test programs running at once were offered one number, and 8 copies of the ProxyTap project
/// failed 5 of 48 runs that way. So <see cref="Free()"/> takes its numbers from below where any system takes port 0 from, in a
/// block of them only this program holds.
/// </para>
/// </summary>
internal static class LoopbackPort
{
    /// <summary>
    /// Below every system's ephemeral range (Linux's starts at 32768, Windows' and macOS's at 49152), so nothing binding port 0 is
    /// ever given one, and above the numbers a stock Windows reserves in http.sys for its own services (10243 to 10247).
    /// </summary>
    internal const int FirstPort = 11000;

    internal const int LastPort = 31999;

    private const int BlockSize = 256;
    private const int BlockCount = (LastPort - FirstPort + 1) / BlockSize;

    private static readonly HashSet<int> HandedOut = [];
    private static readonly List<Mutex> Claims = [];
    private static int _next;
    private static int _blockEnd;

    public static int Free() => Free(NextInABlock);

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

    /// <summary>
    /// Claims a block of ports for as long as this process lives, the first free one from <paramref name="preferred"/> on, or -1
    /// when every block is held. A mutex's name is the machine's, so it is the claim, and nothing ever waits on it: a second
    /// holder of the name, this process or another, is told it did not create it.
    /// </summary>
    internal static int ClaimBlock(int preferred)
    {
        lock (Claims)
        {
            for (var offset = 0; offset < BlockCount; offset++)
            {
                var block = (preferred + offset) % BlockCount;
                var claim = new Mutex(initiallyOwned: false, $"Kronikol.Tests.LoopbackPort.{block}", out var createdNew);
                if (createdNew)
                {
                    Claims.Add(claim);
                    return block;
                }

                claim.Dispose();
            }
        }

        return -1;
    }

    private static int NextInABlock()
    {
        lock (Claims)
        {
            while (true)
            {
                if (_next == _blockEnd)
                {
                    var block = ClaimBlock(Environment.ProcessId % BlockCount);
                    if (block < 0)
                        return Ephemeral();

                    _next = FirstPort + (block * BlockSize);
                    _blockEnd = _next + BlockSize;
                }

                var port = _next++;
                if (CanListen(port))
                    return port;
            }
        }
    }

    /// <summary>
    /// False when something else is on it, or the system keeps it for itself, as Hyper-V does with ranges of its own. On Windows
    /// http.sys is asked too, as a socket cannot see what it holds: a reservation such as <c>http://+:10243/WMPNSSv4/</c>, which refuses
    /// a listener on that port with "Access is denied", and another program's listener.
    /// </summary>
    internal static bool CanListen(int port)
    {
        var socket = new TcpListener(IPAddress.Loopback, port);
        try
        {
            socket.Start();
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            socket.Stop();
        }

        if (!OperatingSystem.IsWindows())
            return true;

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        try
        {
            listener.Start();
            listener.Close();
            return true;
        }
        catch (HttpListenerException)
        {
            return false;
        }
    }

    private static int Ephemeral()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
