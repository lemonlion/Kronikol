using Kronikol.ComponentDiagram;

namespace Kronikol.Tests.ComponentDiagram;

/// <summary>
/// A participant's name is the component diagram's other unbounded width axis, and the narrower one.
/// Measured against real PlantUML (see <c>ComponentDiagramLabelWidthTests</c>) a
/// <c>&lt;&lt;system&gt;&gt;</c> rectangle grows at about 7.3 pixels per character and crosses the
/// 4096-pixel <c>PLANTUML_LIMIT_SIZE</c> at around 528 characters; the hexagon an AI dependency draws as
/// grows at about 14 and crosses it at around 284. <c>skinparam wrapWidth</c> does not save it: that only
/// breaks at whitespace, and the names that get long — hosts, fully-qualified type names, connection
/// descriptors — have none.
/// </summary>
public class ComponentDiagramNameWrappingTests
{
    private static ComponentRelationship[] NamedDependency(string name, string? category = null) =>
        [new("Caller", name, category ?? "HTTP", ["GET"], 5, 3, category)];

    /// <summary>The declaration line for the participant whose name starts with <paramref name="prefix"/>.</summary>
    private static string Declaration(string plantUml, string prefix) =>
        plantUml.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Single(l => l.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void An_ordinary_name_keeps_its_exact_declaration()
    {
        // Every real service name is far short of the budget, so no existing diagram changes at all.
        var result = ComponentDiagramGenerator.GeneratePlantUml(NamedDependency("OrderService"), useC4: false);

        Assert.Contains(
            "rectangle \"**OrderService**\\n<size:10>[Software System]</size>\" as orderService <<system>>",
            result, StringComparison.Ordinal);
        Assert.Contains(
            "rectangle \"**Caller**\\n<size:10>[Person]</size>\" as caller <<person>>",
            result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_name_reopens_bold_on_every_line()
    {
        // `**a\nb**` is not bold across the break — creole bold is line-scoped, and the closing marker
        // draws as literal text. Each line has to carry its own pair.
        var name = "DataInsights." + new string('x', 300) + ".Gateway";

        var result = ComponentDiagramGenerator.GeneratePlantUml(NamedDependency(name), useC4: false);

        var declaration = Declaration(result, "rectangle \"**DataInsights");
        var field = declaration["rectangle \"".Length..declaration.IndexOf("\\n<size:10>", StringComparison.Ordinal)];
        var lines = field.Split("\\n");

        Assert.True(lines.Length > 1, "a 300-character name should not be drawn on one line");
        Assert.All(lines, line =>
        {
            Assert.StartsWith("**", line, StringComparison.Ordinal);
            Assert.EndsWith("**", line, StringComparison.Ordinal);
            Assert.True(line.Length - 4 <= ComponentDiagramGenerator.MaxNameLineChars, $"{line.Length - 4} chars");
        });
        // And the name itself is intact — the breaks add nothing and lose nothing.
        Assert.Equal(name, string.Concat(lines.Select(l => l[2..^2])));
    }

    [Fact]
    public void A_long_caller_name_is_wrapped_the_same_way()
    {
        var name = "Acceptance." + new string('y', 300) + ".Harness";

        var result = ComponentDiagramGenerator.GeneratePlantUml(
            [new(name, "OrderService", "HTTP", ["GET"], 5, 3)], useC4: false);

        var declaration = Declaration(result, "rectangle \"**Acceptance");
        Assert.Contains("[Person]", declaration, StringComparison.Ordinal);
        Assert.Contains("**\\n**", declaration, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ClickHouse", "database ")]
    [InlineData("Redis", "collections ")]
    [InlineData("MessageQueue", "queue ")]
    [InlineData("AI", "hexagon ")]
    public void A_long_name_on_a_plain_shape_carries_no_creole_markers(string category, string shape)
    {
        // database/collections/queue/hexagon take an unstyled quoted label, so they need the breaks and
        // nothing else — a `**` here would draw as literal text.
        var name = "host-" + new string('x', 300) + ".internal";

        var result = ComponentDiagramGenerator.GeneratePlantUml(NamedDependency(name, category), useC4: false);

        var declaration = Declaration(result, shape);
        Assert.DoesNotContain("**", declaration, StringComparison.Ordinal);
        Assert.Contains("\\n", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_name_in_the_C4_flavour_needs_no_bold_markers()
    {
        // The C4 macros apply the weight through the style, not through creole.
        var name = "DataInsights." + new string('x', 300) + ".Gateway";

        var result = ComponentDiagramGenerator.GeneratePlantUml(NamedDependency(name), useC4: true);

        var declaration = Declaration(result, "System(");
        Assert.DoesNotContain("**", declaration, StringComparison.Ordinal);
        Assert.Contains("\\n", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapping_a_name_does_not_disturb_the_alias_the_edges_refer_to()
    {
        // The alias is derived from the name, not from the wrapped label — an edge that referred to a
        // participant before the break has to still refer to it after.
        var name = "DataInsights." + new string('x', 300) + ".Gateway";

        var result = ComponentDiagramGenerator.GeneratePlantUml(NamedDependency(name), useC4: false);

        var alias = Declaration(result, "rectangle \"**DataInsights")
            .Split("\" as ")[1].Split(' ')[0];
        Assert.Contains($"-> {alias} : \"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\\n", alias, StringComparison.Ordinal);
    }
}
