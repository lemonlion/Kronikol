using Kronikol.ComponentDiagram;

namespace Kronikol.Tests.PlantUml.Ikvm;

/// <summary>
/// What this renderer does on a machine with no Graphviz.
///
/// <para>Component, class and state diagrams are laid out by <c>dot</c>. Without it PlantUML does not
/// throw — it returns a valid image of a small card reading "Cannot find Graphviz". So a user who plugged
/// this package into <c>PlantUmlRendering.Local</c> without Graphviz installed got that card in place of
/// their architecture overview, with nothing anywhere saying why. It is the same silence that let this
/// repository's own CI certify a 400-pixel error image as a diagram that fits inside PlantUML's size
/// limit, for every run between the commit that added those facts and the one that added Graphviz to the
/// runner.</para>
///
/// <para>The fallback itself can only be exercised where <c>dot</c> is absent, which most development
/// machines are not — so these facts pin the two halves that are testable anywhere: that the source
/// rewrite is correct, and that a source carrying the pragma really does draw the diagram. The end-to-end
/// behaviour is verified in a container with no Graphviz installed.</para>
/// </summary>
public class GraphvizFallbackTests
{
    private static ComponentRelationship[] OneDependency() =>
    [
        new("Caller", "Payments API", "HTTP", ["POST"], 5, 3, null)
    ];

    [Fact]
    public void The_engine_it_reports_is_the_engine_it_hands_to_plantuml()
    {
        // Deliberately not `Assert.Equal("dot", …)`: that would assert a property of whichever machine is
        // running the test, which is how a Windows-shaped path assertion got into this repository and
        // reddened every CI runner. What is true on both kinds of machine is that the reported engine and
        // the source handed over agree - and on a machine with dot, agreeing means nothing is added, so
        // no output that renders today moves by a byte.
        var source = ComponentDiagramGenerator.GeneratePlantUml(OneDependency(), useC4: false);
        var handed = IkvmPlantUmlRenderer.WithLayoutEngine(source);

        if (IkvmPlantUmlRenderer.LayoutEngine == "dot")
            Assert.Same(source, handed);
        else
            Assert.Contains("!pragma layout smetana", handed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pragma_goes_on_the_line_after_the_start_directive()
    {
        // PlantUML reads a pragma only inside the diagram, so "prepend it" and "append it" are both
        // wrong; it has to land immediately after @startuml.
        var rewritten = IkvmPlantUmlRenderer.WithLayoutEngine("@startuml\nBob -> Alice : hi\n@enduml", dotIsUsable: false);

        Assert.Equal("@startuml\n!pragma layout smetana\nBob -> Alice : hi\n@enduml", rewritten);
    }

    [Fact]
    public void A_source_with_no_start_directive_is_left_alone()
    {
        // Nothing here parses PlantUML. A source shaped in a way this does not recognise is handed on
        // unchanged rather than guessed at - a wrong injection is a syntax error where today there is a
        // working diagram.
        Assert.Equal("no directive here",
            IkvmPlantUmlRenderer.WithLayoutEngine("no directive here", dotIsUsable: false));
    }

    [Fact]
    public void A_source_that_already_rendered_is_untouched_when_dot_works()
    {
        const string source = "@startuml\nBob -> Alice : hi\n@enduml";

        Assert.Same(source, IkvmPlantUmlRenderer.WithLayoutEngine(source, dotIsUsable: true));
    }

    [Fact]
    public void A_diagram_carrying_the_pragma_still_draws_the_diagram()
    {
        // The half that matters and that a machine WITH dot can still check: smetana is a real layout
        // engine here, not a word PlantUML ignores. If this ever regressed, the fallback would be
        // swapping one blank image for another.
        var source = ComponentDiagramGenerator.GeneratePlantUml(OneDependency(), useC4: false)
            .Replace("@startuml", "@startuml\n!pragma layout smetana");

        var drawn = RenderedDiagram.DrawnLine(RenderedDiagram.Svg(source));

        Assert.Contains("Payments API", drawn, StringComparison.Ordinal);
    }
}
