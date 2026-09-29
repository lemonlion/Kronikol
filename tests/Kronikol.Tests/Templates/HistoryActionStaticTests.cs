using System.Text.RegularExpressions;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;

namespace Kronikol.Tests.Templates;

/// <summary>
/// What the history action's files must say, read without running them (plans/HISTORY_ACTION_PLAN.md §5.3): every
/// step's shell and expressions, every input wired to a script and every variable a script reads set by its step, the
/// actions it calls pinned to commits, no force push, the tool version held to the repository's, git's credential kept
/// off every command line and out of every file, scripts that stay LF on any checkout, and a README whose workflows
/// call each phase as it is declared from a job that can use it.
/// </summary>
public class HistoryActionStaticTests
{
    private static readonly string[] Phases = ["read", "gate", "save", "record"];

    public static TheoryData<string> PhaseNames => new(Phases);

    private static ActionDefinition Phase(string name) => ActionDefinition.Load(Path.Combine(HistoryWorld.Folder, name));

    private static string ActionText(string name) => File.ReadAllText(Path.Combine(HistoryWorld.Folder, name, "action.yml"));

    private static IEnumerable<string> Scripts() => Directory.EnumerateFiles(Path.Combine(HistoryWorld.Folder, "scripts"), "*.sh");

    private static readonly Regex ScriptRun = new(@"scripts/([a-z]+\.sh)");
    private static readonly Regex Sourced = new(@"(?m)^\s*(?:\.|source)\s+""\$\(dirname ""\$0""\)/([a-z]+\.sh)""");
    private static readonly Regex Variable = new(@"\$\{?(KRONIKOL_HISTORY_[A-Z_]+)");

