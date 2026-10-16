"""R2's mutations (plans/TAP_CALLER_TIME_LIMITS_PLAN.md section 5), run with R0's runner: each applied alone to a copy of
the release's tree, then the facts of the project the file feeds. A mutation is killed when at least one fact fails.

    python mutations_r2.py <tree> <output file> [<id> ...]
"""
import importlib.util
import pathlib

runner_path = pathlib.Path(__file__).resolve().parent.parent / "r0" / "mutations.py"
spec = importlib.util.spec_from_file_location("runner", runner_path)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

OT = "src/Kronikol.Extensions.Otlp/OtlpTap.cs"
OTO = "src/Kronikol.Extensions.Otlp/OtlpTapOptions.cs"
OTX = "src/Kronikol.Extensions.Otlp/OtlpTapServiceCollectionExtensions.cs"

runner.PROJECTS = {
    "otlp": ("tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj", "FullyQualifiedName~OtlpTap"),
}

runner.MUTATIONS = [
    ("K1 a keep-alive close counted (R2-3, R2-4)", OT, ["otlp"],
     "                return; // kept alive past KeepAliveTimeout: the using closes it",
     "                Interlocked.Increment(ref _sendTimeouts);\n                return;"),
    ("K2 the keep-alive wait timed by the stall limit (R2-3, R2-5)", OT, ["otlp"],
     "CutPhase.KeptAlive => (KeptAlivePhase, options.KeepAliveLimit),",
     "CutPhase.KeptAlive => (KeptAlivePhase, options.StallLimit),"),
    ("K3 no keep-alive limit (R2-3, R2-4)", OT, ["otlp"],
     "CutPhase.KeptAlive => (KeptAlivePhase, options.KeepAliveLimit),",
     "CutPhase.KeptAlive => (KeptAlivePhase, (TimeSpan?)null),"),
    ("H1 a head's first byte not marked: the head is the keep-alive wait's (R2-1)", OT, ["otlp"],
     "                    HeadStarted = () => limits.Next(CutPhase.Sending),\n",
     ""),
    ("H2 reads record no progress: a total deadline (R2-5)", OT, ["otlp"],
     "                    Progressed = limits.Progress,\n",
     ""),
    ("B1 a body that ends after its limit fired is received (the after-the-limit theory)", OT, ["otlp"],
     "                        if (!limits.End())\n                            break;\n\n                        if (!await HandleAsync(",
     "                        limits.End();\n\n                        if (!await HandleAsync("),
    ("B2 no 408 for an exporter cut while sending (R2-1, R2-2)", OT, ["otlp"],
     "                    await WriteResponseAsync(stream, limits: null, 408,",
     "                    if (phase == CutPhase.None) await WriteResponseAsync(stream, limits: null, 408,"),
    ("B3 the 408 keeps the connection alive (R2-1)", OT, ["otlp"],
     "keepAlive: false, bound.Token)",
     "keepAlive: true, bound.Token)"),
    ("B4 a stall while sending counted as a rejected payload (R2-9)", OT, ["otlp"],
     "                Interlocked.Increment(ref _sendTimeouts);\n",
     "                Interlocked.Increment(ref _sendTimeouts);\n                Interlocked.Increment(ref _rejected);\n"),
    ("C1 the limit records its cut but ends no read (R2-1, R2-2)", OT, ["otlp"],
     "                connection.Cancel();",
     "                _ = connection;"),
    ("F1 the forward watched as the export (R2-8)", OT, ["otlp"],
     "                        if (!limits.End())\n                            break;\n\n                        if (!await HandleAsync(",
     "                        if (!await HandleAsync("),
    ("A1 the relayed answer in one write (R2-10)", OT, ["otlp"],
     """        for (var offset = 0; offset < body.Length; offset += AnswerPiece)
        {
            await stream.WriteAsync(body.AsMemory(offset, Math.Min(AnswerPiece, body.Length - offset)), ct).ConfigureAwait(false);
            limits?.Progress();
        }""",
     """        if (body.Length > 0)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
        limits?.Progress();"""),
    ("A2 the answer not watched (R2-10)", OT, ["otlp"],
     "        limits?.Next(CutPhase.Answering);\n",
     ""),
    ("A3 an answer stall counted as a sending one (R2-10)", OT, ["otlp"],
     "                Interlocked.Increment(ref _answerTimeouts);",
     "                Interlocked.Increment(ref _sendTimeouts);"),
    ("A4 an answer that ends after its limit fired is not cut (the after-the-limit theory)", OT, ["otlp"],
     "        if (limits is not null && !limits.End())\n            throw new OperationCanceledException(ct);",
     "        limits?.End();"),
    ("E1 an entry left behind when its connection ends (leave-no-entry theory)", OT, ["otlp"],
     "            limits.End(); // no watch outlives its connection\n",
     ""),
    ("D1 limits fire under a debugger (debugger fact)", OT, ["otlp"],
     "_watch = new StallWatch(Clock, DebuggerAttached,",
     "_watch = new StallWatch(Clock, static () => false,"),
    ("O1 a stall limit of zero accepted", OTO, ["otlp"],
     "if (CallerStallTimeout is { } stall && stall <= TimeSpan.Zero",
     "if (CallerStallTimeout is { } stall && stall < TimeSpan.Zero"),
    ("O2 a keep-alive limit of zero accepted", OTO, ["otlp"],
     "if (KeepAliveTimeout is { } idle && idle <= TimeSpan.Zero",
     "if (KeepAliveTimeout is { } idle && idle < TimeSpan.Zero"),
    ("O3 another stall default", OTO, ["otlp"],
     "public TimeSpan? CallerStallTimeout { get; set; } = TimeSpan.FromSeconds(100);",
     "public TimeSpan? CallerStallTimeout { get; set; } = TimeSpan.FromSeconds(120);"),
    ("O4 another keep-alive default", OTO, ["otlp"],
     "public TimeSpan? KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(120);",
     "public TimeSpan? KeepAliveTimeout { get; set; } = TimeSpan.FromSeconds(130);"),
    ("O5 the stall limit not copied for registered taps (R2-7)", OTX, ["otlp"],
     "        to.CallerStallTimeout = from.CallerStallTimeout;\n",
     ""),
    ("O6 the keep-alive limit not copied for registered taps (R2-7)", OTX, ["otlp"],
     "        to.KeepAliveTimeout = from.KeepAliveTimeout;\n",
     ""),
]

if __name__ == "__main__":
    runner.main()
