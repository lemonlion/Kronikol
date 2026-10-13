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

    [Fact]
    public void Ports_handed_out_one_after_another_are_all_different()
    {
        // Windows offers ports in turn, so this holds there whatever the helper does; Linux offers them at random, and
        // a thousand probes repeat one there unless the helper skips it.
        var ports = Enumerable.Range(0, 1000).Select(_ => LoopbackPort.Free()).ToList();

        Assert.Equal(ports.Count, ports.Distinct().Count());
    }
}
