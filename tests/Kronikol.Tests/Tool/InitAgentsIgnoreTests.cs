using System.Text.RegularExpressions;
using Kronikol.Tool;

namespace Kronikol.Tests.Tool;

/// <summary>
/// A report is written under the test project's build output, and <c>bin/</c> is gitignored in every .NET
/// repository. So `rg` — and every tool built on the same ignore rules, which is most of them — walks
/// straight past <c>Failures.md</c> and the <c>CLAUDE.md</c> beside it. An agent told "search the repo for
/// why the tests failed" finds nothing, and the absence is indistinguishable from there being nothing to
/// find.
///
/// <para><c>.ignore</c> is ripgrep's own file and outranks <c>.gitignore</c>, so it can re-include what
/// <c>.gitignore</c> excluded. Git never reads it, so nothing becomes committable that was not before —
/// which is the property that makes this safe to write into somebody's repository.</para>
///
/// <para>The rules have to re-include the <b>directories</b> before their contents: an ignore rule cannot
/// re-include a file whose parent directory is still excluded, so <c>!bin/**/Reports/**</c> on its own
/// does nothing. That is the part that is easy to get wrong and impossible to notice.</para>
/// </summary>
public class InitAgentsIgnoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-ignore-" + Guid.NewGuid().ToString("N"));

    public InitAgentsIgnoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Install()
    {
        File.WriteAllText(Path.Combine(_dir, ".gitignore"), "[Bb]in/\nobj/\n");
        var exit = InitAgentsCommand.Run([_dir], new StringWriter(), new StringWriter());
        Assert.Equal(0, exit);
        return File.ReadAllText(Path.Combine(_dir, ".ignore"));
    }

    [Fact]
    public void It_writes_an_ignore_file_that_reaches_a_report_under_bin()
    {
        var ignore = Install();

        // The directory rules, then the contents. Without the first, the second matches nothing.
        Assert.Contains("!bin/", ignore, StringComparison.Ordinal);
        Assert.Contains("!bin/**/Reports/", ignore, StringComparison.Ordinal);
        Assert.Contains("!bin/**/Reports/**", ignore, StringComparison.Ordinal);

        // The order matters as much as the presence.
        var dirRule = ignore.IndexOf("!bin/**/Reports/\n", StringComparison.Ordinal) >= 0
            ? ignore.IndexOf("!bin/**/Reports/\n", StringComparison.Ordinal)
            : ignore.IndexOf("!bin/**/Reports/\r\n", StringComparison.Ordinal);
        Assert.True(dirRule >= 0, "the directory itself is never re-included:\n" + ignore);
        Assert.True(ignore.IndexOf("!bin/", StringComparison.Ordinal) < dirRule,
            "bin/ is re-included after its subdirectory, which cannot work");
    }

    [Fact]
    public void It_is_a_managed_block_so_a_repositorys_own_rules_survive()
    {
        File.WriteAllText(Path.Combine(_dir, ".ignore"), "# ours\n!vendor/generated/**\n");

        var ignore = Install();

        Assert.Contains("!vendor/generated/**", ignore, StringComparison.Ordinal);
        Assert.Contains("# kronikol:begin", ignore, StringComparison.Ordinal);
        Assert.Contains("# kronikol:end", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Re_running_replaces_the_block_rather_than_stacking_copies()
    {
        Install();
        var ignore = Install();

        Assert.Equal(1, Regex.Matches(ignore, Regex.Escape("# kronikol:begin")).Count);
    }

    [Fact]
    public void It_leaves_git_alone()
    {
        // The whole reason this is safe to write. If it changed what git tracks, installing it would start
        // committing build output, which is a far worse outcome than an unfindable report.
        Install();

        Assert.False(File.ReadAllText(Path.Combine(_dir, ".gitignore")).Contains("Reports", StringComparison.Ordinal));
    }
}
