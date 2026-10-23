namespace Kronikol.Tests.Tool;

/// <summary>
/// From 5.0.0 a run writes <c>TestRunReport.krkl</c> unless <c>CompressTestRunReport</c> is off
/// (<c>plans/REPORT_FORMAT_PLAN.md</c> 6.3), so the text an agent reads before it opens anything names that file first:
/// the block <c>kronikol init-agents</c> writes into a repository's <c>CLAUDE.md</c> and <c>AGENTS.md</c>, and the
/// skill. The text a run writes beside its report already names the file that run wrote (<c>NamingTheDataFile</c>).
/// </summary>
public class AgentTextNamesTheDataFileTests
{
    private static string Read(params string[] parts) => ReportHtml.Read(Path.Combine([BuiltTool.RepoRoot, .. parts]));

    [Fact]
    public void The_init_agents_block_forbids_opening_the_krkl_and_the_json()
    {
        var block = Read("templates", "agents", "CLAUDE.md");

        Assert.Contains("**Never open `TestRunReport.krkl`, `TestRunReport.json`,", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_skill_names_the_krkl_as_the_file_a_run_writes()
    {
        var skill = Read("templates", "skills", "kronikol-test-debugging", "SKILL.md");

        Assert.Contains("(TestRunReport.krkl, or TestRunReport.json)", skill, StringComparison.Ordinal);
        Assert.Contains("otherwise open `TestRunReport.krkl`, `TestRunReport.json`, `TestRunReport.html`, or a diagram.**", skill,
            StringComparison.Ordinal);
        Assert.Contains("kronikol query summary .logs/kronikol/TestRunReport.krkl", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("query summary .logs/kronikol/TestRunReport.json", skill, StringComparison.Ordinal);
    }
}
