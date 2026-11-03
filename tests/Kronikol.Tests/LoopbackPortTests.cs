using System.Net;
using System.Net.Sockets;

namespace Kronikol.Tests;

public class LoopbackPortTests
{
    // Ports below 1024 never come from binding port 0, so the numbers these facts feed the helper cannot collide with
    // a port it has handed out to a real listener in this process.

    [Fact]
    public void A_port_the_OS_offers_twice_is_handed_out_once()
    {
        // On Linux the OS can offer the number a probe just released to the next probe, and two tests running at once
        // each started a listener on it: the second failed with "conflicts with an existing registration on the
        // machine" (ProxyTapFailureTests on CI, 2026-10-13).
        var offered = new Queue<int>([1001, 1001, 1002]);

        var first = LoopbackPort.Free(offered.Dequeue);
        var second = LoopbackPort.Free(offered.Dequeue);

        Assert.Equal(1001, first);
        Assert.Equal(1002, second);
    }

    [Fact]
    public void A_probe_that_offers_only_ports_handed_out_already_gives_up()
    {
        Assert.Equal(1003, LoopbackPort.Free(() => 1003));

        var ex = Assert.Throws<InvalidOperationException>(() => LoopbackPort.Free(() => 1003));

        Assert.Contains("handed out already", ex.Message);
    }

    [Fact]
    public void A_holder_given_a_port_handed_out_already_lets_it_go_and_binds_again()
    {
        // A socket bound to port 0 and held (ClosedPort, a raw TCP stub) could be given the port a listener was handed
        // a moment before and has not started on yet; the listener then failed to start.
        Assert.Equal(1004, LoopbackPort.Free(() => 1004));
        var offered = new Queue<int>([1004, 1005]);
        var released = new List<int>();

        var held = LoopbackPort.Hold(offered.Dequeue, port => port, released.Add);

        Assert.Equal(1005, held);
        Assert.Equal([1004], released);
    }

    /// <summary>
    /// A number that came from binding port 0 and was let go can be offered to the next program that asks, and to any client's
    /// connection, so a second test program started a listener on it first: 8 copies of the ProxyTap project at once failed 5 of 48
    /// runs on 2026-11-02, a different fact each time, with "conflicts with an existing registration on the machine" or "being used
    /// by another process". These numbers are below where any system takes port 0 from: Linux's range starts at 32768, Windows' and
    /// macOS's at 49152.
    /// </summary>
    [Fact]
    public void A_port_comes_from_below_every_systems_ephemeral_range()
    {
        var ports = Enumerable.Range(0, 300).Select(_ => LoopbackPort.Free()).ToList();

        Assert.All(ports, port => Assert.InRange(port, LoopbackPort.FirstPort, LoopbackPort.LastPort));
        Assert.True(LoopbackPort.LastPort < 32768, "the range reaches into Linux's ephemeral ports");
        Assert.True(LoopbackPort.FirstPort > 10247, "the range reaches into what a stock Windows reserves in http.sys");
    }

    /// <summary>The claim is a name on the machine: a second program asking for a block is refused just as a second ask from this one is, as a run measures across processes.</summary>
    [Fact]
    public void A_block_someone_holds_is_not_claimed_again()
    {
        var first = LoopbackPort.ClaimBlock(preferred: 40);
        var second = LoopbackPort.ClaimBlock(preferred: first);

        Assert.True(first >= 0, "no block was free");
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// http.sys holds what a socket cannot see: a stock Windows reserves these numbers for its own services (http://+:10243/WMPNSSv4/), and
    /// the first cut of this range began at 10000, so the program that drew that block failed with "Access is denied". What a
    /// real listener says of each is what the check must say, on a machine with those reservations or without.
    /// </summary>
    [Theory]
    [InlineData(10243)]
    [InlineData(10245)]
    [InlineData(10246)]
    [InlineData(10247)]
    public void A_port_a_listener_is_refused_is_seen_as_taken(int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        bool started;
        try
        {
            listener.Start();
            listener.Close();
            started = true;
        }
        catch (HttpListenerException)
        {
            started = false;
        }

        Assert.Equal(started, LoopbackPort.CanListen(port));
    }

    /// <summary>A real service on one of these numbers, or a range the system keeps for itself (Hyper-V does), is no port to hand out.</summary>
    [Fact]
    public void A_port_someone_is_listening_on_is_not_handed_out()
    {
        var next = LoopbackPort.Free() + 1;
        var taken = new List<TcpListener>();
        try
        {
            for (var port = next; port < next + 20 && port <= LoopbackPort.LastPort; port++)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                taken.Add(listener);
            }

            var busy = taken.Select(listener => ((IPEndPoint)listener.LocalEndpoint).Port).ToHashSet();
            var handed = Enumerable.Range(0, 40).Select(_ => LoopbackPort.Free()).ToList();

            Assert.Empty(handed.Where(busy.Contains));
        }
        finally
        {
            foreach (var listener in taken)
                listener.Stop();
        }
    }

    [Fact]
    public void Ports_handed_out_one_after_another_are_all_different()
    {
        // Windows offers ports in turn, so this holds there whatever the helper does; Linux offers them at random, and
        // a thousand probes repeat one there unless the helper skips it.
        var ports = Enumerable.Range(0, 1000).Select(_ => LoopbackPort.Free()).ToList();

        Assert.Equal(ports.Count, ports.Distinct().Count());
    }
}
