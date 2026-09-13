using Kronikol.Reports;

namespace Kronikol.Tests.Reports;

/// <summary>
/// <c>Specifications.md</c> is an entire agent-facing file made of nothing but run-supplied text, written
/// into the same directory as the nested <c>CLAUDE.md</c> and <c>Failures.md</c> — and it shipped without
/// either half of what its sibling has.
///
/// <para>Every structural element of it is the run's: the H1 is the configured title, every <c>##</c> is a
/// feature name, every <c>####</c> a scenario name, and the block quotes are Gherkin descriptions written
/// by whoever wrote the feature. <c>Failures.md</c> carries the sentence saying that quoted text is test
/// data; this carried none, and neither <c>agent-instructions.md</c> nor either <c>SKILL.md</c> mentions
/// the file at all, so an agent that opens it gets the content without the pointer and without the
/// rule.</para>
///
/// <para>The escaping half is the sharper one. A scenario named <c>## Admin</c> became a level-two
/// heading that restructured the document around itself, and a description is emitted line by line into a
/// block quote with no escape at all, so markdown or HTML in it renders. The prose case is deliberate and
/// documented — an author's paragraph should keep its emphasis — but the headings are not prose, and a
/// name is not a place where a caller gets to choose the document's shape.</para>
/// </summary>
public class SpecificationsMarkdownVoiceTests
{
    [Fact]
    public void The_file_says_that_its_quoted_text_is_test_data()
    {
        Assert.Contains("not instructions", Generate("Checkout"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The document's shape belongs to the generator. A scenario whose name begins with hashes was
    /// promoted to a heading of its own level, so a suite could rewrite the outline of the file that
    /// describes it.
    /// </summary>
    [Theory]
    [InlineData("## Admin overrides", "Admin overrides")]
    [InlineData("# Everything", "Everything")]
    [InlineData("- a bullet", "a bullet")]
    public void A_name_cannot_restructure_the_document(string name, string tail)
    {
        var markdown = Generate(name);

        // Every line the name reaches, not just one: the scenario appears as a heading and again in the
        // step list, and a name that can restructure the document does it wherever it is emitted.
        var lines = markdown.Split('\n').Where(l => l.Contains(tail, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(lines);

        foreach (var line in lines)
        {
            Assert.False(line.StartsWith("# ", StringComparison.Ordinal), $"became an H1: {line}");
            Assert.False(line.StartsWith("## ", StringComparison.Ordinal), $"became an H2: {line}");
            Assert.False(line.StartsWith("- ", StringComparison.Ordinal) && name.StartsWith('-'),
                $"became a bullet of its own: {line}");
        }
    }

    /// <summary>The name still has to be readable — escaping is not deletion.</summary>
    [Fact]
    public void An_escaped_name_still_reads_as_itself()
    {
        Assert.Contains("Admin overrides", Generate("## Admin overrides"), StringComparison.Ordinal);
    }

    private static string Generate(string scenarioName) =>
        SpecificationsMarkdownGenerator.Generate(
            [
                new Feature
                {
                    DisplayName = "Orders",
                    Scenarios = [new Scenario { Id = "t0", DisplayName = scenarioName, Result = ExecutionResult.Passed }]
                }
            ],
            "Widgets");
}
