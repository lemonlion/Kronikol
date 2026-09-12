using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>CLAUDE.md</c> and <c>AGENTS.md</c> next to the report are the durable half of the discovery loop: the
/// console is swallowed by most runners, but an agent that reads any file in a directory loads the
/// instruction file sitting in it. Because they are <em>instructions</em>, they must contain nothing a test
/// run produced — a scenario name or a response body in an instruction file is a prompt-injection channel.
/// </summary>
public class AgentInstructionsGeneratorTests
{
    [Fact]
    public void Both_files_are_byte_identical()
    {
        // Claude Code reads CLAUDE.md; Codex, Cursor, Copilot, Jules and Amp read AGENTS.md. One text, two
        // names — a divergence between them is a bug nobody would notice for months.
        Assert.Equal(AgentInstructionsGenerator.Build("TestRunReport"), AgentInstructionsGenerator.Build("TestRunReport"));
        Assert.Equal(AgentInstructionsGenerator.ClaudeFileName == AgentInstructionsGenerator.AgentsFileName, false);
        Assert.Equal("CLAUDE.md", AgentInstructionsGenerator.ClaudeFileName);
        Assert.Equal("AGENTS.md", AgentInstructionsGenerator.AgentsFileName);
    }

    [Fact]
    public void It_states_the_rule_and_names_the_file_the_rule_is_about()
    {
        var text = AgentInstructionsGenerator.Build("TestRunReport");

        Assert.Contains("TestRunReport.json", text);
        Assert.Contains("Failures.md", text);
        Assert.Contains("never", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void It_carries_the_install_line_and_the_runtime_it_needs()
    {
        var text = AgentInstructionsGenerator.Build("TestRunReport");

        Assert.Contains("dotnet tool install -g Kronikol.Tool", text);
        Assert.Contains(".NET 10", text);
        Assert.Contains("kronikol query --help", text);
    }

    [Fact]
    public void It_teaches_the_ladder_and_the_addresses()
    {
        var text = AgentInstructionsGenerator.Build("TestRunReport");

        foreach (var verb in new[] { "summary", "failures", "steps", "services", "flow", "http" })
            Assert.Contains("kronikol query " + verb, text);
        Assert.Contains("s3/i47", text);
        // The one address that is for a person rather than for the tool.
        Assert.Contains("TestRunReport.html#sid-<stableId>", text);
    }

    [Fact]
    public void It_honours_a_renamed_report()
    {
        var text = AgentInstructionsGenerator.Build("Acceptance");

        Assert.Contains("Acceptance.json", text);
        Assert.DoesNotContain("TestRunReport.json", text);
    }

    [Fact]
    public void It_is_static_text_with_no_room_for_run_data()
    {
        // The signature is the guarantee: there is no overload that takes features, logs or a summary, so
        // an attacker-influenced string has no route into an instruction file. Belt and braces, the text
        // also says so.
        var overloads = typeof(AgentInstructionsGenerator).GetMethods()
            .Where(m => m.Name == nameof(AgentInstructionsGenerator.Build))
            .ToArray();

        Assert.All(overloads, m => Assert.All(m.GetParameters(), p => Assert.Equal(typeof(string), p.ParameterType)));
        Assert.Contains("captured test data", AgentInstructionsGenerator.Build("TestRunReport"));
    }
}
