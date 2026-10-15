"""R1's seams, stubbed onto 4.22.2 so R1's facts compile there (plans/TAP_CALLER_TIME_LIMITS_PLAN.md section 5): the
option with no default and no check, a counter that stays 0, the clock and debugger seams, no watch, one piece for any
answer, and a CallerAnswer that never times out. StallWatch.cs is linked for its types only; nothing uses it.

    python red1_stubs.py <tree at 4.22.2> <R1 tree>
"""
import pathlib
import shutil
import sys

red = pathlib.Path(sys.argv[1])
r1 = pathlib.Path(sys.argv[2])


def edit(rel, pairs):
    p = red / rel
    s = p.read_bytes().decode("utf-8")
    for old, new in pairs:
        assert s.count(old) == 1, (rel, old[:70], s.count(old))
        s = s.replace(old, new)
    p.write_bytes(s.encode("utf-8"))


shutil.copy(r1 / "src/Kronikol.Extensions.TcpTap/StallWatch.cs", red / "src/Kronikol.Extensions.TcpTap/StallWatch.cs")
edit("src/Kronikol.Extensions.ProxyTap/Kronikol.Extensions.ProxyTap.csproj", [
    ("    <PackageTags>$(PackageTags);proxy;tee;e2e;playwright;polyglot</PackageTags>\n",
     "    <PackageTags>$(PackageTags);proxy;tee;e2e;playwright;polyglot</PackageTags>\n"
     "    <DefineConstants>$(DefineConstants);KRONIKOL_PROXYTAP_SHARED</DefineConstants>\n"),
    ("  <ItemGroup>\n    <InternalsVisibleTo Include=\"Kronikol.Tests.ProxyTap\" />\n",
     "  <ItemGroup>\n    <Compile Include=\"..\\Kronikol.Extensions.TcpTap\\StallWatch.cs\" Link=\"Shared\\StallWatch.cs\" />\n"
     "    <InternalsVisibleTo Include=\"Kronikol.Tests.ProxyTap\" />\n"),
])
edit("src/Kronikol.Extensions.ProxyTap/ProxyTapOptions.cs", [
    ("    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);\n",
     "    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);\n\n"
     "    /// <summary>Stub (red proof): read by nothing.</summary>\n"
     "    public TimeSpan? CallerStallTimeout { get; set; }\n"),
])
edit("src/Kronikol.Extensions.ProxyTap/ProxyTap.cs", [
    ("    internal Func<Stream, Stream>? WrapRequestBody { get; set; }\n",
     "    internal Func<Stream, Stream>? WrapRequestBody { get; set; }\n\n"
     "    // Stubs (red proof): R1's seams, read by nothing.\n"
     "    public long CallerTimeouts => 0;\n"
     "    internal const string BodyPhase = \"request body\";\n"
     "    internal const string AnswerPhase = \"answer\";\n"
     "    internal const int PieceBytes = 16 * 1024;\n"
     "    internal TimeProvider Clock { get; set; } = TimeProvider.System;\n"
     "    internal Func<bool> DebuggerAttached { get; set; } = static () => Debugger.IsAttached;\n"
     "    internal StallWatch? Watch => null;\n"
     "    internal static int PieceLength(int remaining) => remaining;\n"),
    ("        /// <summary>Whether neither a close nor an end has begun.</summary>\n",
     "        public bool IsTimedOut => false;\n\n"
     "        public bool TimeOut(int? status) => false;\n\n"
     "        /// <summary>Whether neither a close nor an end has begun.</summary>\n"),
    ("        public void Close()\n        {\n            var phase = Volatile.Read(ref _state);\n"
     "            if (phase > Answering || Interlocked.CompareExchange(ref _state, Closing, phase) != phase)\n                return;\n"
     "            try\n            {\n                Response.Close();\n                Volatile.Write(ref _state, Closed);\n            }\n",
     "        public bool Close()\n        {\n            var phase = Volatile.Read(ref _state);\n"
     "            if (phase > Answering || Interlocked.CompareExchange(ref _state, Closing, phase) != phase)\n                return false;\n"
     "            try\n            {\n                Response.Close();\n                Volatile.Write(ref _state, Closed);\n                return true;\n            }\n"),
    ("        public void EndUnclosed() => TryEnd(", "        public bool EndUnclosed() => TryEnd("),
])
for rel in ("tests/Kronikol.Tests.ProxyTap/ProxyTapStallTests.cs", "tests/Kronikol.Tests.ProxyTap/ProxyTapRegistrationTests.cs",
            "tests/Kronikol.Tests.ProxyTap/ProxyTapHangUpTests.cs", "tests/Kronikol.Tests.ProxyTap/Kronikol.Tests.ProxyTap.csproj"):
    shutil.copy(r1 / rel, red / rel)
print("stubbed")
