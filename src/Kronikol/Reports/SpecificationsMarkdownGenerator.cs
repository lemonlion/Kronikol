using System.Text;

namespace Kronikol.Reports;

/// <summary>
/// Writes <c>Specifications.md</c>: what the system does, as prose, in a file small enough to read whole.
///
/// <para><b>Why it exists.</b> The same question already has two answers and neither fits a reader who
/// just wants the specification. <c>Specifications.html</c> is living documentation for a browser — one
/// measured report was 10.7&#160;MB, and a single embedded diagram 663&#160;KB, so opening it is not a
/// slow way to read the spec, it is a way not to read it. The YAML/JSON/XML trio is a data structure: it
/// answers a parser, not a person. Markdown is what an agent, a README, a docs site and a pull request
/// review all read natively.</para>
///
/// <para><b>What it carries that the trio does not.</b> The data writers flatten each step to one string
/// and drop <see cref="Scenario.Rule"/> and <see cref="Scenario.Description"/> entirely. A narrative
/// without them reads as an ungrouped list of scenarios nobody wrote, so both are here — a deliberate
/// divergence from "the same source as the specifications data", not an accident.</para>
///
/// <para><b>What it deliberately does not carry.</b> No result, no duration, no interactions, no
/// diagrams. The same features run red and run green produce the same bytes, which is what makes this a
/// specification rather than a second report, and what makes it safe to commit to a docs site. It also
/// does <em>not</em> follow the blank-on-a-failed-run rule that <c>Specifications.html</c> and the data
/// trio follow: those exist to stop a broken build publishing half-truths as documentation, but an agent
/// reading this file is trying to understand the system it is debugging, and a blank file at exactly that
/// moment is the least useful thing it could find.</para>
/// </summary>
public static class SpecificationsMarkdownGenerator
{
    /// <summary>The specification as markdown. <paramref name="title"/> is the document's H1.</summary>
    public static string Generate(Feature[] features, string title)
    {
        ArgumentNullException.ThrowIfNull(features);

        var markdown = new StringBuilder();
        markdown.Append("# ").Append(Inline(title)).Append("\n\n");
        markdown.Append("Living documentation: every feature, scenario and step in the suite, without the run.\n")
                .Append("Results, timings, calls and diagrams are deliberately absent — for those, read `Failures.md`\n")
                .Append("or ask `kronikol query`.\n");

        // The same ordering as every other specifications writer, under the same comparer, so the three
        // views of one suite cannot be read against each other and disagree.
        foreach (var feature in features.OrderBy(f => f.DisplayName))
        {
            markdown.Append("\n## ").Append(Inline(feature.DisplayName)).Append('\n');

            if (feature.Endpoint is { Length: > 0 })
                markdown.Append("\nEndpoint: `").Append(Inline(feature.Endpoint)).Append("`\n");

            AppendProse(markdown, feature.Description);
            AppendTags(markdown, "Labels", feature.Labels);

            foreach (var group in Grouped(feature.Scenarios ?? []))
            {
                if (group.Key.Length > 0)
                    markdown.Append("\n### Rule: ").Append(Inline(group.Key)).Append('\n');

                var heading = group.Key.Length > 0 ? "\n#### " : "\n### ";
                foreach (var scenario in group)
                {
                    markdown.Append(heading).Append(Inline(scenario.DisplayName)).Append('\n');

                    AppendProse(markdown, scenario.Description);
                    AppendTags(markdown, "Labels", scenario.Labels);
                    AppendTags(markdown, "Categories", scenario.Categories);
                    AppendExamples(markdown, scenario);

                    // Kept apart for the same reason every data writer keeps them apart: merged, a reader
                    // cannot tell setup from the scenario, and the b{i}/{i} step-path split disappears.
                    if (scenario.BackgroundSteps is { Length: > 0 })
                    {
                        markdown.Append("\nBackground:\n\n");
                        foreach (var step in scenario.BackgroundSteps)
                            AppendStep(markdown, step, "");
                    }

                    if (scenario.Steps is { Length: > 0 })
                    {
                        markdown.Append('\n');
                        foreach (var step in scenario.Steps)
                            AppendStep(markdown, step, "");
                    }
                }
            }
        }

        return markdown.ToString();
    }

    /// <summary>
    /// Scenarios in the writers' usual order — happy paths first, then by name — grouped by
    /// <see cref="Scenario.Rule"/>. Ungrouped scenarios come first, which is where Gherkin puts the
    /// scenarios written before the first <c>Rule:</c>; after that the groups follow the ordering, and
    /// <c>OrderBy</c> is stable so nothing else moves.
    /// </summary>
    private static IEnumerable<IGrouping<string, Scenario>> Grouped(Scenario[] scenarios) =>
        scenarios
            .OrderByDescending(s => s.IsHappyPath)
            .ThenBy(s => s.DisplayName)
            .GroupBy(s => s.Rule ?? "")
            .OrderBy(g => g.Key.Length == 0 ? 0 : 1);

    /// <summary>
    /// The author's own prose, as a block quote. Quoted rather than emitted as a paragraph because a
    /// Gherkin description is free text: a line of it beginning <c>##</c> would otherwise become a heading
    /// and restructure the document around itself.
    /// </summary>
    private static void AppendProse(StringBuilder markdown, string? prose)
    {
        if (prose is not { Length: > 0 }) return;

        markdown.Append('\n');
        foreach (var line in prose.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            markdown.Append("> ").Append(line).Append('\n');
    }

    private static void AppendTags(StringBuilder markdown, string heading, string[]? values)
    {
        if (values is not { Length: > 0 }) return;

        markdown.Append('\n').Append(heading).Append(": ")
                .Append(string.Join(", ", values.Select(v => "`" + Inline(v) + "`")))
                .Append('\n');
    }

    /// <summary>
    /// Which row of an outline this is. Without it every row of a scenario outline renders as the same
    /// heading repeated, and the specification says one thing several times instead of several things.
    /// </summary>
    private static void AppendExamples(StringBuilder markdown, Scenario scenario)
    {
        if (scenario.ExampleValues is not { Count: > 0 }) return;

        markdown.Append("\nExample: ")
                .Append(string.Join(", ", scenario.ExampleValues.Select(v => $"{Inline(v.Key)} = `{Inline(v.Value)}`")))
                .Append('\n');
    }

    private static void AppendStep(StringBuilder markdown, ScenarioStep step, string indent)
    {
        markdown.Append(indent).Append("- ");
        if (step.Keyword is { Length: > 0 })
            markdown.Append("**").Append(Inline(step.Keyword)).Append("** ");
        markdown.Append(Inline(step.Text)).Append('\n');

        foreach (var sub in step.SubSteps ?? [])
            AppendStep(markdown, sub, indent + "  ");
    }

    /// <summary>
    /// Text that has to stay on one line: a heading, a list item, a tag. Line breaks become spaces —
    /// a step text written across three lines is one step, and rendering it as three list items would
    /// claim two steps that were never written.
    /// </summary>
    private static string Inline(string? text)
    {
        if (text is not { Length: > 0 }) return "";

        var collapsed = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (character is '\r' or '\n' or '\t' or ' ')
            {
                space = true;
                continue;
            }
            if (space && collapsed.Length > 0) collapsed.Append(' ');
            space = false;
            collapsed.Append(character);
        }
        return collapsed.ToString();
    }
}
