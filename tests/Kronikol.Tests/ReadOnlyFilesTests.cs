using System.Text.RegularExpressions;

namespace Kronikol.Tests;

/// <summary>
/// A test that makes a write fail by marking the file <see cref="FileAttributes.ReadOnly"/> depends on the
/// machine enforcing that mark. On Linux and macOS the mark clears the file's write bits, and root ignores
/// them: in a container that runs the tests as root (a Claude Code cloud session, for one) the write the test
/// needs to fail succeeds, and the test fails for a reason that is not the code's. Three tests did exactly
/// that, while CI, which does not run as root, passed them.
///
/// <para><b>Why a source scan.</b> Whether a test depends on the mark is a property of its source, and a new
/// test that marks a file read-only would pass everywhere but root without anyone seeing it. Each marking must
/// be matched by a <c>ReadOnlyFiles.AreEnforced</c> skip, so such a test skips as root and runs everywhere
/// else.</para>
/// </summary>
public class ReadOnlyFilesTests
{
    private static readonly string TestsRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tests"));

    [Fact]
    public void Every_test_that_marks_a_file_read_only_skips_where_the_mark_is_not_enforced()
    {
        var offenders = Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        // The probe marks its own scratch file to find out whether the mark holds.
                        && Path.GetFileName(f) != "ReadOnlyFiles.cs")
            .Select(f => (File: f, Code: WithoutComments(File.ReadAllText(f))))
            .Where(s => Count(s.Code, @"FileAttributes\s*\.\s*ReadOnly") > Count(s.Code, @"ReadOnlyFiles\s*\.\s*AreEnforced"))
            .Select(s => Path.GetRelativePath(TestsRoot, s.File).Replace('\\', '/'))
            .ToList();

        Assert.True(offenders.Count == 0,
            "these test sources mark a file read-only without skipping where root can still write it: "
            + string.Join(", ", offenders)
            + ". Start each such test with Assert.SkipUnless(ReadOnlyFiles.AreEnforced, ReadOnlyFiles.NotEnforcedReason).");
    }

    private static int Count(string code, string pattern) => Regex.Matches(code, pattern).Count;

    private static string WithoutComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//.*", "");
}
