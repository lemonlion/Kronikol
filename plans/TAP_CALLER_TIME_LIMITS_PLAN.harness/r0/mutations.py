"""R0's mutations (plans/TAP_CALLER_TIME_LIMITS_PLAN.md section 5): exact-string replacements, each applied alone to a copy
of the release's tree, then the facts of the projects the file feeds. A mutation is killed when at least one fact fails;
one that does not build is reported as such and must be rewritten.

    python mutations.py <tree> <output file> [<id> ...]
"""
import pathlib
import re
import subprocess
import sys

PT = "src/Kronikol.Extensions.ProxyTap/ProxyTap.cs"
PTO = "src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs"
OT = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"
OTO = "src/Kronikol.Extensions.Otlp/OtlpTapOptions.cs"
RD = "src/Kronikol.Extensions.Otlp/HttpRequestReader.cs"
TT = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
TTO = "src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs"

PROJECTS = {
    "proxy": ("tests/Kronikol.Tests.ProxyTap/Kronikol.Tests.ProxyTap.csproj",
              "FullyQualifiedName~ProxyTapStoppingTests|FullyQualifiedName~ProxyTapOptionRangeTests|FullyQualifiedName~ProxyTapLogTests"),
    "otlp": ("tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj",
             "FullyQualifiedName~OtlpTapRefusalTests|FullyQualifiedName~HttpRequestReaderTests|FullyQualifiedName~OtlpTapLogTests|FullyQualifiedName~OtlpExportSinkLogTests"),
    "tcp": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj", "FullyQualifiedName~TcpTapLogTests"),
}

