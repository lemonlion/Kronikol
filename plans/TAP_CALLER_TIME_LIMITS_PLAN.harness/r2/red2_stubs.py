"""R2's seams stubbed onto 4.23.0, so R2's facts compile there (plans/TAP_CALLER_TIME_LIMITS_PLAN.md §5: "red before the fix"
means on the previous release with only the seams added).

    python red2_stubs.py <4.23.0 tree> <R2 tree>

The options with no default and no check, a CallerTimeouts that stays 0, the clock seam (fed to the accept loop's clock,
the one the tap reads today), the debugger seam, no watch, the phase names; StallWatch.cs linked for its types alone.
The test project, RawExporter, the shared HeldCollector and the new facts come from the R2 tree as they are.
"""
import pathlib
import shutil
import sys

red, r2 = (pathlib.Path(each) for each in sys.argv[1:3])


def edit(relative, old, new):
    p = red / relative
    s = p.read_bytes().decode("utf-8")
    assert s.count(old) == 1, (relative, old[:80], s.count(old))
    p.write_bytes(s.replace(old, new).encode("utf-8"))


edit("src/Kronikol.Extensions.Otlp/OtlpTap.cs",
     "    public long SpansIgnored => Interlocked.Read(ref _spansIgnored);\n",
     """    public long SpansIgnored => Interlocked.Read(ref _spansIgnored);

    // R2's seams, stubbed (plans/TAP_CALLER_TIME_LIMITS_PLAN.harness/r2/).
    public long CallerTimeouts => 0;

    internal TimeProvider Clock
    {
        get => Accepting.Clock;
        set => Accepting.Clock = value;
    }

    internal Func<bool> DebuggerAttached { get; set; } = static () => false;

    internal StallWatch? Watch => null;

    internal const string KeptAlivePhase = "kept-alive connection";
    internal const string SendingPhase = "export";
    internal const string AnsweringPhase = "answer";
""")

edit("src/Kronikol.Extensions.Otlp/OtlpTapOptions.cs",
     "    public int MaxRequestBytes { get; set; } = 32 * 1024 * 1024;\n",
     "    public int MaxRequestBytes { get; set; } = 32 * 1024 * 1024;\n\n"
     "    // R2's options, stubbed: no default and no check.\n"
     "    public TimeSpan? CallerStallTimeout { get; set; }\n\n"
     "    public TimeSpan? KeepAliveTimeout { get; set; }\n")

for relative in (
        "src/Kronikol.Extensions.Otlp/Kronikol.Extensions.Otlp.csproj",
        "tests/Kronikol.Tests.Otlp/Kronikol.Tests.Otlp.csproj",
        "tests/Kronikol.Tests.Otlp/RawExporter.cs",
        "tests/Kronikol.Tests.Otlp/OtlpTapHangUpTests.cs",
        "tests/Kronikol.Tests.Otlp/OtlpTapStallTests.cs"):
    shutil.copyfile(r2 / relative, red / relative)
print("ok")
