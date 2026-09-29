using System.Text;
using System.Text.RegularExpressions;

namespace Kronikol.Tests.Templates;

/// <summary>
/// Runs one composite <c>action.yml</c> the way GitHub's runner does (plans/HISTORY_ACTION_PLAN.md §5.2):
/// <list type="bullet">
/// <item>Inputs take their declared defaults, which may be expressions, and a call that passes an undeclared input or
/// leaves out a required one is refused.</item>
/// <item>Every <c>${{ }}</c> is evaluated by <see cref="WorkflowExpression"/>, which refuses anything outside the subset
/// the template actions may use.</item>
/// <item>A <c>run:</c> step must declare <c>shell: bash</c>, and its body may hold no <c>${{ }}</c>: inputs reach a
/// script through <c>env:</c>, never written into it. It runs as the runner runs it,
/// <c>bash --noprofile --norc -eo pipefail &lt;file&gt;</c>, in the workspace, with <c>GITHUB_ENV</c>,
/// <c>GITHUB_OUTPUT</c>, <c>GITHUB_PATH</c> and <c>GITHUB_STEP_SUMMARY</c> read back between steps and the workflow
/// commands on its output (<c>::warning::</c>, <c>::add-mask::</c>, <c>::stop-commands::</c>…) applied.</item>
/// <item><c>actions/upload-artifact</c> and <c>actions/download-artifact</c> run against the job's
/// <see cref="ArtifactStore"/>, and <c>actions/setup-dotnet</c> is a step that does nothing. Any other action is
/// refused.</item>
/// <item><b>The environment is built, never inherited</b> (<see cref="ChildProcess.BaseEnvironment"/>).</item>
/// </list>
/// A step's status functions read the calling job too: <see cref="RunnerJob.Failed"/> stands for a test step that
/// failed before the action was called, as the action's own failed steps do.
/// </summary>
internal static class CompositeActionRunner
{
    private static readonly Regex Command = new(@"^::([a-z-]+)(?: [^:]*)?::(.*)$");

    public static ActionRun Run(RunnerJob job, string actionDirectory, IReadOnlyDictionary<string, string>? with = null)
    {
        var action = ActionDefinition.Load(actionDirectory);
        var given = with ?? new Dictionary<string, string>();

        var undeclared = given.Keys.Where(k => !action.Inputs.ContainsKey(k)).ToList();
        if (undeclared.Count > 0)
            throw new ArgumentException($"{Path.GetFileName(actionDirectory)} declares no input {string.Join(", ", undeclared)}");

        var steps = new Dictionary<string, StepState>(StringComparer.Ordinal);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var log = new StringBuilder();
        var annotations = new List<Annotation>();
        var masks = new List<string>();
        var failed = false;

        object? Lookup(string path)
        {
            var parts = path.Split('.');
            var rest = string.Join('.', parts.Skip(1));
            return parts[0] switch
            {
                "inputs" => inputs.TryGetValue(rest, out var input) ? input : null,
                "github" => rest == "action_path" ? actionDirectory : job.Github.GetValueOrDefault(rest),
                "runner" => rest switch { "temp" => job.Temp, "os" => RunnerJob.Os, _ => null },
                "env" => job.Env.GetValueOrDefault(rest),
                "strategy" => job.Strategy.GetValueOrDefault(rest),
                "steps" when parts.Length >= 3 && steps.TryGetValue(parts[1], out var step) => parts[2] switch
                {
                    "outputs" when parts.Length == 4 => step.Outputs.GetValueOrDefault(parts[3]),
                    "outcome" or "conclusion" => step.Outcome,
                    _ => null
                },
                _ => null
            };
        }

        foreach (var (name, (required, defaultValue)) in action.Inputs)
        {
            if (given.TryGetValue(name, out var value))
                inputs[name] = value;
            else if (required)
                throw new ArgumentException($"{Path.GetFileName(actionDirectory)} requires the input {name}");
            else
                inputs[name] = WorkflowExpression.Interpolate(defaultValue, Lookup);
        }

        var index = 0;
        foreach (var step in action.Steps)
        {
            var id = step.Id ?? $"__step{index}";
            index++;
            var status = job.Cancelled ? "cancelled" : job.Failed || failed ? "failure" : "success";
            if (!WorkflowExpression.Condition(step.If, Lookup, status))
            {
                steps[id] = new StepState("skipped", new Dictionary<string, string>());
                continue;
            }

            var env = step.Env.ToDictionary(kv => kv.Key, kv => WorkflowExpression.Interpolate(kv.Value, Lookup, status), StringComparer.Ordinal);
            StepState state;
            if (step.Run is not null)
            {
                state = RunScript(job, action, step, env, log, annotations, masks);
            }
            else if (step.Uses is not null)
            {
                var stepWith = step.With.ToDictionary(kv => kv.Key, kv => WorkflowExpression.Interpolate(kv.Value, Lookup, status), StringComparer.Ordinal);
                state = RunUses(job, step.Uses, stepWith, log, annotations);
            }
            else
            {
                throw new NotSupportedException($"step {id} has neither run nor uses");
            }

            steps[id] = state;
            if (state.Outcome == "failure" && !step.ContinueOnError)
                failed = true;
        }

        var outputs = action.Outputs.ToDictionary(kv => kv.Key, kv => WorkflowExpression.Interpolate(kv.Value, Lookup), StringComparer.Ordinal);
        job.Annotations.AddRange(annotations);
        job.Log.Append(log);
        return new ActionRun(failed ? "failure" : "success", outputs, steps.ToDictionary(kv => kv.Key, kv => kv.Value.Outcome, StringComparer.Ordinal),
            log.ToString(), annotations, masks);
    }

