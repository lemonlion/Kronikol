using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The public builders read the CI the process runs on (plans/CI_SUMMARY_AUDIENCE_PLAN.md Q11): called from a CI job,
/// they print what the run prints. A fact that compared them with the internal builder fed <c>FromProcess()</c> could
/// fail only on a GitHub runner, so this one sets the run's variables itself, in the collection whose classes set
/// process-wide CI variables (<see cref="CiDebugSectionTests"/>), and puts every one back.
/// </summary>
[Collection("DiagramsFetcher")]
public class CiSummaryPublicBuilderTests : IDisposable
{
    // What CiReader reads: the provider (GITHUB_ACTIONS, TF_BUILD) and the run's GitHub variables.
    private static readonly string[] Variables =
    [
        "GITHUB_ACTIONS", "GITHUB_SERVER_URL", "GITHUB_REPOSITORY", "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_WORKSPACE",
        "GITHUB_OUTPUT", "TF_BUILD"
    ];

    private readonly Dictionary<string, string?> _previous = Variables.ToDictionary(v => v, Environment.GetEnvironmentVariable);

    public void Dispose()
    {
        foreach (var (name, value) in _previous)
            Environment.SetEnvironmentVariable(name, value);
        GC.SuppressFinalize(this);
    }

    private static RunSummary Failing() =>
        new("/home/runner/work/shop/shop/tests/Reports",
            [new RunSummaryFile("TestRunReport.html", 1_000), new RunSummaryFile("TestRunReport.json", 2_000), new RunSummaryFile("Failures.md", 300)],
            ScenarioCount: 3, Failures: [new RunSummaryFailure("0000000000000001", "Checkout", "Pay")], AgentInstructionsWritten: false);

    [Fact]
    public void The_public_builders_read_the_process_s_ci()
    {
        foreach (var name in Variables)
            Environment.SetEnvironmentVariable(name, null);
        Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
        Environment.SetEnvironmentVariable("GITHUB_SERVER_URL", "https://github.com");
        Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", "acme/shop");
        Environment.SetEnvironmentVariable("GITHUB_RUN_ID", "424242");
        Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", "/home/runner/work/shop/shop");

        var section = RunSummaryConsoleWriter.BuildCiSummarySection(Failing());
        var notice = RunSummaryConsoleWriter.BuildGitHubNotice(Failing());
        var annotations = new List<string>();
        RunSummaryConsoleWriter.Write(Failing(), CiEnvironment.GitHubActions, _ => { }, annotations.Add);

        Assert.Contains("test -d kronikol-424242 || gh run download 424242 --repo acme/shop --dir kronikol-424242", section);
        Assert.Contains("This job wrote them to `tests/Reports`", section);
        Assert.NotNull(notice);
        Assert.EndsWith("agents: gh run download 424242 --repo acme/shop --dir kronikol-424242, then kronikol query failures kronikol-424242)", notice);
        Assert.Equal([notice], annotations);

        // Off CI, the same builders name the folder: the reader is on this machine.
        foreach (var name in Variables)
            Environment.SetEnvironmentVariable(name, null);
        Assert.Contains("kronikol query failures /home/runner/work/shop/shop/tests/Reports", RunSummaryConsoleWriter.BuildCiSummarySection(Failing()));
        Assert.DoesNotContain("gh run download", RunSummaryConsoleWriter.BuildCiSummarySection(Failing()));
    }
}
