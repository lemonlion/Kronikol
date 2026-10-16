"""The #173 audit's mutations (4.26.3), run with R0's runner: each applied alone to a copy of the patch's tree, then the
facts of the projects the file feeds. A mutation is killed when at least one fact fails. The first three are the R1
mutations plan section 5 names and R1's run left out (the audit's A5); the rest undo one fix of the patch each.
The 30 ran on the patch before its rebase onto 4.26.2 (39642161); T9b, T1r and O1r on the rebased tree (26aad29a),
T1r and O1r against 4.26.2's reworked start, which makes the heartbeat after the bind as the patch did.

    python mutations_audit.py <tree> <output file> [<id> ...]
"""
import importlib.util
import pathlib
import sys

here = pathlib.Path(__file__).resolve().parent
runner_path = here.parent / "r0" / "mutations.py"
if not runner_path.exists():
    runner_path = pathlib.Path(sys.argv[1]) / "plans" / "TAP_CALLER_TIME_LIMITS_PLAN.harness" / "r0" / "mutations.py"
spec = importlib.util.spec_from_file_location("runner", runner_path)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

PT = "src/Kronikol.Extensions.ProxyTap/ProxyTap.cs"
OT = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"
RD = "src/Kronikol.Extensions.Otlp/HttpRequestReader.cs"
TT = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
SW = "src/Kronikol.Extensions.TcpTap/StallWatch.cs"

runner.PROJECTS = {
    "proxy": ("tests/Kronikol.Tests.ProxyTap/Kronikol.Tests.ProxyTap.csproj",
              "FullyQualifiedName~StallWatchTests|FullyQualifiedName~ProxyTapStallTests|FullyQualifiedName~ProxyTapLifecycleTests"
              "|FullyQualifiedName~ProxyTapStoppingTests|FullyQualifiedName~A_head_answer_keeps"),
    "otlp": ("tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj",
             "FullyQualifiedName~OtlpTapStallTests|FullyQualifiedName~HttpRequestReaderTests|FullyQualifiedName~OtlpTapRefusalTests"
             "|FullyQualifiedName~OtlpTapLogTests"),
    "tcptap": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj", "FullyQualifiedName~TcpTapStallTests"),
}