    private sealed record StepState(string Outcome, IReadOnlyDictionary<string, string> Outputs);

    private static StepState RunScript(RunnerJob job, ActionDefinition action, ActionStep step, Dictionary<string, string> stepEnv,
        StringBuilder log, List<Annotation> annotations, List<string> masks)
    {
        if (step.Shell != "bash")
            throw new NotSupportedException($"a run step of {Path.GetFileName(action.Directory)} declares shell '{step.Shell}'; the template actions run bash everywhere");
        if (step.Run!.Contains("${{", StringComparison.Ordinal))
            throw new NotSupportedException($"a run step of {Path.GetFileName(action.Directory)} writes an expression into its script; inputs reach a script through env:");
        if (!BashProbe.IsAvailable)
            throw new InvalidOperationException("no bash");

        var commands = Path.Combine(job.Temp, "_runner_file_commands");
        Directory.CreateDirectory(commands);
        var suffix = Guid.NewGuid().ToString("N");
        var files = new Dictionary<string, string>
        {
            ["GITHUB_ENV"] = Path.Combine(commands, "set_env_" + suffix),
            ["GITHUB_OUTPUT"] = Path.Combine(commands, "set_output_" + suffix),
            ["GITHUB_PATH"] = Path.Combine(commands, "add_path_" + suffix),
            ["GITHUB_STEP_SUMMARY"] = Path.Combine(commands, "step_summary_" + suffix)
        };
        foreach (var file in files.Values)
            File.WriteAllText(file, "");

        var script = Path.Combine(job.Temp, suffix + ".sh");
        File.WriteAllText(script, step.Run.ReplaceLineEndings("\n"), new UTF8Encoding(false));

        var environment = ChildProcess.BaseEnvironment(job.Home, job.OsTemp, job.GitConfigGlobal ?? job.EmptyGitConfig);
        environment["PATH"] = string.Join(Path.PathSeparator, Enumerable.Reverse(job.AddedPath).Concat(job.BinDirectories).Concat(ChildProcess.SystemPath()));
        environment["CI"] = "true";
        environment["GITHUB_ACTIONS"] = "true";
        environment["RUNNER_OS"] = RunnerJob.Os;
        environment["RUNNER_TEMP"] = job.Temp;
        environment["GITHUB_ACTION_PATH"] = action.Directory;
        environment["GITHUB_WORKSPACE"] = job.Workspace;
        foreach (var (variable, key) in RunnerJob.GithubVariables)
            environment[variable] = job.Github.GetValueOrDefault(key) ?? "";
        foreach (var (name, path) in files)
            environment[name] = path;
        foreach (var (name, value) in job.Env)
            environment[name] = value;
        foreach (var (name, value) in stepEnv)
            environment[name] = value;

        var result = ChildProcess.Run(BashProbe.Executable!, ["--noprofile", "--norc", "-eo", "pipefail", script], environment, job.Workspace, timeout: TimeSpan.FromMinutes(5));
        log.Append(result.Stdout).Append(result.Stderr);

        string? stopToken = null;
        foreach (var line in result.Stdout.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (stopToken is not null)
            {
                if (line == $"::{stopToken}::")
                    stopToken = null;
                continue;
            }

            var match = Command.Match(line);
            if (!match.Success)
                continue;
            var data = Unescape(match.Groups[2].Value);
            switch (match.Groups[1].Value)
            {
                case "add-mask": masks.Add(data); break;
                case "stop-commands": stopToken = match.Groups[2].Value; break;
                case "warning" or "error" or "notice": annotations.Add(new Annotation(match.Groups[1].Value, data)); break;
            }
        }

        foreach (var (name, value) in ReadKeyValues(files["GITHUB_ENV"]))
            job.Env[name] = value;
        job.AddedPath.AddRange(File.ReadAllLines(files["GITHUB_PATH"]).Where(l => l.Trim().Length > 0));
        job.Summary.Append(File.ReadAllText(files["GITHUB_STEP_SUMMARY"]));
        var outputs = ReadKeyValues(files["GITHUB_OUTPUT"]);

        return new StepState(result.ExitCode == 0 ? "success" : "failure", outputs);
    }

