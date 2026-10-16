"""R3's mutations (plans/TAP_CALLER_TIME_LIMITS_PLAN.md section 5), run with R0's runner: each applied alone to a copy of
the release's tree, then the facts of the projects the file feeds. A mutation is killed when at least one fact fails.

    python mutations_r3.py <tree> <output file> [<id> ...]
"""
import importlib.util
import pathlib

runner_path = pathlib.Path(__file__).resolve().parent.parent / "r0" / "mutations.py"
spec = importlib.util.spec_from_file_location("runner", runner_path)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

TT = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
TO = "src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs"
SW = "src/Kronikol.Extensions.TcpTap/StallWatch.cs"

runner.PROJECTS = {
    "tcptap": ("tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj", "FullyQualifiedName~Kronikol.Tests.TcpTap"),
    "proxy": ("tests/Kronikol.Tests.ProxyTap/Kronikol.Tests.ProxyTap.csproj", "FullyQualifiedName~StallWatchTests"),
}

runner.MUTATIONS = [
    ("M1 the half-close limit armed on an idle pair (R3-3)", TT, ["tcptap"],
     "            var limits = new PairLimits(this, id, pair, client, upstream);\n",
     "            var limits = new PairLimits(this, id, pair, client, upstream);\n            limits.PumpEnded(TapDirection.ClientToServer);\n"),
    ("M2 the reaper reads the wall clock again (R3-6)", TT, ["tcptap"],
     "                return ageAtSighting + Clock.GetElapsedTime(sightedAt) >= threshold;",
     "                return Clock.GetUtcNow() - oldest >= threshold;"),
    ("M3 the half-close timed from the FIN, not the last byte (R3-5)", TT, ["tcptap"],
     "        public void Progress() => Volatile.Read(ref _halfClosed)?.Progress();",
     "        public void Progress() { }"),
    ("M4 the reaper's tick a delay of its own again (R3-8)", TT, ["tcptap"],
     "            reaper?.TryEnd();\n",
     "            reaper?.TryEnd();\n            if (reaper is not null)\n                await Task.Delay(1_000).ConfigureAwait(false);\n"),
    ("M5 the write-pending mark never cleared (R3-3)", TT, ["tcptap"],
     "                    writing?.TryEnd();\n",
     ""),
    ("M6 a write not watched (R3-4)", TT, ["tcptap"],
     "            tap._watch?.Watch(WritePhase, tap._options.StalledWriteLimit, () => Close(ByStalledWrite, direction));",
     "            null;"),
    ("M7 a close counted under the other limit (R3-1, R3-4)", TT, ["tcptap"],
     "            if (limit == ByHalfClose)",
     "            if (limit != ByHalfClose)"),
    ("M8 a limit that closes nothing (R3-1, R3-4)", TT, ["tcptap"],
     """            try
            {
                pair.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The pair has already ended.
            }

            try { client.Dispose(); } catch { /* already closing */ }
            try { upstream.Dispose(); } catch { /* already closing */ }
        }
    }""",
     """        }
    }"""),
    ("M9 the reaper stands down under a debugger (the reaper's debugger fact)", TT, ["tcptap"],
     "        }, evenUnderDebugger: true);",
     "        }, evenUnderDebugger: false);"),
    ("M10 a segment stamped by the machine's clock, not the tap's (R3-6)", TT, ["tcptap"],
     "new TapSegment(direction, copy, Clock.GetUtcNow())",
     "new TapSegment(direction, copy, DateTimeOffset.UtcNow)"),
    ("M11 every entry acts under a debugger (debugger facts, W-1)", SW, ["tcptap", "proxy"],
     "            if (!entry.IsActive || (debugger && !entry.EvenUnderDebugger) || !IsDue(entry))",
     "            if (!entry.IsActive || !IsDue(entry))"),
    ("M12 a condition entry is always due (R3-6, W-1)", SW, ["tcptap", "proxy"],
     "            return due();",
     "            return due() || true;"),
    ("M13 a throwing check leaves its entry watched (W-1)", SW, ["proxy"],
     "            if (entry.TryEnd())",
     "            if (!entry.IsActive)"),
    ("M14 no entry acts under a debugger, the reaper's included (W-1, the reaper's debugger fact)", SW, ["tcptap", "proxy"],
     "            if (!entry.IsActive || (debugger && !entry.EvenUnderDebugger) || !IsDue(entry))",
     "            if (!entry.IsActive || debugger || !IsDue(entry))"),
    ("O1 a half-close limit of zero accepted", TO, ["tcptap"],
     "if (HalfClosedTimeout is { } halfClosed && halfClosed <= TimeSpan.Zero",
     "if (HalfClosedTimeout is { } halfClosed && halfClosed < TimeSpan.Zero"),
    ("O2 a stalled-write limit of zero accepted", TO, ["tcptap"],
     "if (StalledWriteTimeout is { } stalledWrite && stalledWrite <= TimeSpan.Zero",
     "if (StalledWriteTimeout is { } stalledWrite && stalledWrite < TimeSpan.Zero"),
    ("O3 another half-close default", TO, ["tcptap"],
     "public TimeSpan? HalfClosedTimeout { get; set; } = TimeSpan.FromSeconds(100);",
     "public TimeSpan? HalfClosedTimeout { get; set; } = TimeSpan.FromSeconds(120);"),
    ("O4 another stalled-write default", TO, ["tcptap"],
     "public TimeSpan? StalledWriteTimeout { get; set; } = TimeSpan.FromSeconds(100);",
     "public TimeSpan? StalledWriteTimeout { get; set; } = TimeSpan.FromSeconds(120);"),
    ("O5 the half-close limit not copied (R3-7)", TO, ["tcptap"],
     "        target.HalfClosedTimeout = HalfClosedTimeout;\n",
     ""),
    ("O6 the stalled-write limit not copied (R3-7)", TO, ["tcptap"],
     "        target.StalledWriteTimeout = StalledWriteTimeout;\n",
     ""),
]

if __name__ == "__main__":
    runner.main()
