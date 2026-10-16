"""The audit's red baseline: 4.26.1 (09369515) with only the internal seams the audit's facts name, each wired where
4.26.1 does the same work, so the facts compile and run against 4.26.1's behaviour.

    python red-stub.py <worktree at 09369515>

- TcpTap.CreateUpstream: makes the upstream's client where 4.26.1 does, before the connect's counted path.
- TcpTap.ResolveForward: the name lookup 4.26.1 makes for every ForwardHost, addresses included.
- TcpTap.CreateListener and OtlpTap.CreateListener: make the listeners where 4.26.1 does, outside the guarded step.
- TapAcceptLoop.FirstPause and LongestPause: settable on the loop, as the stopping fact sets them.
"""
import pathlib
import sys

tree = pathlib.Path(sys.argv[1])


def edit(rel, pairs):
    p = tree / rel
    t = p.read_bytes().decode("utf-8")
    for old, new in pairs:
        assert t.count(old) == 1, (rel, old[:90])
        t = t.replace(old, new)
    p.write_bytes(t.encode("utf-8"))


edit("src/Kronikol.Extensions.TcpTap/TcpTap.cs", [
    ("            upstream = new TcpClient { NoDelay = true };",
     "            upstream = CreateUpstream();"),
    ("            var addresses = await Dns.GetHostAddressesAsync(_options.ForwardHost, connectCts.Token).ConfigureAwait(false);",
     "            var addresses = await ResolveForward(_options.ForwardHost, connectCts.Token).ConfigureAwait(false);"),
    ("        var listener = new TcpListener(address, _options.ListenPort);",
     "        var listener = CreateListener(address, _options.ListenPort);"),
    ("        var listener = new TcpListener(address, BoundPort);",
     "        var listener = CreateListener(address, BoundPort);"),
    ("    private static IPAddress ResolveBindAddress(string host)",
     """    internal Func<TcpClient> CreateUpstream { get; set; } = static () => new TcpClient { NoDelay = true };

    internal Func<string, CancellationToken, Task<IPAddress[]>> ResolveForward { get; set; } =
        static (host, ct) => Dns.GetHostAddressesAsync(host, ct);

    internal Func<IPAddress, int, TcpListener> CreateListener { get; set; } = static (address, port) => new TcpListener(address, port);

    private static IPAddress ResolveBindAddress(string host)"""),
])

edit("src/Kronikol.Extensions.TcpTap/TapAcceptLoop.cs", [
    ("    internal static TimeSpan FirstPause { get; } = TimeSpan.FromMilliseconds(10);",
     "    internal TimeSpan FirstPause { get; set; } = TimeSpan.FromMilliseconds(10);"),
    ("    internal static TimeSpan LongestPause { get; } = TimeSpan.FromSeconds(1);",
     "    internal TimeSpan LongestPause { get; set; } = TimeSpan.FromSeconds(1);"),
])

edit("src/Kronikol.Extensions.Otlp/OtlpTap.cs", [
    ("            var listener = new TcpListener(address, port);",
     "            var listener = CreateListener(address, port);"),
    ("    private static (IPAddress Address, bool DualMode)[] BindAddresses(string host)",
     """    internal Func<IPAddress, int, TcpListener> CreateListener { get; set; } = static (address, port) => new TcpListener(address, port);

    private static (IPAddress Address, bool DualMode)[] BindAddresses(string host)"""),
])
print("ok")
