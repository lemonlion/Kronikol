namespace Kronikol.PlantUml;

/// <summary>
/// The look a sequence diagram's source starts with (5.0.0, plans/OPTION_C_PLAN.md S10, P17 to P19): flat boxes, rounded
/// participants with thin edges, thin lifelines and note edges, messages centred over their arrows, and the system's
/// own font where a browser engine draws the diagram. The engine measures and paints the same face only when it is the
/// system's own: a font list is quoted into one family no system has, and the JVM (Server and Local rendering) knows no
/// <c>system-ui</c>. Written straight after the pragma, so the diagram's own styles and settings come over it; a diagram
/// drawn with a user's theme leaves it out and keeps the theme's look.
/// </summary>
internal static class DiagramLook
{
    private const string SystemUiFont = "skinparam defaultFontName system-ui\n";

    // A loop's box and the Setup box (5.8.0): a rounded light box with a thin light edge, its label's tab shaded and its
    // condition in plain type, where a loop was a square frame in a 1.5 px black line, the heaviest line in the drawing.
    // These are the light theme's colours, which a picture drawn away from the page keeps; the page gives each the
    // theme's token (inline-svg-styles.css), so the dark theme draws the boxes dark.

    /// <summary>A loop's ground (<c>--dg-loop</c>).</summary>
    internal const string LoopFill = "#F4F7FB";

    /// <summary>A loop's edge and its tab's edge (<c>--dg-loop-edge</c>).</summary>
    internal const string LoopEdge = "#CDD5E0";

    /// <summary>The ground of a loop's tab, which holds the word "loop" (<c>--dg-loop-tab</c>).</summary>
    internal const string LoopTab = "#E9EEF6";

    /// <summary>The Setup box's edge (<c>--dg-setup-edge</c>).</summary>
    internal const string SetupEdge = "#D5DBE4";

    /// <summary>The Setup box's ground where the options name no other colour (<c>--dg-setup</c>).</summary>
    internal const string SetupFill = "#F6F6F6";

    // One rule to a line, each opening with its diagram type: a line that starts "note " or "participant " reads as a
    // note or a declaration to the source's line readers (the query tool's payload reader, the report's Setup filter),
    // and the engine draws this form byte for byte as it draws one property to a line.
    private const string Sequence =
        "skinparam shadowing false\n" +
        "skinparam SequenceMessagePadding 4\n" +
        "skinparam sequenceMessageAlignment center\n" +
        "<style>\n" +
        "sequenceDiagram { participant { RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1 } }\n" +
        "sequenceDiagram { actor, database, queue, collections, boundary, control, entity { Margin 0 10; LineThickness 1 } }\n" +
        "sequenceDiagram { lifeLine { LineStyle 0; LineThickness 1 } }\n" +
        "sequenceDiagram { note { LineThickness 1 } }\n" +
        "sequenceDiagram { group { RoundCorner 16; LineThickness 1; LineColor " + LoopEdge + "; BackgroundColor " + LoopFill + "; FontStyle plain } }\n" +
        "sequenceDiagram { groupHeader { LineThickness 1; LineColor " + LoopEdge + "; BackgroundColor " + LoopTab + " } }\n" +
        "sequenceDiagram { partition { RoundCorner 16; LineThickness 1; LineColor " + SetupEdge + " } }\n" +
        "</style>\n";

    /// <summary>The lines a sequence diagram starts with after its pragma, each ending in a line break; empty under a
    /// user's theme.</summary>
    public static string ForSequence(string? plantUmlTheme, bool systemUiFont) =>
        !string.IsNullOrWhiteSpace(plantUmlTheme) ? "" : (systemUiFont ? SystemUiFont : "") + Sequence;
}
