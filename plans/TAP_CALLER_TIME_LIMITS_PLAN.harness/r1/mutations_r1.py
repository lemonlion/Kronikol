"""R1's mutations (plans/TAP_CALLER_TIME_LIMITS_PLAN.md section 5), run with R0's runner: each applied alone to a copy of
the release's tree, then the facts of the project the file feeds. A mutation is killed when at least one fact fails.

    python mutations_r1.py <tree> <output file> [<id> ...]
"""
import importlib.util
import pathlib

runner_path = pathlib.Path(__file__).resolve().parent.parent / "r0" / "mutations.py"
spec = importlib.util.spec_from_file_location("runner", runner_path)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

PT = "src/Kronikol.Extensions.ProxyTap/ProxyTap.cs"
PTO = "src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs"
PTX = "src/Kronikol.Extensions.ProxyTap/ProxyTapServiceCollectionExtensions.cs"
SW = "src/Kronikol.Extensions.TcpTap/StallWatch.cs"

runner.PROJECTS = {
    "proxy": ("tests/Kronikol.Tests.ProxyTap/Kronikol.Tests.ProxyTap.csproj",
              "FullyQualifiedName~ProxyTapStallTests|FullyQualifiedName~ProxyTapRegistrationTests|FullyQualifiedName~ProxyTapStoppingTests"),
}

runner.MUTATIONS = [
    ("S1 progress never recorded: a total deadline (R1-4, R1-5)", SW, ["proxy"],
     "public void Progress() => Volatile.Write(ref _lastProgress, Owner._clock.GetTimestamp());",
     "public void Progress() { }"),
    ("S2 a transfer's end leaves its entry behind (R1-10)", SW, ["proxy"],
     """            var ended = Interlocked.CompareExchange(ref _state, Ended, Active) == Active;
            Owner._entries.TryRemove(this, out _);""",
     """            var ended = Interlocked.CompareExchange(ref _state, Ended, Active) == Active;"""),
    ("S3 limits fire under a debugger (R1-8)", SW, ["proxy"],
     "            if (_debuggerAttached())\n                return;",
     "            if (_debuggerAttached() && false)\n                return;"),
    ("S4 the limit added to a timestamp (R1-9, TimeSpan.MaxValue)", SW, ["proxy"],
     "if (!entry.IsActive || _clock.GetElapsedTime(entry.LastProgress) < entry.Limit)",
     "if (!entry.IsActive || _clock.GetTimestamp() < entry.LastProgress + (long)(entry.Limit.Ticks * (_clock.TimestampFrequency / (double)TimeSpan.TicksPerSecond)))"),
    ("S5 the heartbeat fires whatever the transfer did meanwhile: no swap (R1-11)", SW, ["proxy"],
     """            if (!entry.TryFire())
                continue; // the transfer ended first: it is not the limit's""",
     """            entry.TryFire();"""),
    ("P1 reads larger than 16 KiB (R1-4 chunked)", PT, ["proxy"],
     "        var piece = new byte[PieceBytes];",
     "        var piece = new byte[PieceBytes * 4];"),
    ("P2 the answer in one write (R1-5)", PT, ["proxy"],
     "                var length = PieceLength(body.Length - offset);",
     "                var length = body.Length - offset;"),
    ("P3 a short last piece (R1-12)", PT, ["proxy"],
     "internal static int PieceLength(int remaining) => remaining < 2 * PieceBytes ? remaining : PieceBytes;",
     "internal static int PieceLength(int remaining) => remaining <= PieceBytes ? remaining : PieceBytes;"),
    ("P4 a body read the limit raced is forwarded (R1-2)", PT, ["proxy"],
     """            // The body's end and its limit settle by one move on the entry: a lost move is the limit's, whatever the read
            // returned (a chunked read cut by the abort can end as if the body were whole).
            if (reading is not null && !reading.TryEnd())
            {
                CutStalled(answer, BodyPhase);
                return;
            }""",
     """            reading?.TryEnd();"""),
    ("P5 a check in place of the body's swap (R1-11)", PT, ["proxy"],
     """            // The body's end and its limit settle by one move on the entry: a lost move is the limit's, whatever the read
            // returned (a chunked read cut by the abort can end as if the body were whole).
            if (reading is not null && !reading.TryEnd())""",
     """            if (reading is not null && reading.HasFired)"""),
    ("P6 a cut not counted (R1-1)", PT, ["proxy"],
     "            Interlocked.Increment(ref _bodyTimeouts);\n",
     ""),
    ("P7 the cut sends no 408 (R1-1, managed listener only)", PT, ["proxy"],
     "        public bool TimeOut(int? status) => TryEnd(status, readingOnly: false, closeConnection: true, TimedOut);",
     "        public bool TimeOut(int? status) => TryEnd(null, readingOnly: false, closeConnection: true, TimedOut);"),
    ("P8 the answer sent chunked (R1-5b, managed listener only)", PT, ["proxy"],
     "        response.ContentLength64 = body.Length;\n        var writing",
     "        response.SendChunked = true;\n        var writing"),
    ("P9 a timeout wins after a close began (R1-7)", PT, ["proxy"],
     "                if (phase > Answering || (readingOnly && phase != Reading))",
     "                if (phase > Closing || (readingOnly && phase != Reading))"),
    ("P10 no answer's write is watched (R1-3)", PT, ["proxy"],
     "        var writing = _watch?.Watch(AnswerPhase, _options.StallLimit, () => CutStalled(answer, AnswerPhase));",
     "        StallWatch.Entry? writing = null;"),
    ("P11 no Diagnostics() line for cut callers (R1-1, R1-3)", PT, ["proxy"],
     "        if (bodyTimeouts + answerTimeouts > 0)\n            entries.Add(",
     "        if (bodyTimeouts + answerTimeouts < 0)\n            entries.Add("),
    ("D1 the accept loop throws what a closing listener throws (the disposal fact)", PT, ["proxy"],
     "catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException or InvalidOperationException\n                || Volatile.Read(ref _disposed) == 1)",
     "catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException or InvalidOperationException)"),
    ("O1 zero accepted as a limit (R1-9)", PTO, ["proxy"],
     "if (CallerStallTimeout is { } stall && stall <= TimeSpan.Zero && stall != Timeout.InfiniteTimeSpan)",
     "if (CallerStallTimeout is { } stall && stall < TimeSpan.Zero && stall != Timeout.InfiniteTimeSpan)"),
    ("O2 another default (R1-9)", PTO, ["proxy"],
     "public TimeSpan? CallerStallTimeout { get; set; } = TimeSpan.FromSeconds(100);",
     "public TimeSpan? CallerStallTimeout { get; set; } = TimeSpan.FromSeconds(120);"),
    ("O3 the option not copied for registered taps (R1-9)", PTX, ["proxy"],
     "        to.CallerStallTimeout = from.CallerStallTimeout;\n",
     ""),
]

if __name__ == "__main__":
    runner.main()