    private static StepState RunUses(RunnerJob job, string uses, Dictionary<string, string> with, StringBuilder log, List<Annotation> annotations)
    {
        var name = uses.Split('@')[0];
        string Full(string path) => Path.IsPathRooted(path) ? path : Path.Combine(job.Workspace, path);

        switch (name)
        {
            case "actions/setup-dotnet":
                return new StepState("success", new Dictionary<string, string>());

            case "actions/upload-artifact":
            {
                var paths = with["path"].Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).Select(Full).ToList();
                var failure = job.Artifacts.Upload(
                    with["name"], paths,
                    with.GetValueOrDefault("include-hidden-files") == "true",
                    with.GetValueOrDefault("if-no-files-found") is { Length: > 0 } ifNone ? ifNone : "warn",
                    with.GetValueOrDefault("overwrite") == "true",
                    out var warning);
                if (warning is not null)
                    annotations.Add(new Annotation("warning", warning));
                if (failure is not null)
                {
                    annotations.Add(new Annotation("error", failure));
                    log.AppendLine(failure);
                    return new StepState("failure", new Dictionary<string, string>());
                }

                var uploaded = warning is null && job.Artifacts.Names.Contains(with["name"], StringComparer.Ordinal);
                return new StepState("success", uploaded
                    ? new Dictionary<string, string> { ["artifact-id"] = (job.Artifacts.Names.ToList().IndexOf(with["name"]) + 1).ToString() }
                    : new Dictionary<string, string>());
            }

            case "actions/download-artifact":
            {
                var destination = Full(with.GetValueOrDefault("path") is { Length: > 0 } path ? path : ".");
                var pattern = with.GetValueOrDefault("pattern") is { Length: > 0 } p ? p : with.GetValueOrDefault("name") ?? "*";
                var names = job.Artifacts.Download(pattern, destination, with.GetValueOrDefault("merge-multiple") == "true");
                log.AppendLine($"downloaded {names.Count} artifact(s) matching {pattern}");
                return new StepState("success", new Dictionary<string, string> { ["download-path"] = destination });
            }

            default:
                throw new NotSupportedException($"the runner does not run {uses}");
        }
    }

    /// <summary>The <c>name=value</c> and <c>name&lt;&lt;DELIMITER</c> lines of a file command.</summary>
    internal static Dictionary<string, string> ReadKeyValues(string file)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                continue;
            var heredoc = line.IndexOf("<<", StringComparison.Ordinal);
            var equals = line.IndexOf('=');
            if (heredoc > 0 && (equals < 0 || heredoc < equals))
            {
                var delimiter = line[(heredoc + 2)..];
                var value = new List<string>();
                while (++i < lines.Length && lines[i] != delimiter)
                    value.Add(lines[i]);
                if (i >= lines.Length)
                    throw new FormatException($"{Path.GetFileName(file)}: {line[..heredoc]} has no closing {delimiter}");
                values[line[..heredoc]] = string.Join('\n', value);
            }
            else if (equals > 0)
            {
                values[line[..equals]] = line[(equals + 1)..];
            }
            else
            {
                throw new FormatException($"{Path.GetFileName(file)}: '{line}' is not name=value");
            }
        }

        return values;
    }

    private static string Unescape(string data) =>
        data.Replace("%0D", "\r", StringComparison.OrdinalIgnoreCase).Replace("%0A", "\n", StringComparison.OrdinalIgnoreCase).Replace("%25", "%", StringComparison.Ordinal);
}

