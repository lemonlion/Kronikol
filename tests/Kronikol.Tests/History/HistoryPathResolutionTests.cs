using Kronikol.History;

namespace Kronikol.Tests.History;

/// <summary>
/// Where the ledger lives (plans/CROSS_RUN_HISTORY_PLAN.md §3.2, §6.6): an explicit option, then the
/// <c>KRONIKOL_HISTORY</c> variable, then the nearest repository above the test output or the reports
/// directory. The order matters because the test run that resolves it is often Kronikol's own, whose
/// output sits inside this repository - so <c>off</c> has to be a sentinel and the walk has to be pinned.
/// </summary>
public class HistoryPathResolutionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("kronikol-resolve").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string Sub(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string? NoEnv(string _) => null;

    [Fact]
    public void An_explicit_option_wins_and_is_taken_as_given()
    {
        var explicitPath = Path.Combine(_root, "elsewhere", "ledger.jsonl");
        var location = HistoryPathResolver.Resolve(explicitPath, Sub("reports"), _ => "/should/not/be/used", Sub("bin"));

        Assert.Equal(HistoryLocationSource.Option, location.Source);
        Assert.Equal(explicitPath, location.Path);
    }

    [Fact]
    public void A_relative_option_is_resolved_against_the_repository_root_when_there_is_one()
    {
        Sub(".git");
        var bin = Sub("tests", "X", "bin", "Debug", "net10.0");

        var location = HistoryPathResolver.Resolve("history/ledger.jsonl", Sub("reports"), NoEnv, bin);

        Assert.Equal(Path.Combine(_root, "history", "ledger.jsonl"), location.Path);
    }

    [Fact]
    public void The_environment_variable_is_next_and_off_disables_history()
    {
        var envPath = Path.Combine(_root, "ci", "history.jsonl");
        var fromEnv = HistoryPathResolver.Resolve(null, Sub("reports"), name => name == "KRONIKOL_HISTORY" ? envPath : null, Sub("bin"));
        var off = HistoryPathResolver.Resolve(null, Sub("reports"), name => name == "KRONIKOL_HISTORY" ? "off" : null, Sub("bin"));
        var offCased = HistoryPathResolver.Resolve(null, Sub("reports"), name => name == "KRONIKOL_HISTORY" ? " OFF " : null, Sub("bin"));

        Assert.Equal(HistoryLocationSource.Environment, fromEnv.Source);
        Assert.Equal(envPath, fromEnv.Path);
        Assert.Equal(HistoryLocationSource.Disabled, off.Source);
        Assert.Null(off.Path);
        Assert.Equal(HistoryLocationSource.Disabled, offCased.Source);
    }

    [Fact]
    public void The_option_beats_the_environment_variable()
    {
        var explicitPath = Path.Combine(_root, "opt.jsonl");
        var location = HistoryPathResolver.Resolve(explicitPath, Sub("reports"), name => "off", Sub("bin"));

        Assert.Equal(HistoryLocationSource.Option, location.Source);
    }

    [Fact]
    public void The_repository_above_the_test_output_is_found_by_its_git_directory()
    {
        // The usual shape: tests/X/bin/Debug/net10.0 four levels under the root, and the reports
        // directory under that. The ledger goes beside .git, not beside the binary.
        Sub(".git");
        var bin = Sub("tests", "X", "bin", "Debug", "net10.0");
        var reports = Sub("tests", "X", "bin", "Debug", "net10.0", "Reports");

        var location = HistoryPathResolver.Resolve(null, reports, NoEnv, bin);

        Assert.Equal(HistoryLocationSource.Repository, location.Source);
        Assert.Equal(Path.Combine(_root, ".kronikol", "history.jsonl"), location.Path);
    }

    [Fact]
    public void A_git_worktree_whose_dot_git_is_a_file_counts_as_a_repository()
    {
        // `git worktree add` writes a .git FILE pointing at the main repository - which is exactly how the
        // dogfood job checks out the data branch.
        File.WriteAllText(Path.Combine(_root, ".git"), "gitdir: /somewhere/.git/worktrees/x\n");
        var bin = Sub("out");

        var location = HistoryPathResolver.Resolve(null, bin, NoEnv, bin);

        Assert.Equal(HistoryLocationSource.Repository, location.Source);
        Assert.Equal(Path.Combine(_root, ".kronikol", "history.jsonl"), location.Path);
    }

    [Fact]
    public void An_existing_kronikol_directory_is_found_even_without_git()
    {
        // `kronikol history init` outside a repository, or a monorepo project that keeps its own ledger.
        Sub("project", ".kronikol");
        var bin = Sub("project", "bin", "Debug", "net10.0");

        var location = HistoryPathResolver.Resolve(null, bin, NoEnv, bin);

        Assert.Equal(HistoryLocationSource.KronikolDirectory, location.Source);
        Assert.Equal(Path.Combine(_root, "project", ".kronikol", "history.jsonl"), location.Path);
    }

    [Fact]
    public void The_nearest_marker_wins_over_a_farther_one()
    {
        // A project-level .kronikol under a repository-level .git: the explicit, closer marker is the
        // one somebody created on purpose.
        Sub(".git");
        Sub("project", ".kronikol");
        var bin = Sub("project", "bin");

        var location = HistoryPathResolver.Resolve(null, bin, NoEnv, bin);

        Assert.Equal(Path.Combine(_root, "project", ".kronikol", "history.jsonl"), location.Path);
    }

    [Fact]
    public void The_reports_directory_is_walked_when_the_test_output_is_outside_any_repository()
    {
        // A published test binary run from a temp directory, writing its reports into a checkout.
        var bin = Sub("published");
        Sub("checkout", ".git");
        var reports = Sub("checkout", "artifacts", "reports");

        var location = HistoryPathResolver.Resolve(null, reports, NoEnv, bin);

        Assert.Equal(Path.Combine(_root, "checkout", ".kronikol", "history.jsonl"), location.Path);
    }

    [Fact]
    public void Nothing_found_says_every_place_it_looked_and_how_to_fix_it()
    {
        var bin = Sub("a", "bin");
        var reports = Sub("b", "reports");

        var location = HistoryPathResolver.Resolve(null, reports, NoEnv, bin);

        Assert.Equal(HistoryLocationSource.None, location.Source);
        Assert.Null(location.Path);
        Assert.Contains(bin, location.Message);
        Assert.Contains(reports, location.Message);
        Assert.Contains("git init", location.Message);
        Assert.Contains("kronikol history init", location.Message);
        Assert.Contains("HistoryFilePath", location.Message);
        Assert.Contains("KRONIKOL_HISTORY", location.Message);
    }

    [Fact]
    public void The_walk_stops_at_the_drive_root_without_throwing()
    {
        var root = Path.GetPathRoot(_root)!;
        var location = HistoryPathResolver.Resolve(null, root, NoEnv, root);

        // Whatever the machine has at its root, the call returns rather than throws.
        Assert.True(location.Source is HistoryLocationSource.None or HistoryLocationSource.Repository or HistoryLocationSource.KronikolDirectory);
    }
}
