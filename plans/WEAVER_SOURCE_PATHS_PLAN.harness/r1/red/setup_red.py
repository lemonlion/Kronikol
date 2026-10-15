# Sets up the red proof of WEAVER_SOURCE_PATHS_PLAN R1 on v4.20.2: the new and changed facts copied in, and stubs for
# the helpers the facts call that 4.20.2 does not have. The stubs carry no behaviour of R1.
import shutil, os

SRC = r"C:\Code\Kronikol-165"
RED = r"C:\Code\Kronikol-165-red"

tests = [
    r"tests\Kronikol.Tests.AssertionTracking\DeterministicSourcePathsTests.cs",
    r"tests\Kronikol.Tests.AssertionTracking\TargetsBuildTests.cs",
    r"tests\Kronikol.Tests.AssertionTracking\TestAssemblyBuilder.cs",
    r"tests\Kronikol.Tests.AssertionTracking\WeaveAssertionsTaskTests.cs",
    r"tests\Kronikol.Tests\Reports\ErrorDiffParserTests.cs",
    r"tests\Kronikol.Tests\Reports\FailureClusterKeyTests.cs",
    r"tests\Kronikol.Tests\Reports\FailuresDigestGeneratorTests.cs",
]
for t in tests:
    shutil.copyfile(os.path.join(SRC, t), os.path.join(RED, t))

stub = """using System;
using System.Collections.Generic;
using System.Text;

namespace Kronikol.AssertionTracking;

// Red-proof stubs (WEAVER_SOURCE_PATHS_PLAN section 5): the helpers R1 adds, with none of its behaviour.
internal static class SourceDocuments
{
    internal static List<KeyValuePair<string, string>> ParsePathMap(string? pathMap, ICollection<string> refused) => new();

    internal static string Windows1252(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    internal static Encoding? CodePageEncoding(string? value, out string? note)
    {
        note = null;
        return null;
    }
}

public sealed class UnreadSource
{
    public string Document { get; } = "";
    public IReadOnlyList<string> Tried { get; } = Array.Empty<string>();
    public int Statements { get; internal set; }
}
"""
open(os.path.join(RED, r"src\Kronikol.AssertionTracking\Stubs165.cs"), "w", encoding="utf-8", newline="\n").write(stub)

p = os.path.join(RED, r"src\Kronikol.AssertionTracking\AssertionWeaver.cs")
b = open(p, "rb").read()
old = b"""    public AssertionWeaver(TaskLoggingHelper? log = null, string[]? searchDirectories = null)
    {
        _log = log;
        _searchDirectories = searchDirectories ?? Array.Empty<string>();
    }
"""
new = old + b"""
    // Red-proof stub: the map and the code page are ignored.
    internal AssertionWeaver(TaskLoggingHelper? log, string[]? searchDirectories,
        IReadOnlyList<KeyValuePair<string, string>> sourceMap, System.Text.Encoding? codePage)
        : this(log, searchDirectories)
    {
    }
"""
assert b.count(old) == 1
b = b.replace(old, new)
old = b"""    public List<string> Unwoven { get; set; } = new List<string>();
}"""
new = b"""    public List<string> Unwoven { get; set; } = new List<string>();

    // Red-proof stub.
    public List<UnreadSource> UnreadSources { get; set; } = new List<UnreadSource>();
}"""
assert b.count(old) == 1
b = b.replace(old, new)
open(p, "wb").write(b)

p = os.path.join(RED, r"src\Kronikol.AssertionTracking\WeaveAssertionsTask.cs")
b = open(p, "rb").read()
old = b"""    public ITaskItem[] SourceRoots { get; set; } = Array.Empty<ITaskItem>();
"""
new = old + b"""
    // Red-proof stubs: never read.
    public string? PathMap { get; set; }
    public string? CodePage { get; set; }
"""
assert b.count(old) == 1
b = b.replace(old, new)
open(p, "wb").write(b)
print("red set up")
