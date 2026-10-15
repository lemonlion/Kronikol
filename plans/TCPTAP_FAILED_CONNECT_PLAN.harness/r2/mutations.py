"""R2's mutations (plans/TCPTAP_FAILED_CONNECT_PLAN.md section 5): exact-string replacements, each applied alone to a
copy of the release's tree, then the facts of the projects the file feeds. A mutation is killed when at least one fact
fails; one that does not build is reported as such and must be rewritten.

    python mutations.py <tree> <output file> [<id> ...]
"""
import pathlib
import re
import subprocess
import sys

TCP = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
LOOP = "src/Kronikol.Extensions.TcpTap/TapAcceptLoop.cs"
OTLP = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"

PROJECTS = {
    "tcp": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj",
            "FullyQualifiedName~TcpTapConnectFailureTests|FullyQualifiedName~TcpTapAcceptTests|FullyQualifiedName~ADecoderThatThrowsStopsDecodingButKeepsForwarding"),
    "otlp": ("tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj", "FullyQualifiedName~OtlpTapAcceptTests"),
}

MUTATIONS = [
    ("N1a an OperationCanceledException taken for the tap stopping", TCP, ["tcp"],
     """        catch (Exception) when (ct.IsCancellationRequested)
        {
            return false; // the tap is stopping: not the upstream's doing
        }
        catch (OperationCanceledException) when (connectCts.IsCancellationRequested)
        {
            CountConnectFailure(id, caller, exception: null);
            return false;
        }""",
     """        catch (OperationCanceledException)
        {
            return false; // the tap is stopping: not the upstream's doing
        }"""),
    ("N1b an OperationCanceledException taken for a timeout", TCP, ["tcp"],
     """        catch (Exception) when (ct.IsCancellationRequested)
        {
            return false; // the tap is stopping: not the upstream's doing
        }""",
     """        catch (Exception) when (ct.IsCancellationRequested && false)
        {
            return false; // the tap is stopping: not the upstream's doing
        }"""),
    ("N2 a timeout counted as failed", TCP, ["tcp"],
     "CountConnectFailure(id, caller, exception: null);",
     "CountConnectFailure(id, caller, new TimeoutException(\"timed out\"));"),
    ("N3 no event for a failed connect", TCP, ["tcp"],
     "        Degrade(id, CaptureDegradationKind.UpstreamConnectFailed, detail);\n",
     ""),
    ("N4 the factory fault still closes the connection", TCP, ["tcp"],
     """            DisableDecoding(context.ConnectionId, ex, "the decoder factory threw");
            return null;""",
     """            DisableDecoding(context.ConnectionId, ex, "the decoder factory threw");
            throw;"""),
    ("N5 the line dropped when there are only timeouts", TCP, ["tcp"],
     "if (failedConnects + timedOutConnects > 0)",
     "if (failedConnects > 0)"),
    ("N6 the outer catch keeping its type filter", TCP, ["tcp"],
     """        catch (Exception ex)
        {
            // Unexpected, whether or not the tap is stopping""",
     """        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Unexpected, whether or not the tap is stopping"""),
    ("N7 the count taken after the log line", TCP, ["tcp"],
     """        Interlocked.CompareExchange(ref _firstConnectFailureAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);
        if (exception is null)
        {
            Interlocked.Increment(ref _connectsTimedOut);
        }
        else
        {
            Volatile.Write(ref _lastConnectFailure, TapAcceptLoop.Describe(exception));
            Interlocked.Increment(ref _connectsFailed);
        }

        var detail = exception is null
            ? string.Create(CultureInfo.InvariantCulture, $"could not connect to {ForwardTarget} within ConnectTimeout ({_options.ConnectTimeout}); closed it without forwarding anything")
            : $"could not connect to {ForwardTarget} ({TapAcceptLoop.Describe(exception)}); closed it without forwarding anything";
        TryLog($"[{_options.DisplayName}] tcp-tap connection {id} from {caller?.ToString() ?? "an unknown address"}: {detail}");
        Degrade(id, CaptureDegradationKind.UpstreamConnectFailed, detail);""",
     """        var detail = exception is null
            ? string.Create(CultureInfo.InvariantCulture, $"could not connect to {ForwardTarget} within ConnectTimeout ({_options.ConnectTimeout}); closed it without forwarding anything")
            : $"could not connect to {ForwardTarget} ({TapAcceptLoop.Describe(exception)}); closed it without forwarding anything";
        TryLog($"[{_options.DisplayName}] tcp-tap connection {id} from {caller?.ToString() ?? "an unknown address"}: {detail}");
        Degrade(id, CaptureDegradationKind.UpstreamConnectFailed, detail);
        Interlocked.CompareExchange(ref _firstConnectFailureAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);
        if (exception is null)
        {
            Interlocked.Increment(ref _connectsTimedOut);
        }
        else
        {
            Volatile.Write(ref _lastConnectFailure, TapAcceptLoop.Describe(exception));
            Interlocked.Increment(ref _connectsFailed);
        }"""),
    ("N8 the catch-all's line unguarded", TCP, ["tcp"],
     """            TryLog($"[{_options.DisplayName}] tcp-tap connection {id} error: {ex.GetType().Name}: {ex.Message}");""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap connection {id} error: {ex.GetType().Name}: {ex.Message}");"""),
    ("N9 Degrade's own line unguarded", TCP, ["tcp"],
     """            TryLog($"[{_options.DisplayName}] OnCaptureDegraded threw""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] OnCaptureDegraded threw"""),
    ("N10 no AcceptFailed event", TCP, ["tcp"],
     """            line => Degrade(0, CaptureDegradationKind.AcceptFailed, line));""",
     """            null);"""),
    ("N11 AcceptFailed raised on a connection's id", TCP, ["tcp"],
     """line => Degrade(0, CaptureDegradationKind.AcceptFailed, line)""",
     """line => Degrade(1, CaptureDegradationKind.AcceptFailed, line)"""),
    ("N12 the factory fault not counted as a decode error", TCP, ["tcp"],
     """            Interlocked.Increment(ref _decodeErrors);
            DisableDecoding(context.ConnectionId, ex, "the decoder factory threw");""",
     """            DisableDecoding(context.ConnectionId, ex, "the decoder factory threw");"""),
    ("N13 the last failure not kept", TCP, ["tcp"],
     "            Volatile.Write(ref _lastConnectFailure, TapAcceptLoop.Describe(exception));",
     "            Interlocked.CompareExchange(ref _lastConnectFailure, TapAcceptLoop.Describe(exception), null);"),
    ("N14 the first failure's time not kept", TCP, ["tcp"],
     "        Interlocked.CompareExchange(ref _firstConnectFailureAtTicks, DateTimeOffset.UtcNow.UtcTicks, 0);\n",
     ""),
    ("N15 the stop line without failed connects", TCP, ["tcp"],
     """{AcceptFailures} failed accepts, " +
            $"{UpstreamConnectFailures} failed connects)");""",
     """{AcceptFailures} failed accepts)");"""),
    ("N16 UpstreamConnectFailures leaves out timeouts", TCP, ["tcp"],
     "public long UpstreamConnectFailures => Interlocked.Read(ref _connectsFailed) + Interlocked.Read(ref _connectsTimedOut);",
     "public long UpstreamConnectFailures => Interlocked.Read(ref _connectsFailed);"),
    ("N17 OtlpTap's public count reads nothing", OTLP, ["otlp"],
     "public long AcceptFailures => Accepting.Failures;",
     "public long AcceptFailures => 0;"),
    ("N18 the accept failure not handed to the tap", LOOP, ["tcp", "otlp"],
     "        _failed?.Invoke(line);\n",
     ""),
    ("N19 the formatter's parts joined with a comma", TCP, ["tcp"],
     """({string.Join("; ", causes)})""",
     """({string.Join(", ", causes)})"""),
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
