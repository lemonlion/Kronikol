using System.Text.RegularExpressions;
using Kronikol.Query;
using Kronikol.Reports;

namespace Kronikol.Tests.Tool;

/// <summary>
/// Every surface that tells a reader how to ask a run a question names the way that needs nothing
/// installed, and says it in a form that works (roadmap stage 1b, <c>plans/QUERY_FALLBACK_PLAN.md</c> §6.4).
///
/// <para>The <c>SkillDriftTests</c> idiom, extended to the new command form: a <c>query.cs -- verb</c> line
/// is checked against the verb table and the flags that verb reads, exactly as a <c>kronikol query verb</c>
/// line is. And every one of them goes through <c>dotnet run --file</c>: without <c>--file</c>, from a
/// folder holding a project, <c>dotnet run</c> runs that project and hands it <c>query.cs</c> as an
/// argument - measured, with a warning nobody reads.</para>
/// </summary>
public class QueryScriptAdviceTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([BuiltTool.RepoRoot, .. parts]));

    private static readonly Feature[] OneFailure =
    [
        new Feature
        {
            DisplayName = "Checkout",
            Scenarios = [new Scenario { Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Failed, ErrorMessage = "boom" }]
        }
    ];

    public static TheoryData<string> Surfaces =>
    [
        "the instructions beside every report",
        "the skill",
        "the skill's flag reference",
        "the agent block init-agents installs",
        "this repository's own agent block",
        "README.md",
        "the NuGet readme",
        "the templates' README",
        "Failures.md, failing",
        "Failures.md, green",
    ];

    private static string Text(string surface) => surface switch
    {
        "the instructions beside every report" => AgentInstructionsGenerator.Build("TestRunReport"),
        "the skill" => Read("templates", "skills", "kronikol-test-debugging", "SKILL.md"),
        "the skill's flag reference" => Read("templates", "skills", "kronikol-test-debugging", "references", "commands.md"),
        "the agent block init-agents installs" => Read("templates", "agents", "CLAUDE.md"),
        "this repository's own agent block" => Read("CLAUDE.md"),
        "README.md" => Read("README.md"),
        "the NuGet readme" => Read("nuget-readme.md"),
        "the templates' README" => Read("templates", "README.md"),
        "Failures.md, failing" => FailuresDigestGenerator.Generate(OneFailure, null, "TestRunReport", "3.32.0", queryScript: true).Markdown,
        "Failures.md, green" => FailuresDigestGenerator.Generate(
            [new Feature { DisplayName = "Checkout", Scenarios = [new Scenario { Id = "t1", DisplayName = "Pay", Result = ExecutionResult.Passed }] }],
            null, "TestRunReport", "3.32.0", queryScript: true).Markdown,
        _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
    };

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void It_names_query_cs(string surface)
    {
        Assert.Contains("query.cs", Text(surface), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void Every_query_cs_command_it_shows_is_one_the_tool_takes(string surface)
    {
        var refused = new List<string>();
        foreach (var line in Text(surface).ReplaceLineEndings("\n").Split('\n'))
            foreach (Match invocation in Regex.Matches(line, @"query\.cs -- ([a-z][a-z-]*)([^\n`|#]*)"))
            {
                var verb = invocation.Groups[1].Value;
                if (!QueryCommand.FlagsByVerb.TryGetValue(verb, out var legal))
                {
                    refused.Add($"{verb} is not a verb  —  {line.Trim()}");
                    continue;
                }

                foreach (Match flag in Regex.Matches(invocation.Groups[2].Value, @"--[a-z][a-z-]*"))
                    if (!legal.Contains(flag.Value, StringComparer.Ordinal) && !QueryCommand.UniversalFlags.Contains(flag.Value, StringComparer.Ordinal))
                        refused.Add($"{verb} does not read {flag.Value}  —  {line.Trim()}");
            }

        Assert.True(refused.Count == 0, $"{surface} shows query.cs commands the engine refuses:\n  " + string.Join("\n  ", refused));
    }

    [Theory]
    [MemberData(nameof(Surfaces))]
    public void It_always_runs_it_with_file(string surface)
    {
        var bare = Regex.Matches(Text(surface), @"dotnet run (?!--file )[^\n`]*query\.cs").Select(m => m.Value).ToList();

        Assert.True(bare.Count == 0, $"{surface} runs query.cs without --file, which runs a neighbouring project instead:\n  " + string.Join("\n  ", bare));
    }

    /// <summary>
    /// The plan's order (§3, §4.2): beside every report and in the skill's rule, the first command is the one
    /// that needs nothing installed, and the installed tool comes after it. An agent that may not install
    /// anything reads the directory's instructions first, and its first command should be one it can run.
    /// </summary>
    [Theory]
    [InlineData("the instructions beside every report", "dotnet run --file query.cs -- summary .", "kronikol query summary .")]
    [InlineData("the instructions beside every report", "dotnet run --file query.cs -- summary .", "dotnet tool install -g Kronikol.Tool")]
    [InlineData("the skill", "dotnet run --file .logs/kronikol/query.cs -- summary", "kronikol query summary .logs/kronikol")]
    [InlineData("the skill", "dotnet run --file .logs/kronikol/query.cs -- summary", "dotnet tool install -g Kronikol.Tool")]
    public void It_leads_with_the_way_that_needs_nothing_installed(string surface, string first, string after)
    {
        var text = Text(surface);
        var script = text.IndexOf(first, StringComparison.Ordinal);
        var tool = text.IndexOf(after, StringComparison.Ordinal);

        Assert.True(script >= 0, $"{surface} does not show {first}");
        Assert.True(tool >= 0, $"{surface} does not show {after}");
        Assert.True(script < tool, $"{surface} shows `{after}` before `{first}`");
    }

    [Fact]
    public void The_digest_names_query_cs_only_when_the_run_wrote_one()
    {
        Assert.DoesNotContain("query.cs", FailuresDigestGenerator.Generate(OneFailure, null, "TestRunReport", "3.32.0").Markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// The digest learned about query.cs through an internal overload, not a new parameter on the public
    /// <c>Generate</c>: a parameter added to a public method removes the signature compiled callers bind to,
    /// which is a break the semver rule keeps for a major.
    /// </summary>
    [Fact]
    public void The_digest_keeps_the_public_signature_compiled_callers_bind_to()
    {
        var publicGenerate = typeof(FailuresDigestGenerator)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(m => m.Name == nameof(FailuresDigestGenerator.Generate))
            .Select(m => string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)))
            .ToArray();

        Assert.Equal(
            ["Feature[], RequestResponseLog[], String, String, IReadOnlyList`1, String, IReadOnlyDictionary`2, HistoryVerdicts, FailuresDigestEarlierAttempt"],
            publicGenerate);
    }

    [Fact]
    public void The_managed_block_says_what_query_cs_needs_and_what_it_does_not()
    {
        var block = Text("the agent block init-agents installs");

        Assert.Contains(".NET 10 SDK", block, StringComparison.Ordinal);
        Assert.Contains("no network", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_skill_keeps_query_py_for_a_machine_without_the_NET_10_SDK()
    {
        var skill = Text("the skill");

        Assert.Contains("scripts/query.py", skill, StringComparison.Ordinal);
        Assert.Contains("without the .NET 10 SDK", skill, StringComparison.Ordinal);
    }
}
