using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// Two things write <c>CLAUDE.md</c> and <c>AGENTS.md</c>, and only one of them was careful.
///
/// <para><c>kronikol init-agents</c> writes a <b>marker-delimited block</b> into a file a human owns, so
/// re-running it replaces its own block and leaves everything else alone — the command's help promises
/// exactly that. Report generation writes the same two file names with a plain overwrite. Where the two
/// land in the same directory — a reports folder configured at the repository root, or `init-agents`
/// pointed at a reports folder — the run silently destroys whatever the human had written.</para>
///
/// <para>The second half is worse than the first. Once generation has replaced the file with its own
/// body, that body has no markers, so a later <c>init-agents</c> appends its block to it rather than
/// replacing anything — and the repository's instruction file now carries one run's report notes as
/// though they were the repository's own standing prose.</para>
/// </summary>
[Collection("DiagramsFetcher")]
public class AgentInstructionsOverwriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kronikol-agents-" + Guid.NewGuid().ToString("N"));

    public AgentInstructionsOverwriteTests()
    {
        Directory.CreateDirectory(_dir);
        DefaultDiagramsFetcher.Reset();
    }

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private void Run()
    {
        var testId = "agents-" + Guid.NewGuid().ToString("N");
        RequestResponseLogger.LogPair("Pay", testId, HttpMethod.Post, new Uri("http://payments/charge"), "payments", "Test");
        ReportGenerator.CreateStandardReportsWithDiagrams(
            [
                new Feature
                {
                    DisplayName = "Checkout",
                    Scenarios = [new Scenario { Id = testId, DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "Assert.Equal() Failure" }]
                }
            ],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow,
            new ReportConfigurationOptions
            {
                ReportsFolderPath = _dir,
                InternalFlowTracking = false,
                GenerateComponentDiagram = false,
                GenerateSpecificationsReport = false,
                GenerateSpecificationsData = false,
            });
    }

    [Fact]
    public void Generation_keeps_what_somebody_else_wrote_in_the_same_file()
    {
        var path = Path.Combine(_dir, "CLAUDE.md");
        File.WriteAllText(path,
            "# Our repository\n\nRun the acceptance suite before pushing. Ask Priya before touching billing.\n");

        Run();

        var after = File.ReadAllText(path);

        // Kronikol's own instructions are there...
        Assert.Contains("kronikol query", after, StringComparison.Ordinal);
        // ...and so is everything that was there before.
        Assert.Contains("Ask Priya before touching billing.", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Generation_replaces_its_own_block_rather_than_appending_a_second_one()
    {
        var path = Path.Combine(_dir, "CLAUDE.md");
        File.WriteAllText(path, "# Our repository\n\nAsk Priya before touching billing.\n");

        Run();
        Run();

        var after = File.ReadAllText(path);

        // Twice through, one block. Otherwise every run of the suite grows the file by a copy.
        var occurrences = after.Split("<!-- kronikol:begin -->").Length - 1;
        Assert.Equal(1, occurrences);
        Assert.Contains("Ask Priya before touching billing.", after, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fresh_directory_still_gets_both_files()
    {
        // Non-vacuity: the discovery loop depends on these existing, so care about existing content must
        // not turn into refusing to write.
        Run();

        var claude = Path.Combine(_dir, "CLAUDE.md");
        var agents = Path.Combine(_dir, "AGENTS.md");
        Assert.True(File.Exists(claude));
        Assert.True(File.Exists(agents));
        Assert.Equal(File.ReadAllText(claude), File.ReadAllText(agents));
        Assert.Contains("Failures.md", File.ReadAllText(claude), StringComparison.Ordinal);
    }
}