/// <summary>A workflow command that annotates the run: <c>warning</c>, <c>error</c> or <c>notice</c>.</summary>
internal sealed record Annotation(string Kind, string Message);

/// <summary>What one action call did.</summary>
internal sealed record ActionRun(
    string Outcome,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyDictionary<string, string> StepOutcomes,
    string Log,
    IReadOnlyList<Annotation> Annotations,
    IReadOnlyList<string> Masks)
{
    public bool Succeeded => Outcome == "success";

    public IReadOnlyList<string> Warnings => Annotations.Where(a => a.Kind == "warning").Select(a => a.Message).ToList();

    public IReadOnlyList<string> Errors => Annotations.Where(a => a.Kind == "error").Select(a => a.Message).ToList();

    public IReadOnlyList<string> Notices => Annotations.Where(a => a.Kind == "notice").Select(a => a.Message).ToList();

    public override string ToString() => $"{Outcome}; outputs {string.Join(", ", Outputs.Select(kv => kv.Key + "=" + kv.Value))}\n{Log}";
}

/// <summary>
/// One job of a workflow run, as its composite actions see it: a workspace, <c>RUNNER_TEMP</c>, a home, the
/// <c>github</c> context, the job's environment (what earlier steps put in <c>GITHUB_ENV</c>) and the run's artifacts.
/// </summary>
internal sealed class RunnerJob
{
    public static readonly IReadOnlyList<(string Variable, string Key)> GithubVariables =
    [
        ("GITHUB_SERVER_URL", "server_url"), ("GITHUB_REPOSITORY", "repository"), ("GITHUB_RUN_ID", "run_id"),
        ("GITHUB_RUN_ATTEMPT", "run_attempt"), ("GITHUB_SHA", "sha"), ("GITHUB_JOB", "job"), ("GITHUB_EVENT_NAME", "event_name"),
        ("GITHUB_REF_NAME", "ref_name"), ("GITHUB_BASE_REF", "base_ref"), ("GITHUB_HEAD_REF", "head_ref")
    ];

    public RunnerJob(string root, ArtifactStore artifacts, string job = "test")
    {
        Root = root;
        Workspace = Path.Combine(root, "workspace");
        Temp = Path.Combine(root, "runner-temp");
        Home = Path.Combine(root, "home");
        OsTemp = Path.Combine(root, "tmp");
        EmptyGitConfig = Path.Combine(root, "empty.gitconfig");
        foreach (var directory in new[] { Workspace, Temp, Home, OsTemp })
            Directory.CreateDirectory(directory);
        File.WriteAllText(EmptyGitConfig, "");
        Artifacts = artifacts;
        Github = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["server_url"] = "https://github.com",
            ["repository"] = "octo/app",
            ["run_id"] = "1",
            ["run_attempt"] = "1",
            ["sha"] = "0123456789abcdef0123456789abcdef01234567",
            ["job"] = job,
            ["event_name"] = "push",
            ["ref_name"] = "main",
            ["base_ref"] = "",
            ["head_ref"] = "",
            ["token"] = "",
            ["workspace"] = Workspace
        };
    }

    public static string Os => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";

    public string Root { get; }
    public string Workspace { get; }
    public string Temp { get; }
    public string Home { get; }
    public string OsTemp { get; }
    public string EmptyGitConfig { get; }
    public ArtifactStore Artifacts { get; }
    public Dictionary<string, string> Github { get; }
    public Dictionary<string, string> Strategy { get; } = new(StringComparer.Ordinal) { ["job-index"] = "0", ["job-total"] = "1" };

    /// <summary>The job's environment: what the workflow set, and what its steps have written to <c>GITHUB_ENV</c>.</summary>
    public Dictionary<string, string> Env { get; } = new(StringComparer.Ordinal);

    /// <summary>What steps have written to <c>GITHUB_PATH</c>, oldest first.</summary>
    public List<string> AddedPath { get; } = [];

    /// <summary>Directories of stubs, put on <c>PATH</c> ahead of git, dotnet and the system.</summary>
    public List<string> BinDirectories { get; } = [];

    /// <summary>The machine's git configuration (<see cref="MachineConfig"/>), or null for none.</summary>
    public string? GitConfigGlobal { get; set; }

    /// <summary>A step of the job failed before the action was called, as a test step does.</summary>
    public bool Failed { get; set; }

    public bool Cancelled { get; set; }

    public StringBuilder Summary { get; } = new();

    public StringBuilder Log { get; } = new();

    public List<Annotation> Annotations { get; } = [];
}
