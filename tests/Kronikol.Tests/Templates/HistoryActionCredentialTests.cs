using System.Text;

namespace Kronikol.Tests.Templates;

/// <summary>
/// How the history action's git uses its token (plans/HISTORY_ACTION_PLAN.md §4.8, §4.9, F15; AZURE_DEVOPS_PARITY_PLAN.md
/// F13). The token goes in as <c>actions/checkout</c>'s header, through the environment, on a repository nothing else
/// uses. Over the machine's configuration it resets the credential helpers, so a wrong token fails at once and no
/// helper of the machine's is asked, waits for a person or is told to erase what it stores; and it resets the header
/// for the server before setting its own, so a header the machine holds is not sent beside it.
/// </summary>
public class HistoryActionCredentialTests
{
    private static RunnerJob JobWithFragment(HistoryWorld world, long run)
    {
        var job = world.Job(run);
        HistoryFixtures.Fragment(Path.Combine(job.Workspace, "Reports"), "A", $"gh:{run}:1", "PP");
        return job;
    }

    private static HistoryWorld WorldWithABranch()
    {
        var world = new HistoryWorld(http: true);
        Assert.True(HistoryWorld.Record(JobWithFragment(world, 1), new Dictionary<string, string> { ["path"] = "." }).Succeeded);
        return world;
    }

    [Fact]
    public void A_wrong_token_fails_at_once_and_leaves_the_machines_stored_credentials_alone()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = WorldWithABranch();
        var machine = new MachineConfig(Path.Combine(world.Root, "machine")).StoreCredential(world.ServerUrl, HttpOrigin.OtherUser, HttpOrigin.OtherPassword);
        var stored = File.ReadAllText(machine.StoreFile);
        var job = JobWithFragment(world, 2);
        job.GitConfigGlobal = machine.Path;
        job.Github["token"] = "a-token-the-server-does-not-know";
        var before = world.Http!.Requests.Count;

        var read = HistoryWorld.Read(job);
        var record = HistoryWorld.Record(job, new Dictionary<string, string> { ["path"] = "." });

        Assert.True(read.Succeeded, read.ToString());
        Assert.Equal("false", read.Outputs["found"]);
        Assert.False(record.Succeeded);
        // The machine's identity was never used, and its store still holds what it held.
        Assert.DoesNotContain(world.Http.Requests.Skip(before), r => r.Who == HttpOrigin.OtherUser);
        Assert.Equal(stored, File.ReadAllText(machine.StoreFile));
        Assert.Equal(["gh:1:1 A"], world.Ledger().RunKeys);
    }

    [Fact]
    public void A_credential_helper_that_waits_for_a_person_is_never_asked()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = WorldWithABranch();
        var machine = new MachineConfig(Path.Combine(world.Root, "machine")).HelperThatWaits();
        var job = JobWithFragment(world, 2);
        job.GitConfigGlobal = machine.Path;
        job.Github["token"] = "a-token-the-server-does-not-know";

        var read = HistoryWorld.Read(job);
        var record = HistoryWorld.Record(job, new Dictionary<string, string> { ["path"] = "." });

        Assert.True(read.Succeeded, read.ToString());
        Assert.False(record.Succeeded);
        Assert.False(File.Exists(machine.AskedFile), "the machine's credential helper was asked for a credential");
    }

    [Fact]
    public void A_header_the_machine_holds_for_the_server_is_not_sent_beside_the_token()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = WorldWithABranch();
        var machine = new MachineConfig(Path.Combine(world.Root, "machine"))
            .Header(world.ServerUrl, GitOriginTests.Basic(HttpOrigin.OtherUser, HttpOrigin.OtherPassword));
        var job = JobWithFragment(world, 2);
        job.GitConfigGlobal = machine.Path;
        var before = world.Http!.Requests.Count;

        Assert.True(HistoryWorld.Read(job).Succeeded);
        var record = HistoryWorld.Record(job, new Dictionary<string, string> { ["path"] = "." });

        Assert.True(record.Succeeded, record.ToString());
        var requests = world.Http.Requests.Skip(before).ToList();
        Assert.NotEmpty(requests);
        Assert.All(requests, r => Assert.Equal((1, "token"), (r.AuthorizationHeaders, r.Who)));
    }

    [Fact]
    public void The_token_reaches_no_output_no_file_and_no_command_line()
    {
        HistoryWorld.SkipWithoutBashOrGit();
        using var world = new HistoryWorld(http: true);
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + HttpOrigin.Token));
        var secrets = new[] { HttpOrigin.Token, header };
        var trace = Path.Combine(world.Root, "trace2.json");
        var jobs = new List<RunnerJob>();
        var runs = new List<ActionRun>();

        // A first run and a later one, each reading and then recording, so every git call the phases make is traced.
        foreach (var run in new long[] { 1, 2 })
        {
            var job = JobWithFragment(world, run);
            job.Env["GIT_TRACE2_EVENT"] = trace;
            runs.Add(HistoryWorld.Read(job));
            runs.Add(HistoryWorld.Record(job, new Dictionary<string, string> { ["path"] = "." }));
            jobs.Add(job);
        }

        Assert.All(runs, run => Assert.True(run.Succeeded, run.ToString()));
        Assert.Equal(["gh:1:1 A", "gh:2:1 A"], world.Ledger().RunKeys);
        Assert.Contains(runs, run => run.Masks.Contains(header));

        var leakingLines = runs.SelectMany(run => run.Log.Split('\n'))
            .Where(line => !line.StartsWith("::add-mask::", StringComparison.Ordinal) && secrets.Any(s => line.Contains(s, StringComparison.Ordinal)))
            .ToList();
        Assert.Empty(leakingLines);

        var leakingFiles = jobs.SelectMany(job => Directory.EnumerateFiles(job.Temp, "*", SearchOption.AllDirectories))
            .Where(file => secrets.Any(s => File.ReadAllText(file, Encoding.Latin1).Contains(s, StringComparison.Ordinal)))
            .ToList();
        Assert.Empty(leakingFiles);

        var traced = File.ReadAllText(trace);
        Assert.Contains("\"event\":\"start\"", traced, StringComparison.Ordinal);
        Assert.Contains("\"push\"", traced, StringComparison.Ordinal);
        Assert.All(secrets, s => Assert.DoesNotContain(s, traced, StringComparison.Ordinal));
    }
}