MUTATIONS = [
    # ---- ProxyTap
    ("P1 the client's timeout ForwardTimeout plus 10 s again (R0-1)", PT, ["proxy"],
     """            Timeout = forwardDeadline is { } forward && forward < ProxyTapOptions.LongestDeadline - ClientTimeoutBackstop
                ? forward + ClientTimeoutBackstop
                : Timeout.InfiniteTimeSpan,""",
     """            Timeout = _options.ForwardTimeout + ClientTimeoutBackstop,"""),
    ("P2 a zero ForwardTimeout accepted (R0-7)", PTO, ["proxy"],
     "if (ForwardTimeout <= TimeSpan.Zero && ForwardTimeout != Timeout.InfiniteTimeSpan)",
     "if (ForwardTimeout < TimeSpan.Zero && ForwardTimeout != Timeout.InfiniteTimeSpan)"),
    ("P3 the forward's deadline set whatever its length (R0-7)", PT, ["proxy"],
     """        if (ProxyTapOptions.Deadline(_options.ForwardTimeout) is { } forwardDeadline)
            deadline.CancelAfter(forwardDeadline);""",
     """        deadline.CancelAfter(_options.ForwardTimeout);"""),
    ("P4 ConnectTimeout handed to the handler whatever its length (R0-7)", PT, ["proxy"],
     "ConnectTimeout = ProxyTapOptions.Deadline(_options.ConnectTimeout) ?? Timeout.InfiniteTimeSpan,",
     "ConnectTimeout = _options.ConnectTimeout,"),
    ("P5 no answer still reading its body ended as the stop begins (R0-8)", PT, ["proxy"],
     """        foreach (var answer in _answers.Keys)
            answer.EndWhileReading(503);
""",
     ""),
    ("P6 no drain before the listener closes (R0-8)", PT, ["proxy"],
     "for (var waited = 0; Volatile.Read(ref _inflight) > 0 && waited < 2_000; waited += 25)",
     "for (var waited = 0; Volatile.Read(ref _inflight) > 0 && waited < 0; waited += 25)"),
    ("P7 a request that arrives while the tap stops is forwarded (R0-8)", PT, ["proxy"],
     """            if (Stopping)
                RefuseWhileStopping(answer);""",
     """            if (Stopping && answer is null)
                RefuseWhileStopping(answer!);"""),
    ("P8 an answer written while the tap stops keeps its connection (R0-8)", PT, ["proxy"],
     """        if (Stopping)
            response.KeepAlive = false;""",
     """        if (Stopping && response is null)
            response!.KeepAlive = false;"""),
    ("P9 a forward cut short by the stop not answered (R0-8)", PT, ["proxy"],
     """        if (!answer.IsOpen)
            return; // closed, or ended by the tap's stopping: nothing left to tell""",
     """        if (!answer.IsOpen || Stopping)
            return; // closed, or ended by the tap's stopping: nothing left to tell"""),
    ("P10 the lifetime read from _cts again (R0-12)", PT, ["proxy"],
     "var lifetime = _lifetime;",
     "var lifetime = _cts?.Token ?? CancellationToken.None;"),
    ("P11 the forward begun after the stop ended the answer (R0-8)", PT, ["proxy"],
     """        if (!answer.TryBeginForward())
            return;""",
     """        answer.TryBeginForward();"""),
    ("P12 the start line unguarded (R0-5)", PT, ["proxy"],
     """        TryLog($"[{_options.DisplayName}] proxy-tap listening on""",
     """        _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap listening on"""),
    ("P13 a capture fault's line unguarded (R0-5)", PT, ["proxy"],
     """        TryLog($"[{_options.DisplayName}] proxy-tap: {what} threw""",
     """        _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap: {what} threw"""),
    ("P14 the catch-all's line unguarded (R0-5)", PT, ["proxy"],
     """                TryLog($"[{_options.DisplayName}] proxy-tap error: {ex.Message}");""",
     """                _options.Log?.Invoke($"[{_options.DisplayName}] proxy-tap error: {ex.Message}");"""),
    ("P15 a negative BodyCapBytes accepted (R0-6)", PTO, ["proxy"],
     "if (BodyCapBytes is < 0)",
     "if (BodyCapBytes is < -1)"),
    ("P16 a forward the stop came before still begun (R0-8, R0-12)", PT, ["proxy"],
     """        if (Stopping)
        {
            RefuseWhileStopping(answer);
            return;
        }""",
     ""),
    ("P17 the refusal before the forward never answered (R0-8)", PT, ["proxy"],
     """        if (Stopping)
        {
            RefuseWhileStopping(answer);
            return;
        }""",
     """        if (Stopping)
        {
            return;
        }"""),
    # ---- OtlpTap
    ("O1 the answer closed at once, no FIN and no linger (R0-2)", OT, ["otlp"],
     """        try
        {
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return; // the peer is gone already
        }

        var declared = unread is { Framing: BodyFraming.ContentLength } head ? head.ContentLength : (long?)null;
        await reader.LingerAsync(declared, LingerQuiet, LingerTotal, ct).ConfigureAwait(false);""",
     """        await Task.CompletedTask;"""),
    ("O2 a FIN, then the close at once (R0-2)", OT, ["otlp"],
     "        await reader.LingerAsync(declared, LingerQuiet, LingerTotal, ct).ConfigureAwait(false);",
     "        _ = declared;"),
    ("O3 the secret checked after the body is read (R0-2)", OT, ["otlp"],
     """                    // Refused before anything of the body is read or allocated.
                    if (Refusal(head) is { } refusal)""",
     """                    if (head.Framing is BodyFraming.ContentLength && await reader.ReadBodyAsync(head, ct).ConfigureAwait(false) is null)
                        return;
                    if (Refusal(head) is { } refusal)"""),
    ("O4 the body allocated at its declared length (R0-3)", RD, ["otlp"],
     "var result = new byte[Math.Min(count, FirstBodyBytes)];",
     "var result = new byte[count];"),
    ("O5 a sign taken in Content-Length (R0-4)", RD, ["otlp"],
     """            if (c is < '0' or > '9')
                return BodyFraming.InvalidLength;""",
     """            if (c is (< '0' or > '9') and not '+' and not '-')
                return BodyFraming.InvalidLength;"""),
    ("O6 digits past int answered 400, not 413 (R0-4)", RD, ["otlp"],
     ": BodyFraming.LengthPastInt;",
     ": BodyFraming.InvalidLength;"),
    ("O7 an over-cap drain cut short still refused as too large (R0-9)", RD, ["otlp"],
     """                if (!await DiscardAsync(head.ContentLength, ct).ConfigureAwait(false))
                    return null;""",
     """                await DiscardAsync(head.ContentLength, ct).ConfigureAwait(false);"""),
    ("O8 both framings keep the connection (R0-11)", RD, ["otlp"],
     """            if (hasLength)
                keepAlive = false;""",
     """            if (hasLength && transferEncoding.Length < 0)
                keepAlive = false;"""),
    ("O9 the stop line without the new counters (R0-10)", OT, ["otlp"],
     "{PayloadsDropped} dropped, {PayloadsRejected} rejected, {PayloadsFailed} failed, ",
     "{PayloadsDropped} dropped, "),
    ("O10 the unauthenticated line unguarded (R0-5)", OT, ["otlp"],
     """            TryLog($"[{_options.DisplayName}] otlp-tap rejected an unauthenticated""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap rejected an unauthenticated"""),
    ("O11 the map loop's line unguarded (R0-5)", OT, ["otlp"],
     """            TryLog($"[{_options.DisplayName}] otlp-tap could not map an export payload""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap could not map an export payload"""),
    ("O12 the queue-full line unguarded (R0-5)", OT, ["otlp"],
     """            TryLog($"[{_options.DisplayName}] otlp-tap dropped an export payload \"""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap dropped an export payload \""""),
    ("O13 a negative ContentCapBytes accepted (R0-6)", OTO, ["otlp"],
     "if (ContentCapBytes is < 0)",
     "if (ContentCapBytes is < -1)"),
    # ---- the export sink, found while implementing R0
    ("E1 the exporter's refused-batch line unguarded", "src/Kronikol.Extensions.Otlp/OtlpExporter.cs", ["otlp"],
     """                TryLog($"[{_options.DisplayName}] export batch of {page.Count} span(s) answered""",
     """                _options.Log?.Invoke($"[{_options.DisplayName}] export batch of {page.Count} span(s) answered"""),
    ("E2 the sink's drop line unguarded", "src/Kronikol.Extensions.Otlp/OtlpExportSink.cs", ["otlp"],
     """            TryLog($"[{_options.DisplayName}] dropped a captured entry""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] dropped a captured entry"""),
    ("E3 the sink's flush-timeout line unguarded", "src/Kronikol.Extensions.Otlp/OtlpExportSink.cs", ["otlp"],
     """            TryLog($"[{_options.DisplayName}] flush timed out""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] flush timed out"""),
    ("E4 the sink's stop line unguarded", "src/Kronikol.Extensions.Otlp/OtlpExportSink.cs", ["otlp"],
     """        TryLog(
            $"[{_options.DisplayName}] export sink stopped""",
     """        _options.Log?.Invoke(
            $"[{_options.DisplayName}] export sink stopped"""),
    # ---- TcpTap
    ("T1 the decoder's dispose line unguarded (R0-5)", TT, ["tcp"],
     """catch (Exception ex) { TryLog($"[{_options.DisplayName}] tcp-tap decoder dispose failed""",
     """catch (Exception ex) { _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap decoder dispose failed"""),
    ("T2 the reap line unguarded (R0-5)", TT, ["tcp"],
     """            TryLog($"[{_options.DisplayName}] tcp-tap connection {state.ConnectionId} reaped""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap connection {state.ConnectionId} reaped"""),
    ("T3 the queue-full line unguarded (R0-5)", TT, ["tcp"],
     """            TryLog($"[{_options.DisplayName}] tcp-tap decode queue full""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap decode queue full"""),
    ("T4 the decoder-reset line unguarded (R0-5)", TT, ["tcp"],
     """            TryLog(
                $"[{_options.DisplayName}] tcp-tap decoder reset on connection""",
     """            _options.Log?.Invoke(
                $"[{_options.DisplayName}] tcp-tap decoder reset on connection"""),
    ("T5 the reset-threw line unguarded (R0-5)", TT, ["tcp"],
     """            TryLog($"[{_options.DisplayName}] tcp-tap decoder reset threw""",
     """            _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap decoder reset threw"""),
    ("T6 the decoding-disabled line unguarded (R0-5)", TT, ["tcp"],
     """        TryLog(
            $"[{_options.DisplayName}] tcp-tap {what} on connection""",
     """        _options.Log?.Invoke(
            $"[{_options.DisplayName}] tcp-tap {what} on connection"""),
    ("T7 the closed-mid-message line unguarded (R0-5)", TT, ["tcp"],
     """        TryLog($"[{_options.DisplayName}] tcp-tap connection {connectionId} closed mid-message""",
     """        _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap connection {connectionId} closed mid-message"""),
    ("T8 the identity resolver's line unguarded (R0-5)", TT, ["tcp"],
     """                TryLog($"[{_options.DisplayName}] tcp-tap identity resolver threw""",
     """                _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap identity resolver threw"""),
    ("T9 a decoder's own line unguarded (R0-5)", TT, ["tcp"],
     """    internal void Diagnostic(string message) => TryLog($"[{_options.DisplayName}] {message}");""",
     """    internal void Diagnostic(string message) => _options.Log?.Invoke($"[{_options.DisplayName}] {message}");"""),
    ("T10 a negative BodyCapBytes accepted (R0-6)", TTO, ["tcp"],
     "if (BodyCapBytes is < 0)",
     "if (BodyCapBytes is < -1)"),
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