    [Theory]
    [MemberData(nameof(PhaseNames))]
    public void Every_run_step_declares_bash_and_writes_no_expression_into_its_script(string phase)
    {
        foreach (var step in Phase(phase).Steps.Where(s => s.Run is not null))
        {
            Assert.Equal("bash", step.Shell);
            Assert.DoesNotContain("${{", step.Run!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(PhaseNames))]
    public void Every_expression_is_one_the_runner_evaluates(string phase)
    {
        var expressions = WorkflowExpression.ExpressionsIn(ActionText(phase));
        Assert.NotEmpty(expressions);
        foreach (var expression in expressions)
            WorkflowExpression.Validate(expression);
    }

    /// <summary>The scripts a phase's run step runs, and every script those source.</summary>
    private static IReadOnlyList<string> ScriptsOf(ActionStep step)
    {
        var scripts = new List<string>();
        var queue = new Queue<string>(ScriptRun.Matches(step.Run ?? "").Select(m => m.Groups[1].Value));
        while (queue.TryDequeue(out var script))
        {
            if (scripts.Contains(script))
                continue;
            scripts.Add(script);
            foreach (Match sourced in Sourced.Matches(File.ReadAllText(Path.Combine(HistoryWorld.Folder, "scripts", script))))
                queue.Enqueue(sourced.Groups[1].Value);
        }
        return scripts;
    }

    [Theory]
    [MemberData(nameof(PhaseNames))]
    public void Every_declared_input_reaches_a_step_and_every_variable_its_scripts_read_is_set_by_the_step(string phase)
    {
        var action = Phase(phase);
        var text = string.Join("\n", action.Steps.SelectMany(s => s.Env.Values.Concat(s.With.Values).Append(s.If ?? "")));
        var unwired = action.Inputs.Keys.Where(input => !Regex.IsMatch(text, @"\binputs\." + Regex.Escape(input) + @"\b")).ToList();
        Assert.True(unwired.Count == 0, $"{phase} declares inputs no step reads: {string.Join(", ", unwired)}");

        foreach (var step in action.Steps.Where(s => s.Run is not null))
        {
            var read = ScriptsOf(step).SelectMany(s => Variable.Matches(File.ReadAllText(Path.Combine(HistoryWorld.Folder, "scripts", s))))
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.NotEmpty(read);
            var unset = read.Where(v => !step.Env.ContainsKey(v)).ToList();
            Assert.True(unset.Count == 0, $"{phase}'s scripts read {string.Join(", ", unset)}, which its step does not set");
        }
    }

    [Theory]
    [MemberData(nameof(PhaseNames))]
    public void Every_action_a_phase_calls_is_pinned_to_a_commit_with_its_version_beside_it(string phase)
    {
        foreach (var line in ActionText(phase).Split('\n').Where(l => l.TrimStart().StartsWith("- uses:", StringComparison.Ordinal) || l.TrimStart().StartsWith("uses:", StringComparison.Ordinal)))
            Assert.Matches(@"uses:\s+actions/[a-z-]+@[0-9a-f]{40}\s+#\s+v\d+\.\d+\.\d+\s*$", line);
    }

    [Fact]
    public void The_pins_are_the_versions_the_plan_names()
    {
        Assert.Contains("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7.0.1", ActionText("save"), StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8.0.1", ActionText("record"), StringComparison.Ordinal);
    }

    [Fact]
    public void No_script_pushes_with_force()
    {
        foreach (var script in Scripts())
        {
            foreach (var line in File.ReadAllLines(script).Where(l => Regex.IsMatch(l, @"\bgit\b.*\bpush\b")))
            {
                Assert.DoesNotMatch(@"--force|\s-f\b|--force-with-lease|\s\+[a-zA-Z""$]", line);
            }
        }
        Assert.Contains(Scripts(), s => File.ReadAllText(s).Contains(" push ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_version_the_folder_installs_is_the_repositorys()
    {
        // As tool.sh reads it (tr -d ' \r\n'): a Windows runner checks the folder out with CRLF line endings, and a
        // copy of it reads the same version.
        var props = XDocument.Load(Path.Combine(HistoryWorld.RepoRoot, "Directory.Build.props"));
        var version = props.Descendants("Version").First().Value.Trim();
        var text = File.ReadAllText(Path.Combine(HistoryWorld.Folder, "VERSION"));
        Assert.Equal(version, string.Concat(text.Where(c => c is not (' ' or '\r' or '\n'))));
        Assert.Matches(@"^\S+\r?\n$", text);
    }

    [Fact]
    public void Every_script_that_runs_git_sources_git_sh_and_none_puts_a_credential_on_a_command_line_or_in_a_file()
    {
        foreach (var script in Scripts().Where(s => Path.GetFileName(s) != "git.sh"))
        {
            var text = File.ReadAllText(script);
            if (Regex.IsMatch(text, @"(?m)^[^#]*\bgit\s+(-C|ls-remote|init|fetch|push|commit)\b"))
                Assert.Matches(@"(?m)^\s*(\.|source)\s+""\$\(dirname ""\$0""\)/git\.sh""", text);
        }

        foreach (var script in Scripts())
        {
            foreach (var line in File.ReadAllLines(script).Where(l => !l.TrimStart().StartsWith('#')))
            {
                Assert.DoesNotMatch(@"\bgit\b[^|;&]*\s-c\s", line);
                Assert.DoesNotMatch(@"\bgit\b[^|;&]*\bconfig\b[^|;&]*(extraheader|credential)", line);
                Assert.DoesNotMatch(@"https?://[^/\s""]*@", line);
            }
        }
    }

    /// <summary>A quote left open (an apostrophe in a <c>${VAR:?message}</c> word opens one) stops every phase at its first line.</summary>
    [Fact]
    public void Every_script_parses_under_bash()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var home = Directory.CreateTempSubdirectory("kronikol-bash-n").FullName;
        var environment = ChildProcess.BaseEnvironment(home, home, Path.Combine(home, "none"));
        environment["PATH"] = string.Join(Path.PathSeparator, ChildProcess.SystemPath());
        foreach (var script in Scripts())
        {
            var result = ChildProcess.Run(BashProbe.Executable!, ["-n", script], environment, home);
            Assert.True(result.ExitCode == 0, $"{Path.GetFileName(script)}: {result.Stderr}");
        }
    }

    [Fact]
    public void The_folder_keeps_its_scripts_LF_on_any_checkout()
    {
        Assert.Contains("*.sh text eol=lf", File.ReadAllLines(Path.Combine(HistoryWorld.Folder, ".gitattributes")));
        Assert.All(Scripts(), script => Assert.DoesNotContain("\r", File.ReadAllText(script), StringComparison.Ordinal));
    }

    // ─── The README ─────────────────────────────────────────────

    private static IReadOnlyList<YamlMappingNode> ReadmeWorkflows()
    {
        var readme = File.ReadAllText(Path.Combine(HistoryWorld.Folder, "README.md")).ReplaceLineEndings("\n");
        return Regex.Matches(readme, @"^```ya?ml\n(.*?)^```", RegexOptions.Singleline | RegexOptions.Multiline)
            .Select(m => ActionDefinition.LoadYaml(m.Groups[1].Value))
            .Where(root => root.Children.ContainsKey(new YamlScalarNode("jobs")))
            .ToList();
    }

    private static IEnumerable<(string Job, YamlMappingNode JobNode, YamlMappingNode Step, string Phase)> Calls(YamlMappingNode workflow)
    {
        foreach (var (name, node) in ((YamlMappingNode)workflow["jobs"]).Children)
        {
            var job = (YamlMappingNode)node;
            if (!job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps))
                continue;
            foreach (var step in ((YamlSequenceNode)steps).Children.Cast<YamlMappingNode>())
            {
                var match = Regex.Match(ActionDefinition.Scalar(step, "uses") ?? "", @"kronikol-history/(read|gate|save|record)(@|$)");
                if (match.Success)
                    yield return (((YamlScalarNode)name).Value!, job, step, match.Groups[1].Value);
            }
        }
    }

    [Fact]
    public void The_readme_workflows_call_each_phase_with_declared_inputs_and_every_required_one()
    {
        var workflows = ReadmeWorkflows();
        Assert.True(workflows.Count >= 2, "the README shows the two-job and the one-job workflow");
        var phases = new HashSet<string>();
        foreach (var (_, _, step, phase) in workflows.SelectMany(Calls))
        {
            phases.Add(phase);
            var declared = Phase(phase).Inputs;
            var passed = step.Children.TryGetValue(new YamlScalarNode("with"), out var with)
                ? ((YamlMappingNode)with).Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList()
                : [];
            Assert.Empty(passed.Where(p => !declared.ContainsKey(p)));
            Assert.Empty(declared.Where(i => i.Value.Required && !passed.Contains(i.Key)).Select(i => i.Key));
        }
        Assert.Equal(Phases.Order(), phases.Order());
    }

    [Fact]
    public void The_job_that_records_can_write_and_a_test_job_of_two_jobs_cannot()
    {
        foreach (var workflow in ReadmeWorkflows())
        {
            var calls = Calls(workflow).ToList();
            var recordJobs = calls.Where(c => c.Phase == "record").Select(c => c.Job).Distinct().ToList();
            foreach (var job in recordJobs)
                Assert.Equal("write", Permission(calls.First(c => c.Job == job).JobNode, workflow, "contents"));

            foreach (var (job, node, _, _) in calls.Where(c => c.Phase is "read" or "save" && !recordJobs.Contains(c.Job)))
                Assert.NotEqual("write", Permission(node, workflow, "contents"));
        }
    }

    private static string? Permission(YamlMappingNode job, YamlMappingNode workflow, string scope)
    {
        foreach (var holder in new[] { job, workflow })
        {
            if (holder.Children.TryGetValue(new YamlScalarNode("permissions"), out var permissions))
                return permissions is YamlMappingNode map ? ActionDefinition.Scalar(map, scope) : ((YamlScalarNode)permissions).Value;
        }
        return null;
    }

    [Fact]
    public void Save_and_record_run_after_failed_tests()
    {
        foreach (var workflow in ReadmeWorkflows())
        {
            foreach (var (job, node, step, phase) in Calls(workflow).Where(c => c.Phase is "save" or "record" or "gate"))
            {
                var condition = ActionDefinition.Scalar(step, "if") ?? "";
                var jobCondition = ActionDefinition.Scalar(node, "if") ?? "";
                var separateJob = phase == "record" && ((YamlMappingNode)node).Children.ContainsKey(new YamlScalarNode("needs"));
                Assert.True(condition.Contains("!cancelled()", StringComparison.Ordinal) || (separateJob && jobCondition.Contains("!cancelled()", StringComparison.Ordinal)),
                    $"{phase} in job {job} would not run after a failed test step");
            }
        }
    }

    [Fact]
    public void No_readme_workflow_has_a_concurrency_group()
    {
        foreach (var workflow in ReadmeWorkflows())
        {
            Assert.False(workflow.Children.ContainsKey(new YamlScalarNode("concurrency")));
            foreach (var (_, job) in ((YamlMappingNode)workflow["jobs"]).Children)
                Assert.False(((YamlMappingNode)job).Children.ContainsKey(new YamlScalarNode("concurrency")));
        }
    }

    [Fact]
    public void The_live_lane_calls_every_phase_by_path_as_declared_and_writes_only_its_scratch_branches()
    {
        // plans/HISTORY_ACTION_PLAN.md S3b: the lane proves the folder on github.com's runners, so it runs when the
        // folder changes, calls each phase as the phase declares its inputs, and never touches kronikol-history.
        var text = File.ReadAllText(Path.Combine(HistoryWorld.RepoRoot, ".github", "workflows", "history-action.yml"));
        Assert.Contains("- 'templates/github-actions/kronikol-history/**'", text);
        var phases = new HashSet<string>();
        foreach (var (_, _, step, phase) in Calls(ActionDefinition.LoadYaml(text)))
        {
            phases.Add(phase);
            Assert.Equal($"./templates/github-actions/kronikol-history/{phase}", ActionDefinition.Scalar(step, "uses"));
            var with = step.Children.TryGetValue(new YamlScalarNode("with"), out var node) ? (YamlMappingNode)node : new YamlMappingNode();
            var passed = with.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList();
            var declared = Phase(phase).Inputs;
            Assert.Empty(passed.Where(p => !declared.ContainsKey(p)));
            Assert.Empty(declared.Where(i => i.Value.Required && !passed.Contains(i.Key)).Select(i => i.Key));
            if (phase is "read" or "record")
                Assert.Matches(@"^\$\{\{ env\.LANE(_ARTIFACTS)? \}\}$", ActionDefinition.Scalar(with, "branch") ?? "(the default, kronikol-history)");
        }
        Assert.Equal(Phases.Order(), phases.Order());
    }

    [Fact]
    public void The_lanes_racers_give_a_real_fragment_a_run_id_of_their_own()
    {
        // S3b's race is six runs only if each racer's copy of a leg's fragment carries a run id of its own. A pattern
        // that missed the writer's layout would fail the step on github.com, which no other fact runs.
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        var workflow = ActionDefinition.LoadYaml(File.ReadAllText(Path.Combine(HistoryWorld.RepoRoot, ".github", "workflows", "history-action.yml")));
        var steps = (YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)workflow["jobs"])["race"])["steps"];
        var step = steps.Children.Cast<YamlMappingNode>().Single(s => ActionDefinition.Scalar(s, "name") == "A fragment of a run of its own");
        var root = Directory.CreateTempSubdirectory("kh-racer-").FullName;
        try
        {
            var temp = Path.Combine(root, "temp");
            var original = File.ReadAllText(HistoryFixtures.Fragment(Path.Combine(temp, "legs", "kronikol-history-test-1-0a1b2c3d", "examples", "Reports"), "Suite", "gh:123:1", "PF"));
            var script = Path.Combine(root, "step.sh");
            File.WriteAllText(script, ActionDefinition.Scalar(step, "run"));
            var gitConfig = Path.Combine(root, "gitconfig");
            File.WriteAllText(gitConfig, "");
            var environment = ChildProcess.BaseEnvironment(root, temp, gitConfig);
            environment["PATH"] = string.Join(Path.PathSeparator, ChildProcess.SystemPath());
            environment["RUNNER_TEMP"] = ChildProcess.Slashes(temp);
            environment["RACER"] = "4";
            environment["RUN"] = "987";
            environment["ATTEMPT"] = "2";
            var workspace = Directory.CreateDirectory(Path.Combine(root, "ws")).FullName;

            var result = ChildProcess.Run(BashProbe.Executable!, ["--noprofile", "--norc", "-e", ChildProcess.Slashes(script)], environment, workspace);

            Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}: {result.Stdout}{result.Stderr}");
            var written = File.ReadAllText(Path.Combine(workspace, "racer", "Reports", "History.run.json"));
            Assert.Equal("gh:98704:2", RunId(written));
            Assert.Equal(original.Replace("\"gh:123:1\"", "\"gh:98704:2\""), written);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string? RunId(string fragment)
    {
        using var document = System.Text.Json.JsonDocument.Parse(fragment);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Object && property.Value.TryGetProperty("t", out var kind) && kind.GetString() == "run")
                return property.Value.GetProperty("id").GetString();
        }
        return null;
    }

