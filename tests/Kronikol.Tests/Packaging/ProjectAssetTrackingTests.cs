using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Kronikol.Tests.Packaging;

/// <summary>
/// Every file a project names by literal path has to be in the repository, not merely on the machine that
/// wrote it.
///
/// <para><b>Why this exists.</b> <c>Kronikol.Tool.csproj</c> grew
/// <c>&lt;EmbeddedResource Include="..\..\templates\agents\CLAUDE.md" /&gt;</c> while
/// <c>templates/agents/</c> was untracked. Locally that builds; on a fresh clone - which is what CI is -
/// it is <b>CS1566</b>, a hard compile error before a single test runs. Nothing in the repository could
/// see it, because every check ran on the machine that had the file.</para>
///
/// <para><b>Why existence is not the assertion.</b> <c>File.Exists</c> is green on exactly the machine
/// where the bug is invisible. The property that matters is <i>tracked</i>, so the question is put to
/// git. A path that is present but ignored is the failure this catches.</para>
///
/// <para>Wildcards are checked differently and deliberately more weakly: an <c>Include</c> containing
/// <c>*</c> that matches nothing is not a compile error - it silently packs nothing, which is how the
/// template pack's agent assets could have gone missing without a build failure. So a wildcard must match
/// at least one tracked file; which files it matches is not this fact's business.</para>
/// </summary>
public class ProjectAssetTrackingTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>The item types whose <c>Include</c> is a real file reference rather than a package or project.</summary>
    private static readonly string[] FileItemTypes = ["EmbeddedResource", "Content", "None", "Compile"];

    public static TheoryData<string> PackableProjects()
    {
        var data = new TheoryData<string>();
        foreach (var project in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(RepoRoot, project).Replace('\\', '/'));
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PackableProjects))]
    public void Every_file_a_project_names_by_literal_path_is_tracked(string relativeProject)
    {
        Assert.SkipUnless(GitCanAnswer.Value,
            "git cannot read this working tree (no git on PATH, or not a checkout), so tracking is unanswerable here");

        var projectPath = Path.Combine(RepoRoot, relativeProject.Replace('/', Path.DirectorySeparatorChar));
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath);

        var includes = document.Descendants()
            .Where(e => FileItemTypes.Contains(e.Name.LocalName, StringComparer.Ordinal))
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            // An Include can be a semicolon-separated list.
            .SelectMany(v => v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            // MSBuild properties resolve at build time; this fact cannot evaluate them and says so by skipping.
            .Where(v => !v.Contains("$(", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var untracked = new List<string>();
        var emptyWildcards = new List<string>();

        foreach (var include in includes)
        {
            var normalised = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

            if (include.Contains('*', StringComparison.Ordinal))
            {
                if (!MatchesAnyTrackedFile(projectDir, normalised)) emptyWildcards.Add(include);
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(projectDir, normalised));
            if (!IsTracked(full)) untracked.Add(include);
        }

        Assert.True(untracked.Count == 0,
            $"{relativeProject} names files by literal path that git does not track - a fresh clone cannot build it: " +
            string.Join(", ", untracked));

        Assert.True(emptyWildcards.Count == 0,
            $"{relativeProject} has wildcard includes matching no tracked file - they pack nothing, silently: " +
            string.Join(", ", emptyWildcards));
    }

    private static bool MatchesAnyTrackedFile(string projectDir, string pattern)
    {
        // Only the last segment may be a wildcard in the patterns this repo uses; anything more exotic is
        // handed to the directory walker below and answered honestly.
        var directory = Path.GetDirectoryName(Path.Combine(projectDir, pattern))!;
        var mask = Path.GetFileName(pattern);
        if (!Directory.Exists(directory)) return false;

        var option = pattern.Contains("**", StringComparison.Ordinal)
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        // "**\*" leaves a literal "**" in the directory portion; strip it back to the real root.
        directory = directory.Replace($"{Path.DirectorySeparatorChar}**", string.Empty);
        if (!Directory.Exists(directory)) return false;

        return Directory.EnumerateFiles(directory, mask.Length == 0 ? "*" : mask, option).Any(IsTracked);
    }

    /// <summary>
    /// Whether git can answer at all here, established against a file that is certainly tracked.
    ///
    /// <para>Without this the fact fails for the wrong reason in exactly the environment it was written
    /// for. A CI runner checks the repository out as one user and runs the build as another, and git then
    /// refuses every command in that directory with <c>detected dubious ownership</c> — a non-zero exit
    /// that this check would otherwise read as "none of these files is tracked" and report as forty
    /// missing assets. <c>-c safe.directory</c> below removes the usual cause; this removes the class.</para>
    /// </summary>
    private static readonly Lazy<bool> GitCanAnswer = new(() =>
        RunGit($"ls-files --error-unmatch \"{Path.Combine(RepoRoot, "Directory.Build.props")}\"") == 0);

    private static bool IsTracked(string fullPath)
    {
        if (!File.Exists(fullPath)) return false;
        return RunGit($"ls-files --error-unmatch \"{fullPath}\"") == 0;
    }

    /// <summary>The exit code, or -1 when git could not be started at all.</summary>
    private static int RunGit(string arguments)
    {
        // -c safe.directory: the checkout and the build often run as different users on CI, and git
        // refuses to touch a repository it thinks belongs to someone else.
        var start = new ProcessStartInfo("git", $"-c safe.directory=\"{RepoRoot}\" {arguments}")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(start);
            if (process is null) return -1;
            process.WaitForExit(20_000);
            return process.ExitCode;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return -1;
        }
    }

    /// <summary>
    /// The mutation that proves the theory above can fail, kept as a fact of its own so the machinery is
    /// exercised against a path that really is untracked rather than only against paths that are fine.
    /// </summary>
    [Fact]
    public void The_tracking_check_can_tell_a_tracked_file_from_an_untracked_one()
    {
        Assert.SkipUnless(GitCanAnswer.Value, "git cannot read this working tree");

        var tracked = Path.Combine(RepoRoot, "src", "Kronikol.Tool", "Kronikol.Tool.csproj");
        Assert.True(IsTracked(tracked), $"{tracked} should be tracked");

        var untracked = Path.Combine(RepoRoot, "obj", $"tracking-probe-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(untracked)!);
        File.WriteAllText(untracked, "probe");
        try
        {
            Assert.False(IsTracked(untracked), $"{untracked} exists but is not tracked, and must read as untracked");
        }
        finally
        {
            File.Delete(untracked);
        }
    }
}
