using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Templates;

/// <summary>
/// The PR report link template, run for real. The script is read out of
/// <c>templates/github-actions/kronikol-pr-report-link/action.yml</c> and executed under node against a GitHub
/// held in memory: one pull request's comments, and each workflow run's artifacts.
///
/// <para><b>Inputs arrive the way the runner delivers them.</b> Each <c>env</c> entry of the action's step is
/// resolved from its <c>${{ inputs.x }}</c> expression and that input's declared default. So a default that
/// changes, or an input that is declared but never wired to the script, fails here rather than in a consumer's
/// workflow.</para>
///
/// <para><b>The in-memory artifacts endpoint filters by <c>name</c>, as the real one does.</b> Checked against a
/// live run: two artifacts without the parameter, one with it, none for an unknown name. A script that stopped
/// passing <c>name</c> would find nothing, and every link fact below would fail.</para>
///
/// <para>The logic was hardened on a consumer repository before it was generalised here. Each fact after the
/// first three pins one of the ways that version was found to go wrong: a late older run, a hand edit that
/// changes line endings or leaves trailing whitespace, a nullable date, another author's comment carrying the
/// marker.</para>
/// </summary>
public class PrReportLinkActionTests
{
    private const string BotLogin = "github-actions[bot]";
    private const string Unit = "unit-test-reports";
    private const string Component = "component-test-reports";

    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ActionDir =>
        Path.Combine(RepoRoot, "templates", "github-actions", "kronikol-pr-report-link");

    private static string LanePath => Path.Combine(RepoRoot, ".github", "workflows", "pr-report-link.yml");

    private static void SkipWithoutNode() =>
        Assert.SkipWhen(!NodeProbe.IsAvailable, "Node.js not available on PATH");

    private static Dictionary<string, string> Label(string label) => new() { ["label"] = label };

