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
    /// The README's workflow is what a consumer copies, so it is held to the action. It may only pass inputs
    /// the action declares, it must pass every required one, and the calling job needs the per-PR concurrency
    /// group and the two permissions the action depends on. Without the group, two lanes finishing together
    /// can each read the comment before the other writes, and one line is lost.
    /// </summary>
    [Fact]
    public void The_readme_workflow_calls_the_action_as_it_is_declared_from_a_job_that_can_use_it()
    {
        var action = ActionDefinition.Load(ActionDir);
        var readme = File.ReadAllText(Path.Combine(ActionDir, "README.md")).ReplaceLineEndings("\n");

        var calls = 0;
        foreach (Match block in Regex.Matches(readme, @"^```ya?ml\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline))
        {
            var root = ActionDefinition.LoadYaml(block.Groups[1].Value);
            if (!root.Children.TryGetValue(new YamlScalarNode("jobs"), out var jobs))
                continue;

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
                    Assert.Empty(passed.Where(p => !action.Inputs.ContainsKey(p)));
                    Assert.Empty(action.Inputs.Where(i => i.Value.Required && !passed.Contains(i.Key)).Select(i => i.Key));

                    var group = job.Children.TryGetValue(new YamlScalarNode("concurrency"), out var concurrency)
                        ? ActionDefinition.Scalar((YamlMappingNode)concurrency, "group")
                        : null;
                    Assert.True(group?.Contains("github.event.pull_request.number", StringComparison.Ordinal) == true,
                        $"job {jobName} calls the action without a per-pull-request concurrency group");

                    var permissions = (YamlMappingNode)job["permissions"];
                    Assert.Equal("write", ActionDefinition.Scalar(permissions, "pull-requests"));
                    Assert.Equal("read", ActionDefinition.Scalar(permissions, "actions"));
                }
            }
        }

        Assert.True(calls > 0, "the README has no workflow that calls the action, so nothing here checked it.");
    }

    private static readonly Regex InputExpression = new(@"^\$\{\{\s*inputs\.([A-Za-z0-9_-]+)\s*\}\}$");

    private static List<string> LaneLines(string body) =>
        body.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();

    private static string LineFor(string body, string artifactName) =>
        Assert.Single(LaneLines(body), l => l.Contains($":{artifactName} run:", StringComparison.Ordinal));

    private sealed record ActionDefinition(
        IReadOnlyDictionary<string, (bool Required, string Default)> Inputs,
        IReadOnlyDictionary<string, string> StepEnv,
        string Script)
    {
        public static ActionDefinition Load(string directory)
        {
            var root = LoadYaml(File.ReadAllText(Path.Combine(directory, "action.yml")));

            var inputs = ((YamlMappingNode)root["inputs"]).Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv =>
                {
                    var input = (YamlMappingNode)kv.Value;
                    return (Required: Scalar(input, "required") == "true", Default: Scalar(input, "default") ?? "");
                },
                StringComparer.Ordinal);

            var step = (YamlMappingNode)Assert.Single(((YamlSequenceNode)((YamlMappingNode)root["runs"])["steps"]).Children);
            var env = ((YamlMappingNode)step["env"]).Children.ToDictionary(
                kv => ((YamlScalarNode)kv.Key).Value!,
                kv => ((YamlScalarNode)kv.Value).Value!,
                StringComparer.Ordinal);

            return new ActionDefinition(inputs, env, Scalar((YamlMappingNode)step["with"], "script")!);
        }

        public static YamlMappingNode LoadYaml(string text)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            return (YamlMappingNode)stream.Documents[0].RootNode;
        }

        public static string? Scalar(YamlMappingNode node, string key) =>
            node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value : null;

        /// <summary>What the runner would put in the step's environment for these inputs.</summary>
        public Dictionary<string, string> EnvironmentFor(IReadOnlyDictionary<string, string> given)
        {
            foreach (var (name, (required, _)) in Inputs)
                Assert.True(!required || given.ContainsKey(name), $"the runner refuses a call without the required input {name}");

            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (variable, expression) in StepEnv)
            {
                var match = InputExpression.Match(expression);
                Assert.True(match.Success, $"env {variable} is `{expression}`, which is not an input this test can supply");

                var input = match.Groups[1].Value;
                Assert.True(Inputs.ContainsKey(input), $"env {variable} reads inputs.{input}, which action.yml does not declare");
                environment[variable] = given.TryGetValue(input, out var value) ? value : Inputs[input].Default;
            }

            return environment;
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

        public PullRequest Upload(long runId, long id, string name, string? createdAt, string? expiresAt)
        {
            _steps.Add(new { upload = new { runId, id, name, createdAt, expiresAt } });
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

        /// <summary>A person editing the bot's comment: <c>crlf</c> or <c>trailing-whitespace</c>.</summary>
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
              (state.artifacts[u.runId] ??= []).push({ id: u.id, name: u.name, created_at: u.createdAt, expires_at: u.expiresAt, expired: false });
            } else if (step.edit === 'crlf') {
              for (const c of state.comments.filter(c => c.user.login === bot)) c.body = c.body.replace(/\r?\n/g, '\r\n');
            } else if (step.edit === 'trailing-whitespace') {
              for (const c of state.comments.filter(c => c.user.login === bot))
                c.body = c.body.split('\n').map(l => l.startsWith('- ') ? l + ' \t ' : l).join('\n');
            } else if (step.run) {
              const r = step.run;
              await script(
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
