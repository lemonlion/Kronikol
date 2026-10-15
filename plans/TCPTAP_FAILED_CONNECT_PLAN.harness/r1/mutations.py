"""R1's mutations (plans/TCPTAP_FAILED_CONNECT_PLAN.md section 5): exact-string replacements, each applied alone to a
copy of the release's tree, then the accept facts of the projects the file feeds. A mutation is killed when at least one
fact fails; one that does not build is reported as such and must be rewritten.

    python mutations.py <tree> <output file>
"""
import pathlib
import re
import subprocess
import sys

LOOP = "src/Kronikol.Extensions.TcpTap/TapAcceptLoop.cs"
TCP = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
OTLP = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"

PROJECTS = {
    "tcp": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj", "FullyQualifiedName~TcpTapAcceptTests"),
    "otlp": ("tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj", "FullyQualifiedName~OtlpTapAcceptTests"),
}

MUTATIONS = [
    ("M1 the return back in the loop", LOOP, ["tcp", "otlp"],
     """            catch (Exception ex)
            {
                var now = Clock.GetUtcNow();""",
     """            catch (Exception ex)
            {
                if (ex is SocketException)
                    return;
                var now = Clock.GetUtcNow();"""),
    ("M2 no pause", LOOP, ["tcp", "otlp"],
     "await Pause(pause, ct).ConfigureAwait(false);",
     "await Task.CompletedTask.ConfigureAwait(false);"),
    ("M3 the pause never resets", LOOP, ["tcp", "otlp"],
     """                failuresInARow = 0;
                pause = FirstPause;""",
     """                failuresInARow = 0;"""),
    ("M4 a failure while stopping counted", LOOP, ["tcp", "otlp"],
     """            catch (Exception) when (ct.IsCancellationRequested)
            {
                return; // the tap is stopping
            }
""",
     ""),
    ("M5 a log line for every failure", LOOP, ["tcp", "otlp"],
     "else if (now - lastReported >= ReportInterval)",
     "else if (now - lastReported >= TimeSpan.Zero)"),
    ("M6 OtlpTap's loop left as it is", OTLP, ["otlp"],
     """    private Task AcceptLoopAsync(TcpListener listener, CancellationToken ct) =>
        Accepting.RunAsync(listener, client => _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None), ct);""",
     """    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await Accepting.Accept(listener, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client, ct), CancellationToken.None);
        }
    }"""),
    ("M7 the loop's Log call unguarded (TcpTap)", TCP, ["tcp"],
     """Accepting = new TapAcceptLoop(line => TryLog($"[{_options.DisplayName}] tcp-tap {line}"));""",
     """Accepting = new TapAcceptLoop(line => _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap {line}"));"""),
    ("M7b the loop's Log call unguarded (OtlpTap)", OTLP, ["otlp"],
     """Accepting = new TapAcceptLoop(line => TryLog($"[{_options.DisplayName}] otlp-tap {line}"));""",
     """Accepting = new TapAcceptLoop(line => _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap {line}"));"""),
    ("M8 the pause not waiting on the tap's token", LOOP, ["tcp", "otlp"],
     "static (pause, ct) => Task.Delay(pause, ct);",
     "static (pause, ct) => Task.Delay(pause);"),
    ("M9 the start line unguarded (TcpTap)", TCP, ["tcp"],
     """        TryLog($"[{_options.DisplayName}] tcp-tap listening on""",
     """        _options.Log?.Invoke($"[{_options.DisplayName}] tcp-tap listening on"""),
    ("M9b the stop line unguarded (TcpTap)", TCP, ["tcp"],
     """        TryLog(
            $"[{_options.DisplayName}] tcp-tap stopped (""",
     """        _options.Log?.Invoke(
            $"[{_options.DisplayName}] tcp-tap stopped ("""),
    ("M9c the start line unguarded (OtlpTap)", OTLP, ["otlp"],
     """        TryLog($"[{_options.DisplayName}] otlp-tap listening on""",
     """        _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap listening on"""),
    ("M9d the stop line unguarded (OtlpTap)", OTLP, ["otlp"],
     """        TryLog(
            $"[{_options.DisplayName}] otlp-tap stopped (""",
     """        _options.Log?.Invoke(
            $"[{_options.DisplayName}] otlp-tap stopped ("""),
    ("M10 a closed listener retried as a failure", LOOP, ["tcp", "otlp"],
     """            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {""",
     """            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException && false)
            {"""),
    ("M11 no accept line in TcpTap's Diagnostics()", TCP, ["tcp"],
     """        if (Accepting.DiagnosticLine(name) is { } acceptLine)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded, acceptLine));
""",
     ""),
    ("M11b no accept line in OtlpTap's Diagnostics()", OTLP, ["otlp"],
     """        if (Accepting.DiagnosticLine(name) is { } acceptLine)
            entries.Add(new DiagnosticEntry(DiagnosticKind.CaptureDegraded, acceptLine));
""",
     ""),
    ("M12 TcpTap's stop line without failed accepts", TCP, ["tcp"],
     ", {Accepting.Failures} failed accepts)",
     ")"),
    ("M12b OtlpTap's stop line without failed accepts", OTLP, ["otlp"],
     """ForwardFailures} forward failures, "
            + $"{Accepting.Failures} failed accepts)");""",
     """ForwardFailures} forward failures)");"""),
    ("M13 the clock ignored", LOOP, ["tcp", "otlp"],
     "var now = Clock.GetUtcNow();",
     "var now = DateTimeOffset.UtcNow;"),
    ("M14 the minute counted from the first line only", LOOP, ["tcp", "otlp"],
     """                    lastReported = now;
                    _log(string.Create(CultureInfo.InvariantCulture,
                        $"accept on {Endpoint(listener)} still failing""",
     """                    _log(string.Create(CultureInfo.InvariantCulture,
                        $"accept on {Endpoint(listener)} still failing"""),
    ("M15 the first failure's description not kept", LOOP, ["tcp", "otlp"],
     "Volatile.Write(ref _firstFailure, Describe(ex));",
     ""),
    ("M16 the pause never capped", LOOP, ["tcp", "otlp"],
     "Math.Min(pause.Ticks * 2, LongestPause.Ticks)",
     "pause.Ticks * 2"),
    ("M17 no line when accepting resumes", LOOP, ["tcp", "otlp"],
     """                _log(string.Create(CultureInfo.InvariantCulture,
                    $"accepting again on""",
     """                _ = (string.Create(CultureInfo.InvariantCulture,
                    $"accepting again on"""),
    ("M19 OtlpTap's could-not-also-bind line unguarded", OTLP, ["otlp"],
     """                TryLog($"[{_options.DisplayName}] otlp-tap could not also bind {address}: {ex.Message}");""",
     """                _options.Log?.Invoke($"[{_options.DisplayName}] otlp-tap could not also bind {address}: {ex.Message}");"""),
    ("M18 a failure counted only when it is the first in a row", LOOP, ["tcp", "otlp"],
     """                if (Interlocked.Increment(ref _failures) == 1)""",
     """                if (failuresInARow == 0 && Interlocked.Increment(ref _failures) == 1)"""),
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