    [Fact]
    public void The_dogfood_runs_every_phase_by_path_with_the_tool_built_from_source()
    {
        // plans/HISTORY_ACTION_PLAN.md S4: CI Summary Preview records its runs through the action, on the branch its
        // old fold job wrote, with the tool built from the commit under test; only the history job can push.
        var text = File.ReadAllText(Path.Combine(HistoryWorld.RepoRoot, ".github", "workflows", "ci-summary-preview.yml"));
        var workflow = ActionDefinition.LoadYaml(text);
        var phases = new HashSet<string>();
        foreach (var (job, node, step, phase) in Calls(workflow))
        {
            phases.Add(phase);
            Assert.Equal($"./templates/github-actions/kronikol-history/{phase}", ActionDefinition.Scalar(step, "uses"));
            var with = step.Children.TryGetValue(new YamlScalarNode("with"), out var inputs) ? (YamlMappingNode)inputs : new YamlMappingNode();
            var passed = with.Children.Keys.Select(k => ((YamlScalarNode)k).Value!).ToList();
            var declared = Phase(phase).Inputs;
            Assert.Empty(passed.Where(p => !declared.ContainsKey(p)));
            Assert.Empty(declared.Where(i => i.Value.Required && !passed.Contains(i.Key)).Select(i => i.Key));
            Assert.DoesNotContain("branch", passed);
            if (phase is "gate" or "record")
                Assert.StartsWith("dotnet run --no-build --project src/Kronikol.Tool/Kronikol.Tool.csproj ", ActionDefinition.Scalar(with, "tool-command") ?? "(none: the released tool)");
            if (phase != "read")
                Assert.Contains("!cancelled()", (ActionDefinition.Scalar(step, "if") ?? "") + (phase == "record" ? ActionDefinition.Scalar(node, "if") : ""));
            Assert.Equal(phase == "record" ? "write" : "read", Permission(node, workflow, "contents"));
        }
        Assert.Equal(Phases.Order(), phases.Order());
        Assert.DoesNotContain("git worktree", text);
    }
}