    [Fact]
    public void The_first_run_creates_one_comment_linking_its_artifact_with_when_it_was_uploaded_and_when_it_expires()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit, Label("Unit tests"))
            .Go();

        var comment = Assert.Single(outcome.BotComments);
        Assert.StartsWith("<!-- kronikol-report-link -->\n## 📊 Kronikol test reports\n", comment.Body, StringComparison.Ordinal);
        Assert.StartsWith(
            "- 🧪 **Unit tests** — 📦 [unit-test-reports](https://github.com/octo/app/actions/runs/101/artifacts/9001) "
            + "🕒 _(last updated 2026-09-15 12:34 UTC)_ ⏳ _(expires on 2026-09-16 12:34 UTC)_ ",
            LineFor(comment.Body, Unit), StringComparison.Ordinal);
        Assert.Contains("Open `TestRunReport.html` inside it.", comment.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_artifact_adds_its_own_line_to_the_same_comment_and_lines_are_ordered_by_label()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit, Label("Unit tests"))
            .Upload(102, 9002, Component, "2026-09-15T12:40:00Z", "2026-09-16T12:40:00Z")
            .Run(102, Component, Label("Component tests"))
            .Go();

        var lines = LaneLines(Assert.Single(outcome.BotComments).Body);
        Assert.Equal(2, lines.Count);
        Assert.Contains("**Component tests**", lines[0], StringComparison.Ordinal);
        Assert.Contains("**Unit tests**", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_run_replaces_only_its_own_line()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit)
            .Upload(102, 9002, Component, "2026-09-15T12:40:00Z", "2026-09-16T12:40:00Z")
            .Run(102, Component)
            .Upload(201, 9003, Unit, "2026-09-15T13:00:00Z", "2026-09-16T13:00:00Z")
            .Run(201, Unit)
            .Go();

        var body = Assert.Single(outcome.BotComments).Body;
        Assert.Contains("runs/201/artifacts/9003", LineFor(body, Unit), StringComparison.Ordinal);
        Assert.Contains("runs/102/artifacts/9002", LineFor(body, Component), StringComparison.Ordinal);
        Assert.DoesNotContain("runs/101/", body, StringComparison.Ordinal);
    }

    [Fact]
    public void An_older_run_that_finishes_after_a_newer_one_leaves_the_newer_link_alone()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(201, 9003, Unit, "2026-09-15T13:00:00Z", "2026-09-16T13:00:00Z")
            .Run(201, Unit)
            .Upload(101, 9001, Unit, "2026-09-15T13:02:00Z", "2026-09-16T13:02:00Z")
            .Run(101, Unit)
            .Go();

        Assert.Equal(1, outcome.Writes);
        Assert.Contains("runs/201/artifacts/9003", LineFor(Assert.Single(outcome.BotComments).Body, Unit), StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_uploaded_no_artifact_warns_and_writes_nothing()
    {
        SkipWithoutNode();

        var outcome = new PullRequest().Run(301, Unit).Go();

        Assert.Equal(0, outcome.Writes);
        Assert.Empty(outcome.BotComments);
        Assert.Contains(Unit, Assert.Single(outcome.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void An_expired_upload_is_never_linked_and_the_warning_says_it_expired()
    {
        SkipWithoutNode();

        // A link job re-run after its run's artifact expired finds the upload listed with `expired: true`. A line
        // written then would link a download that is already gone.
        var outcome = new PullRequest()
            .Upload(301, 9020, Unit, "2026-09-15T12:00:00Z", "2026-09-16T12:00:00Z", expired: true)
            .Run(301, Unit)
            .Go();

        Assert.Equal(0, outcome.Writes);
        Assert.Empty(outcome.BotComments);
        var warning = Assert.Single(outcome.Warnings);
        Assert.Contains(Unit, warning, StringComparison.Ordinal);
        Assert.Contains("expired", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rerun_links_the_newest_upload_of_its_artifact()
    {
        SkipWithoutNode();

        // A re-run keeps its run id and uploads again. The newer upload is listed first here on purpose, so
        // taking the last one listed would pick the wrong artifact.
        var outcome = new PullRequest()
            .Upload(201, 9004, Unit, "2026-09-15T14:05:30Z", "2026-09-16T14:05:28Z")
            .Upload(201, 9003, Unit, "2026-09-15T13:00:00Z", "2026-09-16T13:00:00Z")
            .Run(201, Unit)
            .Go();

        var line = LineFor(Assert.Single(outcome.BotComments).Body, Unit);
        Assert.Contains("runs/201/artifacts/9004) 🕒 _(last updated 2026-09-15 14:05 UTC)_", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rerun_links_the_newer_upload_even_when_its_artifact_id_is_the_lower()
    {
        SkipWithoutNode();

        // Run 36490823119 of the live lane, after "Re-run failed jobs": the run listed both attempts' artifacts of
        // one name, in this order, and the second attempt's upload had the lower id. Artifact ids do not follow
        // upload order, so only the upload time tells the newer one.
        var outcome = new PullRequest()
            .Upload(36490823119, 11000248501, Unit, "2026-09-28T22:11:44Z", "2026-09-29T22:11:43Z")
            .Upload(36490823119, 11000208931, Unit, "2026-09-28T22:12:51Z", "2026-09-29T22:12:51Z")
            .Run(36490823119, Unit)
            .Go();

        var line = LineFor(Assert.Single(outcome.BotComments).Body, Unit);
        Assert.Contains("runs/36490823119/artifacts/11000208931) 🕒 _(last updated 2026-09-28 22:12 UTC)_", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_saved_with_CRLF_line_endings_still_stops_an_older_run_and_the_next_rewrite_restores_LF()
    {
        SkipWithoutNode();

        // Saving an edit in GitHub's web editor stores CRLF. The comment the action writes has none.
        var outcome = new PullRequest()
            .Upload(500, 9005, Unit, "2026-09-15T15:00:00Z", "2026-09-16T15:00:00Z")
            .Run(500, Unit)
            .Upload(501, 9006, Component, "2026-09-15T15:01:00Z", "2026-09-16T15:01:00Z")
            .Run(501, Component)
            .EditComments("crlf")
            .Upload(400, 9007, Unit, "2026-09-15T15:05:00Z", "2026-09-16T15:05:00Z")
            .Run(400, Unit)
            .Upload(600, 9008, Unit, "2026-09-15T16:00:00Z", "2026-09-16T16:00:00Z")
            .Run(600, Unit)
            .Go();

        Assert.Equal(outcome.BodiesAfterStep[4], outcome.BodiesAfterStep[6]);

        var body = Assert.Single(outcome.BotComments).Body;
        Assert.DoesNotContain("\r", body, StringComparison.Ordinal);
        Assert.Equal(2, LaneLines(body).Count);
        Assert.Contains("runs/600/artifacts/9008", LineFor(body, Unit), StringComparison.Ordinal);
        Assert.Contains("runs/501/artifacts/9006", LineFor(body, Component), StringComparison.Ordinal);
    }

    [Fact]
    public void Whitespace_left_after_a_lines_tag_still_stops_an_older_run()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(1000, 9010, Unit, "2026-09-15T20:00:00Z", "2026-09-16T20:00:00Z")
            .Run(1000, Unit)
            .EditComments("trailing-whitespace")
            .Upload(999, 9011, Unit, "2026-09-15T20:05:00Z", "2026-09-16T20:05:00Z")
            .Run(999, Unit)
            .Go();

        Assert.Equal(1, outcome.Writes);
        Assert.Equal(outcome.BodiesAfterStep[2], outcome.BodiesAfterStep[4]);
    }

    [Fact]
    public void A_tag_with_a_field_this_version_does_not_know_still_stops_an_older_run()
    {
        SkipWithoutNode();

        // The first release tag that carries the action freezes the line's tag, so a later version can only add
        // to it, after the run id. A lane still on this version has to go on reading the run id, or its older
        // run replaces the newer version's link.
        var outcome = new PullRequest()
            .Upload(1000, 9010, Unit, "2026-09-15T20:00:00Z", "2026-09-16T20:00:00Z")
            .Run(1000, Unit)
            .EditComments("tag-field")
            .Upload(999, 9011, Unit, "2026-09-15T20:05:00Z", "2026-09-16T20:05:00Z")
            .Run(999, Unit)
            .Go();

        Assert.Equal(1, outcome.Writes);
        Assert.Equal(outcome.BodiesAfterStep[2], outcome.BodiesAfterStep[4]);
    }

    [Fact]
    public void A_line_break_in_an_input_keeps_the_line_whole()
    {
        SkipWithoutNode();

        // A `with:` value can span lines in YAML. Written into the comment as it is, it splits the line in two,
        // the half holding the tag no longer reads as a line of the comment, and an older run replaces the
        // newer link.
        var inputs = new Dictionary<string, string>
        {
            ["label"] = "Unit\ntests",
            ["icon"] = "🧪\n",
            ["heading"] = "Kronikol\r\nreports",
            ["report-file"] = "TestRun\nReport.html",
        };

        var outcome = new PullRequest()
            .Upload(600, 9030, Unit, "2026-09-15T22:00:00Z", "2026-09-16T22:00:00Z")
            .Run(600, Unit, inputs)
            .Upload(550, 9031, Unit, "2026-09-15T22:05:00Z", "2026-09-16T22:05:00Z")
            .Run(550, Unit, inputs)
            .Go();

        Assert.Equal(1, outcome.Writes);
        var body = Assert.Single(outcome.BotComments).Body;
        Assert.StartsWith("- 🧪 **Unit tests** — 📦 [unit-test-reports](https://github.com/octo/app/actions/runs/600/artifacts/9030) ",
            LineFor(body, Unit), StringComparison.Ordinal);
        Assert.Contains("\n## Kronikol reports\n", body, StringComparison.Ordinal);
        Assert.Contains("Open `TestRun Report.html` inside it.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void What_follows_the_end_marker_survives_every_rewrite_and_holds_no_line()
    {
        SkipWithoutNode();

        // The first release tag freezes what a lane on it does with the comment. A later version, or another
        // writer, adds to the comment after the end marker, so a lane still on this version keeps it. A line
        // there, even one carrying this lane's tag and a newer run, is not read as one of the comment's.
        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit)
            .EditComments("section-after-end")
            .Upload(201, 9003, Unit, "2026-09-15T13:00:00Z", "2026-09-16T13:00:00Z")
            .Run(201, Unit)
            .Go();

        var body = Assert.Single(outcome.BotComments).Body;
        Assert.EndsWith(
            "<!-- kronikol-report-link:end -->\n### Changed scenarios\n\n- a later section's line <!-- kronikol-report-link:unit-test-reports run:999 -->\n",
            body, StringComparison.Ordinal);
        Assert.Contains("runs/201/artifacts/9003",
            Assert.Single(LaneLines(body[..body.IndexOf("<!-- kronikol-report-link:end -->", StringComparison.Ordinal)])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_a_pull_request_it_fails_naming_the_event_and_writes_nothing()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(700, 9012, Unit, "2026-09-15T17:00:00Z", "2026-09-16T17:00:00Z")
            .Run(700, Unit, eventName: "workflow_dispatch")
            .Go();

        Assert.Equal(0, outcome.Writes);
        Assert.Contains("workflow_dispatch", Assert.Single(outcome.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_or_unparseable_artifact_dates_are_left_out_of_the_line()
    {
        SkipWithoutNode();

        // The REST description marks both dates nullable. `new Date(null)` is 1970, not an exception, and an
        // unparseable string throws, so neither may reach the formatter.
        var outcome = new PullRequest()
            .Upload(800, 9013, Unit, "2026-09-15T18:00:00Z", null)
            .Run(800, Unit)
            .Upload(801, 9014, Unit, null, "not a date")
            .Run(801, Unit)
            .Go();

        var withoutExpiry = LineFor(Assert.Single(outcome.BodiesAfterStep[1]), Unit);
        Assert.Contains("_(last updated 2026-09-15 18:00 UTC)_", withoutExpiry, StringComparison.Ordinal);
        Assert.DoesNotContain("expires on", withoutExpiry, StringComparison.Ordinal);

        var withNeither = LineFor(Assert.Single(outcome.BotComments).Body, Unit);
        Assert.Contains("[unit-test-reports](https://github.com/octo/app/actions/runs/801/artifacts/9014)", withNeither, StringComparison.Ordinal);
        foreach (var absent in new[] { "last updated", "expires on", "1970", "N/A" })
            Assert.DoesNotContain(absent, withNeither, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_its_own_comment_is_ever_edited()
    {
        SkipWithoutNode();

        const string quoting = "quoting <!-- kronikol-report-link --> in passing";
        const string pasted = "<!-- kronikol-report-link -->\npasted from the bot's comment to discuss it";
        // Written with the same workflow token by another workflow, so its author alone does not rule it out.
        const string mentioning = "Another workflow's comment, which mentions <!-- kronikol-report-link --> in its text";

        var outcome = new PullRequest()
            .Comment(90, "some-other-app[bot]", quoting)
            .Comment(91, "a-person", pasted)
            .Comment(92, BotLogin, mentioning)
            .Upload(900, 9015, Unit, "2026-09-15T19:00:00Z", "2026-09-16T19:00:00Z")
            .Run(900, Unit)
            .Upload(901, 9016, Unit, "2026-09-15T19:30:00Z", "2026-09-16T19:30:00Z")
            .Run(901, Unit)
            .Go();

        Assert.Equal(quoting, outcome.Comments.Single(c => c.Id == 90).Body);
        Assert.Equal(pasted, outcome.Comments.Single(c => c.Id == 91).Body);
        Assert.Equal(mentioning, outcome.Comments.Single(c => c.Id == 92).Body);

        var own = Assert.Single(outcome.BotComments, c => c.Id != 92);
        Assert.Contains("runs/901/artifacts/9016", LineFor(own.Body, Unit), StringComparison.Ordinal);
    }

    [Fact]
    public void Label_defaults_to_the_artifact_name_and_icon_to_a_test_tube()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit)
            .Go();

        Assert.StartsWith("- 🧪 **unit-test-reports** — 📦 [unit-test-reports](",
            LineFor(Assert.Single(outcome.BotComments).Body, Unit), StringComparison.Ordinal);
    }

    [Fact]
    public void Heading_icon_and_report_file_inputs_appear_in_the_comment()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit, new()
            {
                ["heading"] = "🌙 Nightly reports",
                ["icon"] = "🌙",
                ["report-file"] = "Specifications.html",
            })
            .Go();

        var body = Assert.Single(outcome.BotComments).Body;
        Assert.Contains("\n## 🌙 Nightly reports\n", body, StringComparison.Ordinal);
        Assert.StartsWith("- 🌙 **", LineFor(body, Unit), StringComparison.Ordinal);
        Assert.Contains("Open `Specifications.html` inside it.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_different_comment_key_keeps_a_separate_comment_that_runs_under_the_default_key_never_touch()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit)
            .Upload(102, 9002, "nightly-reports", "2026-09-15T12:40:00Z", "2026-09-16T12:40:00Z")
            .Run(102, "nightly-reports", new() { ["comment-key"] = "kronikol-nightly" })
            .Upload(201, 9003, Unit, "2026-09-15T13:00:00Z", "2026-09-16T13:00:00Z")
            .Run(201, Unit)
            .Go();

        Assert.Equal(2, outcome.BotComments.Count);

        var main = outcome.BotComments.Single(c => c.Body.StartsWith("<!-- kronikol-report-link -->\n", StringComparison.Ordinal));
        Assert.Contains("runs/201/artifacts/9003", Assert.Single(LaneLines(main.Body)), StringComparison.Ordinal);

        var nightly = outcome.BotComments.Single(c => c.Body.StartsWith("<!-- kronikol-nightly -->\n", StringComparison.Ordinal));
        Assert.Contains("runs/102/artifacts/9002", Assert.Single(LaneLines(nightly.Body)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_comment_key_that_could_break_out_of_its_marker_is_refused()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Unit, "2026-09-15T12:34:09Z", "2026-09-16T12:34:07Z")
            .Run(101, Unit, new() { ["comment-key"] = "reports --> <b>hi</b>" })
            .Go();

        Assert.Equal(0, outcome.Writes);
        Assert.Contains("comment-key", Assert.Single(outcome.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_declared_input_is_passed_to_the_script()
    {
        var action = ActionDefinition.Load(ActionDir);

        var wired = action.StepEnv.Values
            .Select(expression => InputExpression.Match(expression))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var unwired = action.Inputs.Keys.Where(input => !wired.Contains(input)).ToList();
        Assert.True(unwired.Count == 0,
            "action.yml declares inputs its script never receives, so setting them does nothing: " + string.Join(", ", unwired));
    }

    /// <summary>
    /// The README shows the comment a reader will find on their pull request, so it is the comment the action
    /// writes: the README's two lanes run through the script, compared byte for byte.
    /// </summary>
    [Fact]
    public void The_readme_shows_the_comment_the_action_writes()
    {
        SkipWithoutNode();

        var outcome = new PullRequest()
            .Upload(101, 9001, Component, "2026-09-15T13:58:00Z", "2026-09-16T13:58:00Z")
            .Run(101, Component, Label("Component tests"))
            .Upload(102, 9002, Unit, "2026-09-15T14:05:00Z", "2026-09-16T14:05:00Z")
            .Run(102, Unit, Label("Unit tests"))
            .Go();

        var readme = File.ReadAllText(Path.Combine(ActionDir, "README.md")).ReplaceLineEndings("\n");
        var sample = Regex.Match(readme, @"^```markdown\n(.*?)\n```$", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(sample.Success, "the README shows no ```markdown sample of the comment the action writes");
        Assert.Equal(Assert.Single(outcome.BotComments).Body, sample.Groups[1].Value);
    }

    /// <summary>
    /// The README's workflow is what a consumer copies, so it is held to the action. It may only pass inputs
    /// the action declares, it must pass every required one, and the calling job needs the per-PR concurrency
    /// group, <c>queue: max</c>, the fork guard and the two permissions the action depends on. Without the
    /// group, two lanes finishing together can each read the comment before the other writes, and one line is
    /// lost. With the group's default queue, a third job that arrives while one runs and one waits cancels
    /// the waiting one, and that lane's link is lost the same way.
    /// </summary>
    [Fact]
    public void The_readme_workflow_calls_the_action_as_it_is_declared_from_a_job_that_can_use_it()
    {
        var action = ActionDefinition.Load(ActionDir);
        var readme = File.ReadAllText(Path.Combine(ActionDir, "README.md")).ReplaceLineEndings("\n");

        var calls = 0;
        foreach (Match block in Regex.Matches(readme, @"^```ya?ml\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline))
            calls += CallsOfTheAction(ActionDefinition.LoadYaml(block.Groups[1].Value), action);

        Assert.True(calls > 0, "the README has no workflow that calls the action, so nothing here checked it.");
    }

    /// <summary>
    /// The workflow that runs the action for real, on the pull requests that change it, calls it the way the README
    /// tells a consumer to, checks what it wrote, and runs whenever a link of the chain it proves changes: the
    /// action, the lane, the project it tests and the code that writes the reports' outputs.
    /// </summary>
    [Fact]
    public void The_live_lane_calls_the_action_as_it_is_declared()
    {
        var action = ActionDefinition.Load(ActionDir);
        var laneText = File.ReadAllText(LanePath);
        var lane = ActionDefinition.LoadYaml(laneText);

        Assert.Equal(1, CallsOfTheAction(lane, action));

        var check = LaneCheck.Load();
        Assert.True(check.Env.TryGetValue("ARTIFACT_NAME", out var checkedName) && checkedName == check.ArtifactName,
            $"the lane's check reads the comment for \"{checkedName}\", but the lane links \"{check.ArtifactName}\"");

        var pullRequest = (YamlMappingNode)((YamlMappingNode)lane["on"])["pull_request"];
        var paths = ((YamlSequenceNode)pullRequest["paths"]).Children.Select(p => ((YamlScalarNode)p).Value).ToList();
        Assert.Contains("templates/github-actions/kronikol-pr-report-link/**", paths);
        Assert.Contains(".github/workflows/pr-report-link.yml", paths);

        var tested = Regex.Match(laneText, @"dotnet test (\S+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(tested), "the lane runs no `dotnet test <project>` step");
        Assert.Contains(tested + "/**", paths);

        var writers = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("reports-path=", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file).Replace('\\', '/'))
            .ToList();
        Assert.NotEmpty(writers);
        Assert.True(writers.All(paths.Contains),
            "the lane does not run when the code that writes the reports-path output changes: " + string.Join(", ", writers.Where(w => !paths.Contains(w))));
    }

    /// <summary>
    /// The live lane runs the README's workflow on GitHub, so it proves the README's actions at the versions the lane
    /// itself uses. One bumped without the other leaves the README's version unproven.
    /// </summary>
    [Fact]
    public void The_readme_workflow_uses_its_actions_at_the_versions_the_live_lane_runs()
    {
        var readme = File.ReadAllText(Path.Combine(ActionDir, "README.md")).ReplaceLineEndings("\n");
        var workflows = string.Concat(Regex.Matches(readme, @"^```ya?ml\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline)
            .Select(block => block.Groups[1].Value));

        var inReadme = ActionsUsed(workflows);
        var inLane = ActionsUsed(File.ReadAllText(LanePath));

        Assert.NotEmpty(inReadme);
        var unproven = inReadme.Where(use => !inLane.Contains(use)).ToList();
        Assert.True(unproven.Count == 0,
            "the README's workflow uses actions the live lane does not run at that version: " + string.Join(", ", unproven));
    }

    private static HashSet<string> ActionsUsed(string yaml) =>
        Regex.Matches(yaml, @"uses:\s*(actions/[A-Za-z0-9_.-]+@\S+)").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The action only warns when it finds nothing to link, so the live lane reads the comment back after it, and
    /// would otherwise pass while the comment went stale. The check runs here against the same in-memory GitHub.
    /// </summary>
    [Fact]
    public void The_live_lanes_check_fails_when_the_comment_does_not_link_this_runs_newest_upload()
    {
        SkipWithoutNode();
        var check = LaneCheck.Load();
        var name = check.ArtifactName;

        // Nothing uploaded: the action warns and writes no comment.
        var none = new PullRequest().Run(101, name).Check(101, check).Go();
        Assert.Contains(name, Assert.Single(none.Failures), StringComparison.Ordinal);

        // This run uploaded nothing, so the line still links the previous run's report.
        var stale = new PullRequest()
            .Upload(101, 9001, name, "2026-09-15T12:00:00Z", "2026-09-16T12:00:00Z")
            .Run(101, name)
            .Run(102, name)
            .Check(102, check)
            .Go();
        Assert.Contains("run 101", Assert.Single(stale.Failures), StringComparison.Ordinal);

        // An upload the link did not follow: the line names this run, but not its newest upload.
        var behind = new PullRequest()
            .Upload(201, 9003, name, "2026-09-15T13:00:00Z", "2026-09-16T13:00:00Z")
            .Run(201, name)
            .Upload(201, 9004, name, "2026-09-15T14:05:00Z", "2026-09-16T14:05:00Z")
            .Check(201, check)
            .Go();
        Assert.Contains("9004", Assert.Single(behind.Failures), StringComparison.Ordinal);

        // The link job re-run after the upload expired: the action leaves the line, which links a download that is gone.
        var expired = new PullRequest()
            .Upload(301, 9020, name, "2026-09-15T12:00:00Z", "2026-09-16T12:00:00Z")
            .Run(301, name)
            .Expire(301)
            .Run(301, name)
            .Check(301, check)
            .Go();
        Assert.Contains("expired", Assert.Single(expired.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void The_live_lanes_check_passes_when_this_run_or_a_newer_one_linked_the_report()
    {
        SkipWithoutNode();
        var check = LaneCheck.Load();
        var name = check.ArtifactName;

        var outcome = new PullRequest()
            .Upload(101, 9001, name, "2026-09-15T12:00:00Z", "2026-09-16T12:00:00Z")
            .Run(101, name)
            .Check(101, check)
            .Upload(102, 9002, name, "2026-09-15T12:30:00Z", "2026-09-16T12:30:00Z")
            .Run(102, name)
            // Run 101's link job, re-run after run 102's: the action leaves the newer line alone, and so does the check.
            .Run(101, name)
            .Check(101, check)
            .Go();

        Assert.Empty(outcome.Failures);
        Assert.Contains("runs/102/artifacts/9002", LineFor(Assert.Single(outcome.BotComments).Body, name), StringComparison.Ordinal);
    }

    /// <summary>Holds each job of a workflow that calls the action to the rules above, and counts the calls.</summary>
    private static int CallsOfTheAction(YamlMappingNode root, ActionDefinition action)
    {
        var calls = 0;
        if (!root.Children.TryGetValue(new YamlScalarNode("jobs"), out var jobs))
            return 0;

        // The action links this run's upload of `artifact-name`, so the same workflow has to upload that name, or the
        // action finds nothing and only warns.
        var uploaded = ((YamlMappingNode)jobs).Children.Values.Cast<YamlMappingNode>()
            .SelectMany(job => job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps)
                ? ((YamlSequenceNode)steps).Children.Cast<YamlMappingNode>()
                : [])
            .Where(step => ActionDefinition.Scalar(step, "uses")?.StartsWith("actions/upload-artifact@", StringComparison.Ordinal) == true)
            .Select(step => step.Children.TryGetValue(new YamlScalarNode("with"), out var with) ? ActionDefinition.Scalar((YamlMappingNode)with, "name") : null)
            .ToList();

        foreach (var (jobName, jobNode) in ((YamlMappingNode)jobs).Children)
        {
            var job = (YamlMappingNode)jobNode;
            if (!job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps))
                continue;

            foreach (var step in ((YamlSequenceNode)steps).Children.Cast<YamlMappingNode>())
            {
                var uses = ActionDefinition.Scalar(step, "uses");
                if (uses is null || !uses.Contains("kronikol-pr-report-link", StringComparison.Ordinal))
                    continue;
                calls++;

                var passed = step.Children.TryGetValue(new YamlScalarNode("with"), out var with)
                    ? ((YamlMappingNode)with).Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList()
                    : [];
                Assert.DoesNotContain(passed, p => !action.Inputs.ContainsKey(p));
                Assert.Empty(action.Inputs.Where(i => i.Value.Required && !passed.Contains(i.Key)).Select(i => i.Key));

                var linked = ActionDefinition.Scalar((YamlMappingNode)with!, "artifact-name");
                Assert.True(uploaded.Contains(linked), $"job {jobName} links \"{linked}\", which no step of the workflow uploads");

                var concurrency = job.Children.TryGetValue(new YamlScalarNode("concurrency"), out var node)
                    ? (YamlMappingNode)node
                    : new YamlMappingNode();
                var group = ActionDefinition.Scalar(concurrency, "group");
                Assert.True(group?.Contains("github.event.pull_request.number", StringComparison.Ordinal) == true,
                    $"job {jobName} calls the action without a per-pull-request concurrency group");
                Assert.True(ActionDefinition.Scalar(concurrency, "queue") == "max",
                    $"job {jobName} leaves its concurrency group on the default queue, which cancels a waiting job when a third arrives");
                Assert.NotEqual("true", ActionDefinition.Scalar(concurrency, "cancel-in-progress"));

                // A fork's token cannot write comments, so on a fork's pull request the job would fail.
                Assert.Contains("github.event.pull_request.head.repo.full_name == github.repository",
                    ActionDefinition.Scalar(job, "if") ?? "", StringComparison.Ordinal);

                var permissions = (YamlMappingNode)job["permissions"];
                Assert.Equal("write", ActionDefinition.Scalar(permissions, "pull-requests"));
                Assert.Equal("read", ActionDefinition.Scalar(permissions, "actions"));
            }
        }

        return calls;
    }

    private static readonly Regex InputExpression = new(@"^\$\{\{\s*inputs\.([A-Za-z0-9_-]+)\s*\}\}$");

    private static List<string> LaneLines(string body) =>
        body.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();

    private static string LineFor(string body, string artifactName) =>
        Assert.Single(LaneLines(body), l => l.Contains($":{artifactName} run:", StringComparison.Ordinal));

    /// <summary>The live lane's step after its call of the action, which reads the comment back, and the name it links.</summary>
    private sealed record LaneCheck(string Script, Dictionary<string, string> Env, string ArtifactName)
    {
        public static LaneCheck Load()
        {
            var lane = ActionDefinition.LoadYaml(File.ReadAllText(LanePath));
            foreach (var job in ((YamlMappingNode)lane["jobs"]).Children.Values.Cast<YamlMappingNode>())
            {
                if (!job.Children.TryGetValue(new YamlScalarNode("steps"), out var stepsNode))
                    continue;

                var steps = ((YamlSequenceNode)stepsNode).Children.Cast<YamlMappingNode>().ToList();
                var call = steps.FindIndex(s => ActionDefinition.Scalar(s, "uses")?.Contains("kronikol-pr-report-link", StringComparison.Ordinal) == true);
                if (call < 0)
                    continue;

                Assert.True(call + 1 < steps.Count, "the live lane's job ends with the action, so nothing checks what it wrote");
                var check = steps[call + 1];
                Assert.StartsWith("actions/github-script@", ActionDefinition.Scalar(check, "uses") ?? "", StringComparison.Ordinal);
                var env = check.Children.TryGetValue(new YamlScalarNode("env"), out var envNode)
                    ? ((YamlMappingNode)envNode).Children.ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => ((YamlScalarNode)kv.Value).Value!, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                return new LaneCheck(
                    ActionDefinition.Scalar((YamlMappingNode)check["with"], "script")!,
                    env,
                    ActionDefinition.Scalar((YamlMappingNode)steps[call]["with"], "artifact-name")!);
            }

            throw new InvalidOperationException("the live lane calls the action in no job");
        }
    }

    private sealed record Comment(long Id, string Login, string Body);

    private sealed record Outcome(
        IReadOnlyList<Comment> Comments,
        int Writes,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Failures,
        IReadOnlyList<IReadOnlyList<string>> BodiesAfterStep)
    {
        public IReadOnlyList<Comment> BotComments => Comments.Where(c => c.Login == BotLogin).ToList();
    }

    /// <summary>One pull request, number 7 on octo/app, and the steps that happen to it in order.</summary>
    private sealed class PullRequest
    {
        private readonly ActionDefinition _action = ActionDefinition.Load(ActionDir);
        private readonly List<object> _comments = [];
        private readonly List<object> _steps = [];

        public PullRequest Comment(long id, string login, string body)
        {
            _comments.Add(new { id, issue = 7, user = new { login, type = login.EndsWith("[bot]", StringComparison.Ordinal) ? "Bot" : "User" }, body });
            return this;
        }

        public PullRequest Upload(long runId, long id, string name, string? createdAt, string? expiresAt, bool expired = false)
        {
            _steps.Add(new { upload = new { runId, id, name, createdAt, expiresAt, expired } });
            return this;
        }

        public PullRequest Run(long runId, string artifactName, Dictionary<string, string>? inputs = null, string eventName = "pull_request")
        {
            var given = new Dictionary<string, string>(inputs ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["artifact-name"] = artifactName,
            };
            _steps.Add(new { run = new { runId, eventName, env = _action.EnvironmentFor(given) } });
            return this;
        }

        /// <summary>The live lane's check, run as the step after the action in run <paramref name="runId"/>.</summary>
        public PullRequest Check(long runId, LaneCheck check)
        {
            _steps.Add(new { run = new { runId, eventName = "pull_request", env = check.Env, script = check.Script } });
            return this;
        }

        /// <summary>Every upload of run <paramref name="runId"/> passes its retention and is listed as expired.</summary>
        public PullRequest Expire(long runId)
        {
            _steps.Add(new { expire = runId });
            return this;
        }

        /// <summary>
        /// A person editing the bot's comment, <c>crlf</c> or <c>trailing-whitespace</c>, or a later version of the
        /// action writing a field after each line's run id, <c>tag-field</c>, or a section after the end marker,
        /// <c>section-after-end</c>.
        /// </summary>
        public PullRequest EditComments(string how)
        {
            _steps.Add(new { edit = how });
            return this;
        }

        public Outcome Go()
        {
            var input = JsonSerializer.Serialize(new { script = _action.Script, comments = _comments, steps = _steps });
            var result = JsonNode.Parse(NodeProbe.RunWithStdin(Driver, input))!;

            return new Outcome(
                result["comments"]!.AsArray()
                    .Select(c => new Comment(c!["id"]!.GetValue<long>(), c["user"]!["login"]!.GetValue<string>(), c["body"]!.GetValue<string>()))
                    .ToList(),
                result["writes"]!.GetValue<int>(),
                result["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).ToList(),
                result["failures"]!.AsArray().Select(f => f!.GetValue<string>()).ToList(),
                result["bodies"]!.AsArray()
                    .Select(step => (IReadOnlyList<string>)step!.AsArray().Select(b => b!.GetValue<string>()).ToList())
                    .ToList());
        }
    }

    /// <summary>
    /// Runs the action's script the way actions/github-script does, as the body of an async function given
    /// <c>github</c>, <c>context</c>, <c>core</c> and <c>process</c>, against comments and artifacts held in memory.
    /// </summary>
    private const string Driver = """
        const fs = require('fs');
        const input = JSON.parse(fs.readFileSync(0, 'utf8'));
        const AsyncFunction = Object.getPrototypeOf(async () => {}).constructor;
        const script = new AsyncFunction('github', 'context', 'core', 'process', input.script);

        const state = { artifacts: {}, comments: input.comments, nextId: 1000, writes: 0, warnings: [], failures: [], bodies: [] };
        const bot = 'github-actions[bot]';

        const github = {
          paginate: async (method, params) => await method(params),
          rest: {
            actions: {
              listWorkflowRunArtifacts: async ({ run_id, name }) => (state.artifacts[run_id] ?? []).filter(a => a.name === name),
            },
            issues: {
              listComments: async ({ issue_number }) => state.comments.filter(c => c.issue === issue_number),
              createComment: async ({ issue_number, body }) => {
                state.writes++;
                state.comments.push({ id: state.nextId++, issue: issue_number, user: { login: bot, type: 'Bot' }, body });
              },
              updateComment: async ({ comment_id, body }) => {
                const comment = state.comments.find(c => c.id === comment_id);
                if (!comment) throw new Error(`there is no comment ${comment_id}`);
                if (comment.user.login !== bot) throw new Error(`the action edited comment ${comment_id} by ${comment.user.login}, which is not its own`);
                state.writes++;
                comment.body = body;
              },
            },
          },
        };

        (async () => {
          for (const step of input.steps) {
            if (step.upload) {
              const u = step.upload;
              (state.artifacts[u.runId] ??= []).push({ id: u.id, name: u.name, created_at: u.createdAt, expires_at: u.expiresAt, expired: u.expired });
            } else if (step.expire) {
              for (const a of state.artifacts[step.expire] ?? []) a.expired = true;
            } else if (step.edit === 'crlf') {
              for (const c of state.comments.filter(c => c.user.login === bot)) c.body = c.body.replace(/\r?\n/g, '\r\n');
            } else if (step.edit === 'tag-field') {
              for (const c of state.comments.filter(c => c.user.login === bot)) c.body = c.body.replace(/( run:\d+)( -->)/g, '$1 x:1$2');
            } else if (step.edit === 'section-after-end') {
              for (const c of state.comments.filter(c => c.user.login === bot))
                c.body += '\n### Changed scenarios\n\n- a later section\'s line <!-- kronikol-report-link:unit-test-reports run:999 -->\n';
            } else if (step.edit === 'trailing-whitespace') {
              for (const c of state.comments.filter(c => c.user.login === bot))
                c.body = c.body.split('\n').map(l => l.startsWith('- ') ? l + ' \t ' : l).join('\n');
            } else if (step.run) {
              const r = step.run;
              // A step of its own, the live lane's check, or else the action.
              const run = r.script ? new AsyncFunction('github', 'context', 'core', 'process', r.script) : script;
              await run(
                github,
                {
                  repo: { owner: 'octo', repo: 'app' },
                  runId: r.runId,
                  eventName: r.eventName,
                  serverUrl: 'https://github.com',
                  payload: r.eventName === 'pull_request' ? { pull_request: { number: 7 } } : {},
                },
                { warning: m => state.warnings.push(m), info: () => {}, setFailed: m => state.failures.push(m) },
                { env: r.env });
            } else {
              throw new Error('unknown step ' + JSON.stringify(step));
            }
            state.bodies.push(state.comments.filter(c => c.user.login === bot).map(c => c.body));
          }
          process.stdout.write(JSON.stringify(state));
        })().catch(e => { console.error(e && e.stack || String(e)); process.exit(1); });
        """;
}
