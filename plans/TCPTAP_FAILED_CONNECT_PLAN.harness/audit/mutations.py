"""The audit's mutations: exact-string replacements, each applied alone to a copy of the audit's tree, then the facts of
the projects the file feeds. A mutation is killed when at least one fact fails; one that does not build is reported as
such and must be rewritten. Q18 and Q34 are found by the start/stop race's 200 rounds, so their kill is a matter of odds.

    python mutations.py <tree> <output file> [<id> ...]
"""
import pathlib
import re
import subprocess
import sys

TCP = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
OPTIONS = "src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs"
LOOP = "src/Kronikol.Extensions.TcpTap/TapAcceptLoop.cs"
OTLP = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"

PROJECTS = {
    "tcp": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj",
            "FullyQualifiedName~TcpTapConnectFailureTests|FullyQualifiedName~TcpTapLocalhostTests"
            "|FullyQualifiedName~TcpTapLifecycleTests|FullyQualifiedName~TcpTapAcceptTests"),
    "otlp": ("tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj",
             "FullyQualifiedName~OtlpTapAcceptTests|FullyQualifiedName~OtlpTapLifecycleTests"),
}

MUTATIONS = [
    ("Q1 ConnectTimeout of zero or less taken", OPTIONS, ["tcp"],
     """        if (ConnectTimeout <= TimeSpan.Zero && ConnectTimeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), "ConnectTimeout must be positive, or Timeout.InfiniteTimeSpan for none.");
""", ""),
    ("Q2 a ConnectTimeout past the timer's reach kept as a deadline", OPTIONS, ["tcp"],
     "ConnectTimeout == Timeout.InfiniteTimeSpan || ConnectTimeout >= LongestConnectDeadline ? null : ConnectTimeout;",
     "ConnectTimeout == Timeout.InfiniteTimeSpan ? null : ConnectTimeout;"),
    ("Q3 the deadline set from ConnectTimeout as it is", TCP, ["tcp"],
     """        if (_options.ConnectLimit is { } deadline)
            connectCts.CancelAfter(deadline);""",
     """        connectCts.CancelAfter(_options.ConnectTimeout);"""),
    ("Q4 a factory's null decoder not counted", TCP, ["tcp"],
     """        if (decoder is null)
        {
            Interlocked.Increment(ref _decodeErrors);
            DisableDecoding(context.ConnectionId, "DecoderFactory returned null", "the decoder factory returned no decoder");
        }
""", ""),
    ("Q5 the upstream's client made outside the counted path", TCP, ["tcp"],
     """        TcpClient? upstream = null;
        try
        {
            // Inside the counted path: with the process out of file descriptors the accept can take the last one, and
            // the upstream's socket then cannot be made.
            upstream = CreateUpstream();""",
     """        TcpClient? upstream = CreateUpstream();
        try
        {"""),
    ("Q6 an address looked up as a name", TCP, ["tcp"],
     """            var addresses = IPAddress.TryParse(_options.ForwardHost, out var literal)
                ? [literal]
                : await ResolveForward(_options.ForwardHost, connectCts.Token).ConfigureAwait(false);""",
     """            var addresses = await ResolveForward(_options.ForwardHost, connectCts.Token).ConfigureAwait(false);"""),
    ("Q7 the lookup on the tap's token, outside ConnectTimeout", TCP, ["tcp"],
     "ResolveForward(_options.ForwardHost, connectCts.Token)",
     "ResolveForward(_options.ForwardHost, ct)"),
    ("Q8 the forward addresses in the resolver's order", TCP, ["tcp"],
     "await upstream.ConnectAsync(IPv4First(addresses), _options.ForwardPort, connectCts.Token)",
     "await upstream.ConnectAsync(addresses, _options.ForwardPort, connectCts.Token)"),
    ("Q9 the forward target bracketed by the old rule", TCP, ["tcp"],
     """    internal static string HostAndPort(string host, int port) =>
        IPAddress.TryParse(host, out var address)
            ? new IPEndPoint(address, port).ToString()
            : string.Create(CultureInfo.InvariantCulture, $"{host}:{port}");""",
     """    internal static string HostAndPort(string host, int port) =>
        host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";"""),
    ("Q10 OtlpTap shows an IPv6 address unbracketed", OTLP, ["otlp"],
     """            ? $"[{address}]"
            : host;""",
     """            ? host
            : host;"""),
    ("Q11 OtlpTap shows :: as itself", OTLP, ["otlp"],
     """is "+" or "*" or "any" or "0.0.0.0" or "::" or "[::]" or "::0")""",
     """is "+" or "*" or "any" or "0.0.0.0")"""),
    ("Q12 the ::1 listener made outside the guarded step", TCP, ["tcp"],
     """        TcpListener? listener = null;
        try
        {
            listener = CreateListener(address, BoundPort);""",
     """        TcpListener? listener = CreateListener(address, BoundPort);
        try
        {"""),
    ("Q13 a taken ::1 port not reported", TCP, ["tcp"],
     """                Volatile.Write(ref _alsoListenTaken, endpoint.ToString());
""", ""),
    ("Q14 a taken ::1 port worded as before", TCP, ["tcp"],
     """, which something else holds ({ex.Message}); a client that dials {_options.ListenHost} and tries {address} first may reach it instead of the tap, and its calls are then missing");""",
     """ ({ex.Message}); a client that dials {_options.ListenHost} may try it first and wait for the refusal");"""),
    ("Q15 a second start taken", TCP, ["tcp"],
     """            if (_started)
                throw new InvalidOperationException("This tap has already been started; a tap starts once and stops when it is disposed.");
""", ""),
    ("Q16 the start's token linked into the tap's lifetime", TCP, ["tcp"],
     "            _cts = new CancellationTokenSource();",
     "            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);"),
    ("Q17 a start whose token is cancelled goes ahead", TCP, ["tcp"],
     """            cancellationToken.ThrowIfCancellationRequested();
""", ""),
    ("Q18 the stop's first step outside the lock", TCP, ["tcp"],
     """        lock (_lifecycle)
        {
            if (_disposed == 1)
                return;
            _disposed = 1;
        }""",
     """        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;"""),
    ("Q19 every interface on IPv4 alone", TCP, ["tcp"],
     """        if (host is "*" or "+" && Socket.OSSupportsIPv6)""",
     """        if (host is "**" && Socket.OSSupportsIPv6)"""),
    ("Q20 localhost. not localhost", TCP, ["tcp"],
     """        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.Equals("localhost.", StringComparison.OrdinalIgnoreCase);""",
     """        host.Equals("localhost", StringComparison.OrdinalIgnoreCase);"""),
    ("Q21 the stopped line reads the listener's address after it closed", LOOP, ["tcp", "otlp"],
     """_log($"stopped accepting on {endpoint}:""",
     """_log($"stopped accepting on {Endpoint(listener)}:"""),
    ("Q22 a listener closed under a pending accept counted as a failure", LOOP, ["tcp", "otlp"],
     """when (ex is ObjectDisposedException or InvalidOperationException || IsClosed(listener))""",
     """when (ex is ObjectDisposedException or InvalidOperationException)"""),
    ("Q23 the first failed accept's time and cause overwritten", LOOP, ["tcp", "otlp"],
     """                if (Interlocked.CompareExchange(ref _firstFailureAtTicks, now.UtcTicks, 0) == 0)
                    Volatile.Write(ref _firstFailure, Describe(ex));""",
     """                Interlocked.Exchange(ref _firstFailureAtTicks, now.UtcTicks);
                Volatile.Write(ref _firstFailure, Describe(ex));"""),
    ("Q24 the once-a-minute pace on the wall clock", LOOP, ["tcp", "otlp"],
     """                else if (Clock.GetElapsedTime(lastReportedAt, nowStamp) >= ReportInterval)""",
     """                else if (now - firstInARow >= ReportInterval)"""),
    ("Q25 the still-failing line raises no event", LOOP, ["tcp", "otlp"],
     """                    ReportFailure(string.Create(CultureInfo.InvariantCulture,
                        $"accept on {endpoint} still failing:""",
     """                    _log(string.Create(CultureInfo.InvariantCulture,
                        $"accept on {endpoint} still failing:"""),
    ("Q26 the first failed connect's time on the wall clock", TCP, ["tcp"],
     "Interlocked.CompareExchange(ref _firstConnectFailureAtTicks, Clock.GetUtcNow().UtcTicks, 0);",
     "Interlocked.CompareExchange(ref _firstConnectFailureAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);"),
    ("Q27 the first failed connect's time overwritten", TCP, ["tcp"],
     "Interlocked.CompareExchange(ref _firstConnectFailureAtTicks, Clock.GetUtcNow().UtcTicks, 0);",
     "Interlocked.Exchange(ref _firstConnectFailureAtTicks, Clock.GetUtcNow().UtcTicks);"),
    ("Q28 OtlpTap starts after its stop", OTLP, ["otlp"],
     """            ObjectDisposedException.ThrowIf(_disposed == 1, this);
""", ""),
    ("Q29 OtlpTap's start token linked into its lifetime", OTLP, ["otlp"],
     "            _cts = new CancellationTokenSource();",
     "            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);"),
    ("Q30 OtlpTap's taken ::1 port not reported", OTLP, ["otlp"],
     """                    Volatile.Write(ref _alsoBindTaken, new IPEndPoint(address, port).ToString());
""", ""),
    ("Q31 OtlpTap's listener made outside the guarded step", OTLP, ["otlp"],
     """            TcpListener? listener = null;
            try
            {
                listener = CreateListener(address, port);""",
     """            TcpListener? listener = CreateListener(address, port);
            try
            {"""),
    ("Q32 OtlpTap starts with a cancelled token", OTLP, ["otlp"],
     """            cancellationToken.ThrowIfCancellationRequested();
""", ""),
    ("Q33 OtlpTap takes a second start", OTLP, ["otlp"],
     """            if (_started)
                throw new InvalidOperationException("This tap has already been started; a tap starts once and stops when it is disposed.");
""", ""),
    ("Q34 OtlpTap's stop outside the lock", OTLP, ["otlp"],
     """        lock (_lifecycle)
        {
            if (_disposed == 1)
                return;
            Volatile.Write(ref _disposed, 1);
        }""",
     """        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;"""),
    ("Q36 the pause not waiting on the tap's token (R1's M8, against the rewritten A5)", LOOP, ["tcp", "otlp"],
     "static (pause, ct) => Task.Delay(pause, ct);",
     "static (pause, ct) => Task.Delay(pause);"),
    ("Q37 a tap-wide event printed as connection 0", "src/Kronikol.Extensions.TcpTap/CaptureDegradation.cs", ["tcp"],
     """{(ConnectionId == 0 ? "tap-wide" : $"connection {ConnectionId}")}""",
     """connection {ConnectionId}"""),
    ("Q35 the forward connects by name, as before 4.22.1", TCP, ["tcp"],
     """            var addresses = IPAddress.TryParse(_options.ForwardHost, out var literal)
                ? [literal]
                : await ResolveForward(_options.ForwardHost, connectCts.Token).ConfigureAwait(false);
            await upstream.ConnectAsync(IPv4First(addresses), _options.ForwardPort, connectCts.Token).ConfigureAwait(false);""",
     """            await upstream.ConnectAsync(_options.ForwardHost, _options.ForwardPort, connectCts.Token).ConfigureAwait(false);"""),
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
