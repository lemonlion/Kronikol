using System.Text.RegularExpressions;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The history action's <c>save</c> phase (plans/HISTORY_ACTION_PLAN.md §4.4, F8): after the tests, every fragment the
/// job's runs wrote, uploaded as one artifact of its own with the tree below the workspace kept, so <c>record</c> can
/// tell the reports directories and the attempts kept under <c>runs/</c> apart. The name is unique across the calls of
/// a reusable workflow and across re-run attempts, a reports directory in a hidden folder is uploaded, and a staged
/// rotation never is.
/// </summary>
public class HistoryActionSaveTests
{
    private static readonly Regex Name = new("^kronikol-history-(?<job>.+)-(?<attempt>[0-9]+)-(?<random>[0-9a-f]{8})$");

    private static string Under(RunnerJob job, string relative) => Path.Combine(job.Workspace, relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Save_uploads_each_fragment_under_its_reports_directory_with_kept_attempts_beside_it()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);
        HistoryFixtures.Fragment(Under(job, "tests/A/bin/Reports"), "A", "gh:7:1", "PP");
        HistoryFixtures.Fragment(Under(job, "tests/A/bin/Reports/runs/gh_7_1"), "A", "gh:7:1", "PF");
        HistoryFixtures.Fragment(Under(job, "tests/B/bin/Reports"), "B", "gh:7:1", "P");
        File.WriteAllText(Under(job, "tests/A/bin/Reports/TestRunReport.json"), "{}");

        var save = HistoryWorld.Save(job);

        Assert.True(save.Succeeded, save.ToString());
        var name = Assert.Single(job.Artifacts.Names);
        Assert.Equal(name, save.Outputs["artifact"]);
        Assert.Equal("3", save.Outputs["files"]);
        var match = Name.Match(name);
        Assert.True(match.Success, name);
        Assert.Equal("test", match.Groups["job"].Value);
        Assert.Equal("1", match.Groups["attempt"].Value);
        Assert.Equal(
            ["tests/A/bin/Reports/History.run.json", "tests/A/bin/Reports/runs/gh_7_1/History.run.json", "tests/B/bin/Reports/History.run.json"],
            job.Artifacts.Files(name));
    }

    [Fact]
    public void A_reports_directory_under_a_hidden_folder_is_uploaded()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);
        HistoryFixtures.Fragment(Under(job, ".logs/kronikol"), "A", "gh:7:1", "P");

        var save = HistoryWorld.Save(job);

        Assert.True(save.Succeeded, save.ToString());
        Assert.Equal([".logs/kronikol/History.run.json"], job.Artifacts.Files(Assert.Single(job.Artifacts.Names)));
    }

    [Fact]
    public void A_staged_rotation_is_never_uploaded()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);
        HistoryFixtures.Fragment(Under(job, "Reports"), "A", "gh:7:1", "P");
        HistoryFixtures.Fragment(Under(job, "Reports/runs/.incoming-gh_6_1"), "A", "gh:6:1", "F");

        var save = HistoryWorld.Save(job);

        Assert.True(save.Succeeded, save.ToString());
        Assert.Equal(["Reports/History.run.json"], job.Artifacts.Files(Assert.Single(job.Artifacts.Names)));
    }

    [Fact]
    public void Two_calls_with_the_same_job_and_index_upload_under_different_names()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        // Two lanes of one run that call one reusable workflow: the same job id, no matrix.
        var first = world.Job(7, job: "tests");
        var second = world.Job(7, job: "tests");
        HistoryFixtures.Fragment(Under(first, "Reports"), "A", "gh:7:1", "P");
        HistoryFixtures.Fragment(Under(second, "Reports"), "B", "gh:7:1", "P");

        Assert.True(HistoryWorld.Save(first).Succeeded);
        var save = HistoryWorld.Save(second);

        Assert.True(save.Succeeded, save.ToString());
        Assert.Equal(2, first.Artifacts.Names.Distinct().Count());
    }

    [Fact]
    public void A_rerun_attempt_uploads_beside_the_first_attempts_artifact()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var first = world.Job(7, attempt: 1);
        var rerun = world.Job(7, attempt: 2);
        HistoryFixtures.Fragment(Under(first, "Reports"), "A", "gh:7:1", "F");
        HistoryFixtures.Fragment(Under(rerun, "Reports"), "A", "gh:7:2", "P");

        Assert.True(HistoryWorld.Save(first).Succeeded);
        var save = HistoryWorld.Save(rerun);

        Assert.True(save.Succeeded, save.ToString());
        Assert.Equal(["1", "2"], first.Artifacts.Names.Select(n => Name.Match(n).Groups["attempt"].Value));
    }

    [Fact]
    public void No_fragment_is_a_warning_and_no_artifact()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);

        var save = HistoryWorld.Save(job);

        Assert.True(save.Succeeded, save.ToString());
        Assert.Equal("", save.Outputs["artifact"]);
        Assert.Equal("0", save.Outputs["files"]);
        Assert.Empty(job.Artifacts.Names);
        Assert.Contains(save.Warnings, w => w.Contains("History.run.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_runs_after_a_failed_test_step()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);
        job.Failed = true;
        HistoryFixtures.Fragment(Under(job, "Reports"), "A", "gh:7:1", "F");

        var save = HistoryWorld.Save(job);

        Assert.Equal("1", save.Outputs["files"]);
        Assert.Single(job.Artifacts.Names);
    }

    [Fact]
    public void Save_looks_only_under_the_paths_it_is_given()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld();
        var job = world.Job(7);
        HistoryFixtures.Fragment(Under(job, "unit/Reports"), "Unit", "gh:7:1", "P");
        HistoryFixtures.Fragment(Under(job, "component/Reports"), "Component", "gh:7:1", "P");
        HistoryFixtures.Fragment(Under(job, "other/Reports"), "Other", "gh:7:1", "P");

        var save = HistoryWorld.Save(job, new Dictionary<string, string> { ["path"] = "unit\ncomponent/" });

        Assert.True(save.Succeeded, save.ToString());
        Assert.Equal(["component/Reports/History.run.json", "unit/Reports/History.run.json"], job.Artifacts.Files(Assert.Single(job.Artifacts.Names)));
    }
}
