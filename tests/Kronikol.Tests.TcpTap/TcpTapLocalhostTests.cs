using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kronikol.Extensions.TcpTap;

namespace Kronikol.Tests.TcpTap;

/// <summary>
/// "localhost" is two addresses, <c>::1</c> and <c>127.0.0.1</c>, and Windows tries <c>::1</c> first and takes about 2 s to
/// give up on an address that refuses (plans/TCPTAP_FAILED_CONNECT_PLAN.md R3, harness <c>connect/</c> F). A tap that
/// listened on 127.0.0.1 alone made a client that dials it by name wait 2 s on every connect, and a tap that forwarded to
/// "localhost" made every connection wait 2 s for a server on 127.0.0.1 alone. Linux answers a refused address at once,
/// so the timing facts are red only on Windows; the order itself is pinned on every platform.
/// </summary>
public class TcpTapLocalhostTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static RedisTapOptions Options(StubServer server, List<string> log, string listenHost, string forwardHost) => new()
    {
        ListenPort = 0,
        ListenHost = listenHost,
        ForwardHost = forwardHost,
        ForwardPort = server.Port,
        CallerName = "svc",
        ServiceName = "redis",
        Sink = new RecordingSink(),
        EmitActivities = false,
        Log = line => { lock (log) log.Add(line); },
    };

    private static StubServer Pong(IPAddress? address = null) => new(_ => Encoding.ASCII.GetBytes(Resp.Simple("PONG")), address);

    /// <summary>Whether this machine can listen on the IPv6 loopback at all (a container may have IPv6 switched off).</summary>
    private static bool CanListenOnIPv6Loopback()
    {
        if (!Socket.OSSupportsIPv6)
            return false;
        try
        {
            using var probe = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether "localhost" names the IPv6 loopback here. A hosts file may give it 127.0.0.1 alone (Debian's names ::1
    /// <c>ip6-localhost</c>, as the dotnet/sdk container's does), and then nothing reaches ::1 by that name.
    /// </summary>
    private static bool LocalhostNamesTheIPv6Loopback() =>
        Dns.GetHostAddresses("localhost").Any(a => a.Equals(IPAddress.IPv6Loopback));

    /// <summary>What a client that dials <paramref name="address"/> hears in answer to a PING, or why it heard nothing.</summary>
    private static async Task<string> PingAsync(IPAddress address, int port)
    {
        using var client = new TcpClient(address.AddressFamily);
        try
        {
            await client.ConnectAsync(address, port);
        }
        catch (SocketException ex)
        {
            return $"no connection ({ex.SocketErrorCode})";
        }

        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(Resp.Command("PING")));
        var heard = new StringBuilder();
        var buffer = new byte[256];
        using var patience = new CancellationTokenSource(Patience);
        try
        {
            while (!heard.ToString().EndsWith("\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer, patience.Token);
                if (read == 0)
                    break;
                heard.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Nothing more came.
        }

        return heard.ToString();
    }

    private static string[] Lines(List<string> log, string containing)
    {
        lock (log)
            return log.Where(line => line.Contains(containing, StringComparison.Ordinal)).ToArray();
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LocalHost")]
    public async Task ATapOnLocalhostAlsoListensOnTheIPv6Loopback(string listenHost)
    {
        Assert.SkipUnless(CanListenOnIPv6Loopback(), "this machine cannot listen on the IPv6 loopback");
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log, listenHost, "127.0.0.1"));
        await tap.StartAsync();
        var port = tap.BoundPort;

        Assert.Equal("+PONG\r\n", await PingAsync(IPAddress.IPv6Loopback, port));
        Assert.Equal("+PONG\r\n", await PingAsync(IPAddress.Loopback, port));
        Assert.Empty(Lines(log, "could not also listen"));
        Assert.Equal(2, tap.ConnectionsAccepted);

        // Stopping the tap closes both listeners.
        await tap.DisposeAsync();
        Assert.StartsWith("no connection", await PingAsync(IPAddress.IPv6Loopback, port));
        Assert.StartsWith("no connection", await PingAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task ATapOnAnAddressListensThereAlone()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log, "127.0.0.1", "127.0.0.1"));
        await tap.StartAsync();

        Assert.Equal("+PONG\r\n", await PingAsync(IPAddress.Loopback, tap.BoundPort));
        if (CanListenOnIPv6Loopback())
            Assert.StartsWith("no connection", await PingAsync(IPAddress.IPv6Loopback, tap.BoundPort));
    }

    [Fact]
    public async Task ATapForwardingToLocalhostReachesAnIPv4OnlyServerAtOnce()
    {
        await using var server = Pong();
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log, "127.0.0.1", "localhost"));
        await tap.StartAsync();

        // Two connections, so neither the first resolution nor the first connect alone decides it.
        foreach (var attempt in new[] { 1, 2 })
        {
            var timer = Stopwatch.StartNew();
            var answer = await PingAsync(IPAddress.Loopback, tap.BoundPort);
            timer.Stop();
            Assert.Equal("+PONG\r\n", answer);
            Assert.True(timer.ElapsedMilliseconds < 1000, $"reply {attempt} through the tap took {timer.ElapsedMilliseconds} ms");
        }
    }

    [Fact]
    public async Task ATapForwardingToLocalhostStillReachesAnIPv6OnlyServer()
    {
        Assert.SkipUnless(CanListenOnIPv6Loopback(), "this machine cannot listen on the IPv6 loopback");
        Assert.SkipUnless(LocalhostNamesTheIPv6Loopback(), "localhost does not name the IPv6 loopback on this machine");
        await using var server = Pong(IPAddress.IPv6Loopback);
        var log = new List<string>();
        await using var tap = new RedisTap(Options(server, log, "127.0.0.1", "localhost"));
        await tap.StartAsync();

        // The IPv4 address is tried first and refused (about 2 s on Windows), then ::1 answers.
        Assert.Equal("+PONG\r\n", await PingAsync(IPAddress.Loopback, tap.BoundPort));
        Assert.Equal(0, tap.UpstreamConnectFailures);
    }

    [Fact]
    public async Task ATapOnLocalhostWhoseIPv6PortIsTakenListensOnIPv4AndSaysSo()
    {
        Assert.SkipUnless(CanListenOnIPv6Loopback(), "this machine cannot listen on the IPv6 loopback");
        await using var server = Pong();
        var log = new List<string>();

        // Something else holds [::1] on a port that 127.0.0.1 has free, and the tap is told to take that port. A port
        // free on ::1 can be taken on 127.0.0.1, so a few ports are tried.
        for (var attempt = 1; ; attempt++)
        {
            using var holder = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            holder.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            holder.Listen();
            var port = ((IPEndPoint)holder.LocalEndPoint!).Port;
            var options = Options(server, log, "localhost", "127.0.0.1");
            options.ListenPort = port;
            await using var tap = new RedisTap(options);
            try
            {
                await tap.StartAsync();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && attempt < 5)
            {
                continue;
            }

            Assert.True(tap.IsListening);
            Assert.Equal(port, tap.BoundPort);
            Assert.Equal("+PONG\r\n", await PingAsync(IPAddress.Loopback, port));
            var line = Assert.Single(Lines(log, "could not also listen"));
            Assert.Contains($"[::1]:{port}", line, StringComparison.Ordinal);

            // No accept loop ran on the listener that failed: it would have said its socket was closed.
            await tap.DisposeAsync();
            Assert.Empty(Lines(log, "stopped accepting"));
            return;
        }
    }

    [Fact]
    public void TheForwardAddressesAreTriedIPv4First()
    {
        var v6 = IPAddress.IPv6Loopback;
        var v4 = IPAddress.Loopback;
        var other6 = IPAddress.Parse("fe80::1");
        var other4 = IPAddress.Parse("10.0.0.1");

        Assert.Equal(new[] { v4, v6 }, TcpTapCore.IPv4First([v6, v4]));
        Assert.Equal(new[] { v4, other4, v6, other6 }, TcpTapCore.IPv4First([v6, v4, other6, other4]));
        Assert.Equal(new[] { v6 }, TcpTapCore.IPv4First([v6]));
    }
}
