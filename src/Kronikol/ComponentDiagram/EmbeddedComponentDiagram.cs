namespace Kronikol.ComponentDiagram;

/// <summary>
/// The run report's component diagram as its Components tab draws it (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28), from
/// <see cref="ComponentDiagramGenerator.GenerateEmbedded"/>: the source, without the title the tab's heading carries, and
/// where in it each part is written, so the page can draw it filtered, top to bottom or without its call summaries
/// without reading the source back.
/// </summary>
/// <param name="PlantUml">The source the tab draws.</param>
/// <param name="Heading">The tab's heading: <see cref="ComponentDiagramOptions.Title"/>, or <see cref="DefaultHeading"/>
/// where that is blank.</param>
/// <param name="DirectionLine">The line of <c>left to right direction</c>. Lines are counted from 0, as the page splits
/// the source at its line feeds.</param>
/// <param name="ComponentLines">The line of each component, in the order the source declares them.</param>
/// <param name="Connections">Each connection, in the order the source writes them.</param>
internal sealed record EmbeddedComponentDiagram(
    string PlantUml,
    string Heading,
    int DirectionLine,
    int[] ComponentLines,
    EmbeddedComponentDiagram.Connection[] Connections)
{
    /// <summary>The heading of a diagram whose title is blank, and of one a caller hands in as source alone.</summary>
    internal const string DefaultHeading = "Component Diagram";

    /// <summary>A connection: its line, its caller's and its service's places in <see cref="ComponentLines"/>, its line
    /// without the label, and the ids of the tests whose calls it draws, or null where they are not known.</summary>
    internal sealed record Connection(int Line, int From, int To, string Bare, IReadOnlyCollection<string>? TestIds);
}
