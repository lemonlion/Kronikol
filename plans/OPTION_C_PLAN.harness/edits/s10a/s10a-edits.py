# S10a (the emitter's half of the diagram look, plans/OPTION_C_PLAN.md S10, P17 to P20): the system font where a browser
# engine draws, flat boxes, rounded participants, thin lines and note edges, after the pragma and only without a user
# theme; services drawn as boxes. usage: python s10a-edits.py <worktree root>
import sys
ROOT = sys.argv[1].rstrip('/') + '/'
PUC = ROOT + 'src/Kronikol/PlantUml/PlantUmlCreator.cs'


def edit(path, pairs):
    s = open(path, encoding='utf-8', newline='').read()
    for old, new, *count in pairs:
        n = count[0] if count else 1
        assert s.count(old) == n, (path, old[:80], s.count(old))
        s = s.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(s)


LOOK = '''namespace Kronikol.PlantUml;

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
    private const string SystemUiFont = "skinparam defaultFontName system-ui\\n";

    // One rule to a line, each opening with its diagram type: a line that starts "note " or "participant " reads as a
    // note or a declaration to the source's line readers (the query tool's payload reader, the report's Setup filter),
    // and the engine draws this form byte for byte as it draws one property to a line.
    private const string Sequence =
        "skinparam shadowing false\\n" +
        "skinparam SequenceMessagePadding 4\\n" +
        "skinparam sequenceMessageAlignment center\\n" +
        "<style>\\n" +
        "sequenceDiagram { participant { RoundCorner 12; Padding 8 14; Margin 0 10; LineThickness 1 } }\\n" +
        "sequenceDiagram { actor, database, queue, collections, boundary, control, entity { Margin 0 10; LineThickness 1 } }\\n" +
        "sequenceDiagram { lifeLine { LineStyle 0; LineThickness 1 } }\\n" +
        "sequenceDiagram { note { LineThickness 1 } }\\n" +
        "</style>\\n";

    /// <summary>The lines a sequence diagram starts with after its pragma, each ending in a line break; empty under a
    /// user's theme.</summary>
    public static string ForSequence(string? plantUmlTheme, bool systemUiFont) =>
        !string.IsNullOrWhiteSpace(plantUmlTheme) ? "" : (systemUiFont ? SystemUiFont : "") + Sequence;
}
'''
open(ROOT + 'src/Kronikol/PlantUml/DiagramLook.cs', 'w', encoding='utf-8', newline='').write(LOOK)

edit(PUC, [
    # the public entry and its worker take the flag after the wrap width
    ("""        int? maxArrowsPerDiagram = null,
        int diagramNoteWrapWidth = DefaultNoteWrapWidth)
    {
        excludedHeaders ??= DefaultExcludedHeaders;""",
     """        int? maxArrowsPerDiagram = null,
        int diagramNoteWrapWidth = DefaultNoteWrapWidth,
        bool systemUiFont = false)
    {
        excludedHeaders ??= DefaultExcludedHeaders;"""),
    ("""                maxArrowsPerDiagram,
                diagramNoteWrapWidth);""",
     """                maxArrowsPerDiagram,
                diagramNoteWrapWidth,
                systemUiFont);"""),
    ("""        int? maxArrowsPerDiagram = null,
        int diagramNoteWrapWidth = DefaultNoteWrapWidth)
    {
        // Collapse poll/retry bursts""",
     """        int? maxArrowsPerDiagram = null,
        int diagramNoteWrapWidth = DefaultNoteWrapWidth,
        bool systemUiFont = false)
    {
        // Collapse poll/retry bursts"""),
    ("""            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides,
            diagramNoteWrapWidth);""",
     """            sequenceDiagramArrowColors, sequenceDiagramParticipantColors, dependencyColors, serviceTypeOverrides,
            diagramNoteWrapWidth, systemUiFont);"""),
    # the builder and the prefix
    ("""        Dictionary<string, string>? serviceTypeOverrides = null,
        int noteWrapWidth = DefaultNoteWrapWidth)
    {
        private readonly List<PlantUmlResult> _results = [];""",
     """        Dictionary<string, string>? serviceTypeOverrides = null,
        int noteWrapWidth = DefaultNoteWrapWidth,
        bool systemUiFont = false)
    {
        private readonly List<PlantUmlResult> _results = [];"""),
    ("dependencyColors, serviceTypeOverrides, noteWrapWidth)", "dependencyColors, serviceTypeOverrides, noteWrapWidth, systemUiFont)", 3),
    ("""        Dictionary<string, string>? serviceTypeOverrides = null,
        int noteWrapWidth = DefaultNoteWrapWidth)
    {
        var entitiesPlantUml""",
     """        Dictionary<string, string>? serviceTypeOverrides = null,
        int noteWrapWidth = DefaultNoteWrapWidth,
        bool systemUiFont = false)
    {
        var entitiesPlantUml"""),
    ("""                {themeDirective}!pragma teoz true
                {AddEventStyling(tracesForTest)}""",
     """                {themeDirective}!pragma teoz true
                {DiagramLook.ForSequence(plantUmlTheme, systemUiFont)}{AddEventStyling(tracesForTest)}"""),
    # services drawn as boxes (P20): a caller without a category after the first
    ("""                    // Callers without a category: use actor (first) or entity (subsequent)
                    sb.Append(actorDefined ? "entity" : "actor")""",
     """                    // Callers without a category: an actor (the first) or a box, as a service is (5.0.0, P20)
                    sb.Append(actorDefined ? "participant" : "actor")"""),
])
edit(ROOT + 'src/Kronikol/DependencyPalette.cs', [
    ('        DependencyType.HttpApi => "entity",', '        DependencyType.HttpApi => "participant",'),
])
print('ok')
