"""R3's mutations (plans/TCPTAP_FAILED_CONNECT_PLAN.md section 5): exact-string replacements, each applied alone to a
copy of the release's tree, then the facts of the projects the file feeds. A mutation is killed when at least one fact
fails; one that does not build is reported as such and must be rewritten. Run on Windows: P8 is killed there alone,
since Linux answers a refused address at once.

    python mutations.py <tree> <output file> [<id> ...]
"""
import pathlib
import re
import subprocess
import sys

TCP = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"

PROJECTS = {
    "tcp": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj", "FullyQualifiedName~TcpTapLocalhostTests"),
}

MUTATIONS = [
    ("P1 no listener on ::1", TCP, ["tcp"],
     """        if (_options.ListenHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) && Socket.OSSupportsIPv6)
            AlsoListen(IPAddress.IPv6Loopback);
""",
     ""),
    ("P2 an address listens on ::1 too", TCP, ["tcp"],
     """        if (_options.ListenHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) && Socket.OSSupportsIPv6)""",
     """        if (Socket.OSSupportsIPv6)"""),
    ("P3 the name compared by case", TCP, ["tcp"],
     """        if (_options.ListenHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) && Socket.OSSupportsIPv6)""",
     """        if (_options.ListenHost.Equals("localhost", StringComparison.Ordinal) && Socket.OSSupportsIPv6)"""),
    ("P4 the second listener's failure fatal", TCP, ["tcp"],
     """            listener.Stop();
            TryLog($"[{_options.DisplayName}] tcp-tap could not also listen on""",
     """            listener.Stop();
            if (ex is not null) throw;
            TryLog($"[{_options.DisplayName}] tcp-tap could not also listen on"""),
    ("P5 the second listener's failure not logged", TCP, ["tcp"],
     """            TryLog($"[{_options.DisplayName}] tcp-tap could not also listen on""",
     """            _ = ($"[{_options.DisplayName}] tcp-tap could not also listen on"""),
    ("P6 the forward addresses in the resolver's order", TCP, ["tcp"],
     """        .. addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork),
        .. addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork),""",
     """        .. addresses,"""),
    ("P7 the forward addresses IPv6 first", TCP, ["tcp"],
     """        .. addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork),
        .. addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork),""",
     """        .. addresses.Where(a => a.AddressFamily != AddressFamily.InterNetwork),
        .. addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork),"""),
    ("P8 the forward connect by name, as before", TCP, ["tcp"],
     """            var addresses = await Dns.GetHostAddressesAsync(_options.ForwardHost, connectCts.Token).ConfigureAwait(false);
            await upstream.ConnectAsync(IPv4First(addresses), _options.ForwardPort, connectCts.Token).ConfigureAwait(false);""",
     """            await upstream.ConnectAsync(_options.ForwardHost, _options.ForwardPort, connectCts.Token).ConfigureAwait(false);"""),
    ("P9 one accept loop, the first listener's", TCP, ["tcp"],
     """        _loop = Task.WhenAll(_listeners.Select(l => Task.Run(() => AcceptLoopAsync(l, token), CancellationToken.None)));""",
     """        _loop = Task.Run(() => AcceptLoopAsync(_listeners[0], token), CancellationToken.None);"""),
    ("P10 stopping closes the first listener alone", TCP, ["tcp"],
     """        foreach (var listener in _listeners)
        {
            try { listener.Stop(); }""",
     """        foreach (var listener in _listeners.Take(1))
        {
            try { listener.Stop(); }"""),
    ("P11 a failed second listener kept in the list", TCP, ["tcp"],
     """            listener.Start(_options.AcceptBacklog);
            _listeners.Add(listener);
        }""",
     """            _listeners.Add(listener);
            listener.Start(_options.AcceptBacklog);
        }"""),
]


def run(args, cwd):
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")


def main():
    tree = pathlib.Path(sys.argv[1])
    out = pathlib.Path(sys.argv[2])
    only = set(sys.argv[3:])
    lines = []
    for name, rel, projects, old, new in MUTATIONS:
        if only and name.split()[0] not in only:
            continue
        path = tree / rel
        original = path.read_bytes()
        text = original.decode("utf-8")
        count = text.count(old)
        if count != 1:
            lines.append(f"{name}: NOT APPLIED (found {count} times)")
            print(lines[-1], flush=True)
            continue
        path.write_bytes(text.replace(old, new).encode("utf-8"))
        try:
            verdicts = []
            for key in projects:
                csproj, test_filter = PROJECTS[key]
                build = run(["dotnet", "build", csproj, "-c", "Debug", "-nologo", "-v", "q"], tree)
                if build.returncode != 0:
                    errors = sorted(set(re.findall(r"error \w+: [^\[]+", build.stdout)))[:3]
                    verdicts.append(f"{key}: BUILD FAILED {errors}")
                    continue
                test = run(["dotnet", "test", csproj, "--no-build", "--filter", test_filter], tree)
                failed = sorted(set(re.findall(r"^\s+Failed (\S+)", test.stdout, re.M)))
                summary = re.findall(r"(Passed!|Failed!)\s+-\s+Failed:\s+(\d+), Passed:\s+(\d+)", test.stdout)
                verdicts.append(f"{key}: {summary} failed facts: {[f.rsplit('.', 1)[-1] for f in failed]}")
            killed = any("Failed!" in v for v in verdicts)
            built = not any("BUILD FAILED" in v for v in verdicts)
            verdict = "KILLED" if killed else ("BUILD FAILED" if not built else "SURVIVED")
            lines.append(f"{name}: {verdict}\n    " + "\n    ".join(verdicts))
            print(lines[-1], flush=True)
        finally:
            path.write_bytes(original)
    out.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


if __name__ == "__main__":
    main()
