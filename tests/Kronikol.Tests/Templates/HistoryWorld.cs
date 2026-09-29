using System.Text.Json;
using Kronikol.History;

namespace Kronikol.Tests.Templates;

/// <summary>
/// A world for the history action's facts (plans/HISTORY_ACTION_PLAN.harness/common.sh's <c>new_world</c>, made into
/// C#): an origin holding <c>octo/app</c>, reached as a file or over HTTP; the four phases in
/// <c>templates/github-actions/kronikol-history/</c>; the tool built with these tests, which <c>record</c> and
/// <c>gate</c> run through their <c>tool-command</c> input; and the jobs of each workflow run, which share that run's
/// artifacts as a real run's jobs do.
/// </summary>
internal sealed class HistoryWorld : IDisposable
{
    public const string Branch = "kronikol-history";
    public const string Sha = "0123456789abcdef0123456789abcdef01234567";

    private readonly Dictionary<long, ArtifactStore> _runs = [];

    public HistoryWorld(bool http = false)
    {
        Root = Directory.CreateTempSubdirectory("kronikol-history-action").FullName;
        Origin = new BareOrigin(Path.Combine(Root, "origin"));
        Http = http ? new HttpOrigin(Origin) : null;
    }

    public string Root { get; }
    public BareOrigin Origin { get; }
    public HttpOrigin? Http { get; }

    /// <summary>What <c>github.server_url</c> is for this world's origin.</summary>
    public string ServerUrl => Http?.ServerUrl ?? Origin.ServerUrl;