runner.MUTATIONS = [
    # ---- R1's, left out of its run (plan section 5, the audit's A5)
    ("A1 the body's entry armed at the first completed read (R1-1)", PT, ["proxy"],
     """            var reading = _watch?.Watch(BodyPhase, _options.StallLimit, () => CutStalled(answer, BodyPhase));
            try
            {
                var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
                requestBody = await ReadBodyAsync(body, reading).ConfigureAwait(false);
            }""",
     """            StallWatch.Entry? reading = null;
            try
            {
                var body = WrapRequestBody is { } wrap ? wrap(request.InputStream) : request.InputStream;
                using var all = new MemoryStream();
                var piece = new byte[PieceBytes];
                int read;
                while ((read = await body.ReadAsync(piece.AsMemory()).ConfigureAwait(false)) > 0)
                {
                    all.Write(piece, 0, read);
                    reading ??= _watch?.Watch(BodyPhase, _options.StallLimit, () => CutStalled(answer, BodyPhase));
                    reading?.Progress();
                }

                requestBody = all.ToArray();
            }"""),
    ("A2 the watch moved into the forward (R1-6)", PT, ["proxy"],
     "        if (!answer.TryBeginForward())\n            return;\n",
     "        if (!answer.TryBeginForward())\n            return;\n        _watch?.Watch(AnswerPhase, _options.StallLimit, () => CutStalled(answer, AnswerPhase));\n"),
    ("A3 the Interlocked move in TimeOut dropped (R1-7)", PT, ["proxy"],
     "                if (Interlocked.CompareExchange(ref _state, end, phase) == phase)\n                    break;",
     "                Volatile.Write(ref _state, end);\n                break;"),

    # ---- the watch
    ("W1 a check still running is asked again (D6)", SW, ["proxy"],
     "        if (!entry.TryBeginCheck())\n            return false;\n",
     ""),

    # ---- TcpTap
    ("T1 the heartbeat made before the bind (P-1)", TT, ["tcptap"],
     "        var address = ResolveBindAddress(_options.ListenHost);",
     "        _watch = new StallWatch(Clock, DebuggerAttached, _ => { });\n        var address = ResolveBindAddress(_options.ListenHost);"),
    ("T2 an entry made for each write (A3)", TT, ["tcptap"],
     "            Volatile.Write(ref Mark(direction), last);\n",
     "            Volatile.Write(ref Mark(direction), last);\n            _tap._watch?.Watch(WritePhase, TimeSpan.MaxValue, () => { });\n"),
    ("T3 a half-close and a stalled write that tie left to the race (D4)", TT, ["tcptap"],
     "            if (limit == ByHalfClose && _writes is not null)",
     "            if (false && limit == ByHalfClose && _writes is not null)"),
    ("T4 the reaper closes outside the pair's one close (D3)", TT, ["tcptap"],
     "            if (!limits.TryCloseForReaper())\n                return;\n",
     ""),
    ("T5 the stop line without the reaped (A1)", TT, ["tcptap"],
     "{StuckConnectionsReaped} stuck connections reaped, ",
     ""),
    ("T6 the stop line without the closed mid-message (A1)", TT, ["tcptap"],
     "{ConnectionsClosedMidMessage} connections closed mid-message, ",
     ""),
    ("T7 a write's mark never cleared (R3-3's M5, the new form)", TT, ["tcptap"],
     "            mark == 0 || Interlocked.CompareExchange(ref Mark(direction), 0, mark) == mark;",
     "            true;"),
    ("T8 a write never due (R3-4's M6, the new form)", TT, ["tcptap"],
     "        private bool WriteDue() => Stalled(TapDirection.ClientToServer) || Stalled(TapDirection.ServerToClient);",
     "        private bool WriteDue() => false;"),
    ("T9 a limit that closes nothing (R3's M8, the new form)", TT, ["tcptap"],
     "            try { _client.Dispose(); } catch { /* already closing */ }\n            try { _upstream.Dispose(); } catch { /* already closing */ }\n        }\n    }",
     "        }\n    }"),
    ("T9b a limit that closes nothing, its cancel removed too (R3's M8 as R3 ran it)", TT, ["tcptap"],
     "            try\n            {\n                _pair.Cancel();\n            }\n            catch (ObjectDisposedException)\n            {\n                // The pair has already ended.\n            }\n\n            try { _client.Dispose(); } catch { /* already closing */ }\n            try { _upstream.Dispose(); } catch { /* already closing */ }\n        }\n    }",
     "        }\n    }"),
    ("T1r the heartbeat made before the bind, on 4.26.2's start (P-1)", TT, ["tcptap"],
     "            Listen();\n",
     "            _watch = new StallWatch(Clock, DebuggerAttached, _ => { });\n            Listen();\n"),

    # ---- OtlpTap
    ("O1 the heartbeat made before the bind (P-1)", OT, ["otlp"],
     "        Bind();\n",
     "        _watch = new StallWatch(Clock, DebuggerAttached, _ => { });\n        Bind();\n"),
    ("O2 a phase the limit cut read as not cut (C1)", OT, ["otlp"],
     "                return Volatile.Read(ref _cut) == (int)CutPhase.None; // a phase the limit cut started no watch of its own",
     "                return true;"),
    ("O3 a refusal counted before the head is the tap's (C2)", OT, ["otlp"],
     "                            if (!limits.End())\n                                break;\n                            CountRefusal(refusal);\n",
     "                            CountRefusal(refusal);\n                            if (!limits.End())\n                                break;\n"),
    ("O4 an empty line before a request line not passed over (C6)", RD, ["otlp"],
     "                for (; skipped < 2 && _start < _end && _buffer[_start] is (byte)'\\r' or (byte)'\\n'; skipped++)\n                    _start++;\n",
     ""),
    ("O5 empty lines passed over without end (C6)", RD, ["otlp"],
     "skipped < 2 && ",
     ""),
    ("O6 a coding after chunked read as no body (X2)", RD, ["otlp"],
     "                ? BodyFraming.UnframedCoding\n",
     "                ? BodyFraming.None\n"),
    ("O7 a coding before chunked read as chunked (X2)", RD, ["otlp"],
     "                : codings.Length == 1 ? BodyFraming.Chunked : BodyFraming.UnknownCoding;",
     "                : BodyFraming.Chunked;"),
    ("O8 a repeated header joined as each comes (C5)", RD, ["otlp"],
     "            if (headers.TryAdd(name, value))\n                continue;\n",
     "            if (headers.TryGetValue(name, out var existing))\n            {\n                headers[name] = existing + \", \" + value;\n                continue;\n            }\n\n            headers[name] = value;\n            continue;\n"),

    ("O1r the heartbeat made before the bind, on 4.26.2's start (P-1)", OT, ["otlp"],
     "            Bind();\n",
     "            _watch = new StallWatch(Clock, DebuggerAttached, _ => { });\n            Bind();\n"),

    # ---- ProxyTap
    ("P1 a second start not refused (lifecycle)", PT, ["proxy"],
     "            if (_started)\n                throw new InvalidOperationException(",
     "            if (_started && false)\n                throw new InvalidOperationException("),
    ("P2 the start token linked into the tap's lifetime (lifecycle)", PT, ["proxy"],
     "            Start();\n            _started = true;\n",
     "            Start();\n            cancellationToken.Register(() => _cts!.Cancel());\n            _started = true;\n"),
    ("P3 a cancelled start token not checked (lifecycle)", PT, ["proxy"],
     "            cancellationToken.ThrowIfCancellationRequested();\n            Start();",
     "            Start();"),
    ("P4 a failed wait for a request ends the loop (K71-1)", PT, ["proxy"],
     "                pause = pause * 2 < LongestPause ? pause * 2 : LongestPause;\n                continue;\n",
     "                return;\n"),
    ("P5 a request tracked only once its handler runs (B2; Linux)", PT, ["proxy"],
     "            Interlocked.Increment(ref _inflight);\n            var answer = new CallerAnswer(context.Response) { BeforeLetGo = BeforeCallerLetGo };\n            _answers.TryAdd(answer, 0);\n            Received?.Invoke(context);\n",
     "            Received?.Invoke(context);\n            Interlocked.Increment(ref _inflight);\n            var answer = new CallerAnswer(context.Response) { BeforeLetGo = BeforeCallerLetGo };\n            _answers.TryAdd(answer, 0);\n"),
    ("P6 the stop line without the failure counts (B4)", PT, ["proxy"],
     ", {ForwardFailures} forward failures, {CallerTimeouts} caller timeouts)",
     ")"),
    ("P7 a cut counted after its caller is let go (B6, 4.26.0's order)", PT, ["proxy"],
     "        if (!answer.TimeOut(body ? 408 : null, () => Interlocked.Increment(ref body ? ref _bodyTimeouts : ref _answerTimeouts)))\n            return;\n",
     "        if (!answer.TimeOut(body ? 408 : null))\n            return;\n        Interlocked.Increment(ref body ? ref _bodyTimeouts : ref _answerTimeouts);\n"),
    ("P8 a HEAD answer's length taken from its empty body (B7)", PT, ["proxy"],
     "        response.ContentLength64 = headLength ?? body.Length;",
     "        response.ContentLength64 = body.Length;"),
    ("P9 the stopping refusal written while the answer still reads (B3; no fact can order it)", PT, ["proxy"],
     "                if (answer.TryBeginForward())\n                    await RefuseWhileStoppingAsync(answer).ConfigureAwait(false);",
     "                await RefuseWhileStoppingAsync(answer).ConfigureAwait(false);"),
]

if __name__ == "__main__":
    runner.main()
