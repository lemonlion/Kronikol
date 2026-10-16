"""R3's seams stubbed onto 4.25.0, so R3's facts compile there (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §5: "red before the fix"
means on the previous release with only the seams added; R3-6 reads the pump's stamp and the reaper's UtcNow through the
seam's GetUtcNow).

    python red3_stubs.py <4.25.0 tree> <R3 tree>

The options with no default, no check and no copy; counters that stay 0; the clock seam (fed to the accept loop's clock),
read for a segment's time and the reaper's wall clock; the debugger seam; no watch; the phase names. The test project,
R3's facts and the stop-line fact R3 updates come from the R3 tree; the watch's own facts (StallWatchTests) do not, since
they test R1's watch and R3's additions to it, a new API.
"""
import pathlib
import shutil
import sys

red, r3 = (pathlib.Path(each) for each in sys.argv[1:3])


def edit(relative, old, new):
    p = red / relative
    s = p.read_bytes().decode("utf-8")
    assert s.count(old) == 1, (relative, old[:80], s.count(old))
    p.write_bytes(s.replace(old, new).encode("utf-8"))


TAP = "src/Kronikol.Extensions.TcpTap/TcpTap.cs"
edit(TAP,
     "    public long StuckConnectionsReaped => Interlocked.Read(ref _stuckConnectionsReaped);\n",
     """    public long StuckConnectionsReaped => Interlocked.Read(ref _stuckConnectionsReaped);

    // R3's seams, stubbed (plans/TAP_CALLER_TIME_LIMITS_PLAN.harness/r3/).
    public long HalfClosedPairsClosed => 0;

    public long StalledWritesClosed => 0;

    internal TimeProvider Clock
    {
        get => Accepting.Clock;
        set => Accepting.Clock = value;
    }

    internal Func<bool> DebuggerAttached { get; set; } = static () => false;

    internal StallWatch? Watch => null;

    internal const string HalfClosedPhase = "half-closed pair";
    internal const string WritePhase = "write";
    internal const string ReaperPhase = "unanswered command";
""")
edit(TAP, "new TapSegment(direction, copy, DateTimeOffset.UtcNow)", "new TapSegment(direction, copy, Clock.GetUtcNow())")
edit(TAP, "if (since is not { } oldest || DateTimeOffset.UtcNow - oldest < threshold)",
     "if (since is not { } oldest || Clock.GetUtcNow() - oldest < threshold)")

edit("src/Kronikol.Extensions.TcpTap/TcpTapOptions.cs",
     "    public TimeSpan? ReapStuckConnectionsAfter { get; set; }\n",
     "    public TimeSpan? ReapStuckConnectionsAfter { get; set; }\n\n"
     "    // R3's options, stubbed: no default, no check and no copy.\n"
     "    public TimeSpan? HalfClosedTimeout { get; set; }\n\n"
     "    public TimeSpan? StalledWriteTimeout { get; set; }\n")

for relative in (
        "tests/Kronikol.Tests.TcpTap/Kronikol.Tests.TcpTap.csproj",
        "tests/Kronikol.Tests.TcpTap/TcpTapStallTests.cs",
        "tests/Kronikol.Tests.TcpTap/TcpTapConnectFailureTests.cs"):
    shutil.copyfile(r3 / relative, red / relative)
print("ok")