    public static string RepoRoot { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>The folder the four phases live in.</summary>
    public static string Folder => Path.Combine(RepoRoot, "templates", "github-actions", "kronikol-history");

    public static void SkipWithoutBashOrGit()
    {
        Assert.SkipWhen(!BashProbe.IsAvailable, "bash not available");
        Assert.SkipWhen(!GitProbe.IsAvailable, "git 2.31 or later not available");
    }

    /// <summary>The artifacts of one workflow run, shared by its jobs and its re-run attempts.</summary>
    public ArtifactStore Artifacts(long run)
    {
        lock (_runs)
        {
            if (!_runs.TryGetValue(run, out var store))
                _runs[run] = store = new ArtifactStore(Path.Combine(Root, "artifacts", run.ToString()));
            return store;
        }
    }

    /// <summary>A job of workflow run <paramref name="run"/>, with the token that reads and writes the origin.</summary>
    public RunnerJob Job(long run, int attempt = 1, string job = "test", string eventName = "push", string refName = "main")
    {
        var runner = new RunnerJob(Path.Combine(Root, "jobs", $"{run}-{attempt}-{job}-{Guid.NewGuid().ToString("N")[..6]}"), Artifacts(run), job);
        runner.Github["server_url"] = ServerUrl;
        runner.Github["repository"] = BareOrigin.Repository;
        runner.Github["run_id"] = run.ToString();
        runner.Github["run_attempt"] = attempt.ToString();
        runner.Github["sha"] = Sha;
        runner.Github["event_name"] = eventName;
        runner.Github["ref_name"] = refName;
        runner.Github["token"] = Http is null ? "" : HttpOrigin.Token;
        return runner;
    }

    public static ActionRun Read(RunnerJob job, Dictionary<string, string>? with = null) =>
        CompositeActionRunner.Run(job, Path.Combine(Folder, "read"), with);

    public static ActionRun Save(RunnerJob job, Dictionary<string, string>? with = null) =>
        CompositeActionRunner.Run(job, Path.Combine(Folder, "save"), with);

    /// <summary>Record, running the tool built with these tests unless <paramref name="with"/> names another.</summary>
    public static ActionRun Record(RunnerJob job, Dictionary<string, string>? with = null, string? folder = null) =>
        CompositeActionRunner.Run(job, Path.Combine(folder ?? Folder, "record"), WithTool(with));

    /// <summary>Gate, running the tool built with these tests unless <paramref name="with"/> names another.</summary>
    public static ActionRun Gate(RunnerJob job, Dictionary<string, string> with) =>
        CompositeActionRunner.Run(job, Path.Combine(Folder, "gate"), WithTool(with));

    private static Dictionary<string, string> WithTool(Dictionary<string, string>? with)
    {
        var given = new Dictionary<string, string>(with ?? [], StringComparer.Ordinal);
        given.TryAdd("tool-command", ChildProcess.ToolCommand);
        return given;
    }

    /// <summary>What the data branch's ledger holds now.</summary>
    public LedgerView Ledger() => LedgerView.Parse(Origin.Show(Branch, "history.jsonl") ?? "");

    public void Dispose()
    {
        Http?.Dispose();
        try { BareOrigin.DeleteTree(Root); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A ledger's lines by kind, the runs by id, and the roster lines written more than once.</summary>
internal sealed record LedgerView(int Headers, IReadOnlyList<JsonElement> Runs, int RosterLines, int DuplicateRosterLines, int ShapesLines)
{
    public static LedgerView Parse(string text)
    {
        var runs = new List<JsonElement>();
        var rosters = new List<string>();
        int headers = 0, shapes = 0;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var element = JsonDocument.Parse(line).RootElement.Clone();
            switch (element.GetProperty("t").GetString())
            {
                case "header": headers++; break;
                case "run": runs.Add(element); break;
                case "roster": rosters.Add(element.GetProperty("hash").GetString()!); break;
                case "shapes": shapes++; break;
            }
        }
        return new LedgerView(headers, runs, rosters.Count, rosters.Count - rosters.Distinct().Count(), shapes);
    }

    /// <summary>Each run line as "id suite", in ledger order.</summary>
    public IReadOnlyList<string> RunKeys => Runs.Select(r => $"{r.GetProperty("id").GetString()} {r.GetProperty("suite").GetString()}").ToList();
}

/// <summary>The files a test run leaves for the history action: fragments, and the report the gate reads.</summary>
internal static class HistoryFixtures
{
    public static string[] Ids(int count) => Enumerable.Range(0, count).Select(i => $"{i + 1:x4}aaaabbbbcccc").ToArray();

    public static HistoryRoster Roster(string suite, params string[] ids) =>
        HistoryRoster.Create(suite, ids.Select(id => new HistoryRosterEntry(id, "Scenario " + id, "Feature", null)).ToArray());

    public static HistoryRun Run(HistoryRoster roster, string id, string results, DateTimeOffset? at = null, string branch = "main") => new()
    {
        Id = id, Suite = roster.Suite, Partial = null, At = at ?? new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero),
        Branch = branch, Commit = "0123456", Provider = "GitHubActions", Url = null, Shards = 1, RosterHash = roster.Hash,
        Results = results, Attempts = new string('-', results.Length), Durations = results.Select(_ => (int?)100).ToArray(),
        Calls = results.Select(_ => 1).ToArray(), ShapeSet = results.Select(_ => "aaaaaaaa").ToArray(), ShapeOrdered = results.Select(_ => "aaaaaaaa").ToArray(),
        Errors = results.Select(r => r == 'F' ? "e1" : null).ToArray(),
        ErrorText = results.Contains('F') ? new Dictionary<string, string> { ["e1"] = "boom" } : new Dictionary<string, string>()
    };

    /// <summary>Writes <c>History.run.json</c> into <paramref name="directory"/>: one run of the suite, a scenario per result.</summary>
    public static string Fragment(string directory, string suite, string runId, string results, string[]? ids = null, DateTimeOffset? at = null, string branch = "main")
    {
        var roster = Roster(suite, ids ?? Ids(results.Length));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, HistoryFormat.FragmentFileName);
        File.WriteAllText(path, HistoryFragment.Write(roster, Run(roster, runId, results, at, branch), "3.33.0"));
        return path;
    }

    /// <summary>
    /// Writes <c>TestRunReport.json</c> into <paramref name="directory"/>, with only what the gate reads (the shape
    /// <c>HistoryGateTests</c> writes by hand): a scenario per result, ids from <see cref="Ids"/>.
    /// </summary>
    public static string Report(string directory, string suite, long run, string results, string branch = "main")
    {
        Directory.CreateDirectory(directory);
        var ids = Ids(results.Length);
        var scenarios = string.Join(",\n", results.Select((r, i) =>
            $$"""{ "id": "t{{i}}", "stableId": "{{ids[i]}}", "name": "Scenario {{ids[i]}}", "result": "{{(r == 'F' ? "Failed" : "Passed")}}", "durationSeconds": 0.1, "errorMessage": {{(r == 'F' ? "\"boom\"" : "null")}}, "labels": [], "categories": [], "steps": [], "httpInteractions": [] }"""));
        var path = Path.Combine(directory, "TestRunReport.json");
        File.WriteAllText(path, $$"""
            {
              "kronikolVersion": "3.33.0", "formatVersion": 1, "suite": "{{suite}}",
              "startTime": "2026-09-29T10:00:00Z", "endTime": "2026-09-29T10:05:00Z",
              "ciMetadata": { "provider": "GitHubActions", "buildNumber": "{{run}}", "branch": "{{branch}}", "commitSha": "0123456", "pipelineUrl": null, "repository": "octo/app", "runId": "{{run}}", "runAttempt": "1" },
              "features": [ { "name": "Feature", "labels": [], "scenarios": [
            {{scenarios}}
              ] } ]
            }
            """);
        return path;
    }

    /// <summary>A ledger at <paramref name="path"/> holding these green-or-red runs of one suite, oldest first, runs gh:1:1 up.</summary>
    public static void Ledger(string path, string suite, params string[] results)
    {
        var roster = Roster(suite, Ids(results[0].Length));
        for (var i = 0; i < results.Length; i++)
            HistoryLedgerWriter.Append(path, roster, Run(roster, $"gh:{i + 1}:1", results[i], new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero).AddHours(i)), "3.33.0");
    }
}
