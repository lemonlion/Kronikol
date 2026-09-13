using System.Reflection;
using Kronikol.Tracking;
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
        // The signature is the guarantee: nothing here takes features, logs or a summary, so
        // attacker-influenced text has no route into an instruction file. An instruction file is the one
        // artifact an agent is told to treat as directions rather than as data, which is why the route has
        // to be closed at the type and not only in the prose.
        //
        // The previous version of this asserted that every parameter of every `Build` is a `string`, over
        // a bare `GetMethods()`. Both halves were wrong. `GetMethods()` with no flags returns public
        // members only, so a non-public overload taking `Feature[]` was invisible to it; and "all
        // parameters are strings" is satisfied by `Build(string report, string capturedBody)`, which is
        // precisely the injection this test exists to forbid. It was also vacuous under a rename, because
        // `Assert.All` over an empty array passes.
        const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic
                                        | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var builders = typeof(AgentInstructionsGenerator).GetMethods(Everything)
            .Where(m => m.Name == nameof(AgentInstructionsGenerator.Build))
            .ToArray();

        // Exactly one way in, taking exactly one thing, and that thing is a file name.
        var build = Assert.Single(builders);
        var parameter = Assert.Single(build.GetParameters());
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("htmlTestRunReportFileName", parameter.Name);

        // And nothing else on the type — public or not, property or method — accepts a run. Named by type
        // rather than by count, so adding a legitimate helper does not redden this and adding a route does.
        Type[] runShapes = [typeof(Feature), typeof(Scenario), typeof(ScenarioStep), typeof(RequestResponseLog), typeof(FailuresDigest)];
        foreach (var method in typeof(AgentInstructionsGenerator).GetMethods(Everything))
            foreach (var each in method.GetParameters())
            {
                var type = each.ParameterType;
                var element = type.IsArray ? type.GetElementType()! : type;
                Assert.DoesNotContain(element, runShapes);
            }

        Assert.Contains("captured test data", AgentInstructionsGenerator.Build("TestRunReport"));
    }
}
