# S11e (D26, D28): the Components tab. Applies AFTER S11d (the Timeline tab's controls), whose markup, helpers and rules
# it reuses: the header line (.timeline-header), the control groups (TimelineSegments, .timeline-controls, .tl-seg,
# .tl-seg-btn) and the note (TimelineFilteredNote, .filtered-note).
# The generator heads the tab with the diagram's title, which the embedded drawing leaves out (ComponentDiagram.html keeps
# it), and where the page draws the diagram (BrowserJs), writes Show (Filtered or Full, where the report knows which
# scenarios made each connection's calls), Direction (Left to right or Top to bottom) and Call summaries (Shown or Hidden)
# at the right of that line, the note under it, and a model of the source beside it (#component-diagram-model): the line
# of the direction, of each component and of each connection, each connection's two components, its line without its
# label, and the scenarios whose calls it draws, by stable id, computed from the run's calls and not from the drawn text.
# report-components-script.js (new; embedded by Kronikol.csproj's Reports\report-*.js) draws each form from the source
# as written. The tab's card joins the timeline's rule in stylesheets.css. A run whose calls give the diagram no
# connection has no Components tab, as a merged report without relationships already had none.
# usage: python scripts-s11e.py <worktree root> [--stage all|generator|rest]
#   all (the default) applies everything. generator applies section 1 alone (ComponentDiagramGenerator,
#   EmbeddedComponentDiagram.cs and the option's summary), which changes no output, so the core test class compiles and
#   its report facts can be run red on assertions; rest then applies sections 2 to 5.
import os
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'
R = ROOT + 'src/Kronikol/'
STAGE = sys.argv[3] if len(sys.argv) > 3 and sys.argv[2] == '--stage' else 'all'
assert STAGE in ('all', 'generator', 'rest'), ('unknown stage', STAGE)

# All or nothing: every edit is made in memory and checked, and the files are written only once all of them hold, so a
# moved anchor leaves the tree as it was.
files = {}
originals = {}
created = {}
section = 1


def wanted():
    return STAGE == 'all' or (STAGE == 'generator') == (section == 1)


def text(path):
    if path not in files:
        files[path] = originals[path] = open(R + path, encoding='utf-8', newline='').read()
    return files[path]


def edit(path, old, new, count=1):
    if not wanted():
        return
    s = text(path)
    n = s.count(old)
    assert n == count, (path, n, old[:80])
    files[path] = s.replace(old, new)


def create(path, content):
    if not wanted():
        return
    assert not os.path.exists(R + path), ('already there', path)
    created[path] = content


# The new script is embedded by the csproj's wildcard, as every report-*.js is; nothing to add there.
assert '<EmbeddedResource Include="Reports\\report-*.js" />' in text('Kronikol.csproj'), 'the Reports\\report-*.js wildcard is gone'

RG = 'Reports/ReportGenerator.cs'
CDG = 'ComponentDiagram/ComponentDiagramGenerator.cs'
CSS = 'Reports/stylesheets.css'
MERGE = 'Reports/Merge/MergeableReportRenderer.cs'
assert 'report-components-script.js' not in text(RG), 'S11e is in already'
# rest needs section 1 in; all and generator need it out
assert ('GenerateEmbedded' in text(CDG)) == (STAGE == 'rest'), \
    'apply --stage generator first' if STAGE == 'rest' else 'section 1 is in already: use --stage rest'
# S11e builds on S11d: its controls, its note and its card rule must be there first.
assert 'report-timeline-script.js' in text(RG), 'S11d is not in: apply scripts-s11d.py first'
assert 'private const string TimelineFilteredNote' in text(RG), 'S11d is not in: apply scripts-s11d.py first'
assert '.tl-seg-btn[aria-pressed="true"]' in text(CSS), "S11d's rules are not in stylesheets.css"

# ---------------------------------------------------------------------------------------------------------------------
# 1. ComponentDiagramGenerator: the calls an edge draws in one place, the tests that made them, and the embedded form.
# ---------------------------------------------------------------------------------------------------------------------
edit(CDG, r'''    {
        var filtered = logs.Where(log =>
            !log.TrackingIgnore &&
            !log.IsOverrideStart &&
            !log.IsOverrideEnd &&
            !log.IsActionStart &&
            log.Type == RequestResponseType.Request);

        if (participantFilter is not null)
            filtered = filtered.Where(log =>
                participantFilter(log.CallerName) &&
                participantFilter(log.ServiceName));

        var groups = filtered.GroupBy(log => (log.CallerName, log.ServiceName, Protocol: GetProtocol(log)));
''', r'''    {
        var groups = DrawnCalls(logs, participantFilter).GroupBy(log => (log.CallerName, log.ServiceName, Protocol: GetProtocol(log)));
''')

edit(CDG, r'''    public static string GeneratePlantUml(
        ComponentRelationship[] relationships,
        ComponentDiagramOptions? options = null,
        Dictionary<string, RelationshipStats>? stats = null,
        bool useC4 = true)
    {
        options ??= new ComponentDiagramOptions();
        var sb = new StringBuilder();
''', r'''    /// <summary>
    /// The calls a component diagram draws: requests, without the tracking's own markers and the calls it was told to
    /// ignore, between two participants <paramref name="participantFilter"/> keeps. <see cref="ExtractRelationships"/>
    /// counts them and <see cref="TestsOf"/> says which tests made them, so the two agree on every edge.
    /// </summary>
    private static IEnumerable<RequestResponseLog> DrawnCalls(IEnumerable<RequestResponseLog> logs, Func<string, bool>? participantFilter)
    {
        var filtered = logs.Where(log =>
            !log.TrackingIgnore &&
            !log.IsOverrideStart &&
            !log.IsOverrideEnd &&
            !log.IsActionStart &&
            log.Type == RequestResponseType.Request);

        if (participantFilter is not null)
            filtered = filtered.Where(log =>
                participantFilter(log.CallerName) &&
                participantFilter(log.ServiceName));

        return filtered;
    }

    /// <summary>
    /// The ids of the tests whose calls each edge draws, by the edge's caller, service and protocol, read from the calls
    /// <see cref="ExtractRelationships"/> counts: what the run report's Components tab draws its Filtered form from (5.0.0,
    /// plans/OPTION_C_PLAN.md S11, D28).
    /// </summary>
    internal static Dictionary<(string Caller, string Service, string Protocol), HashSet<string>> TestsOf(
        IEnumerable<RequestResponseLog> logs,
        Func<string, bool>? participantFilter = null) =>
        DrawnCalls(logs, participantFilter)
            .GroupBy(log => (Caller: log.CallerName, Service: log.ServiceName, Protocol: GetProtocol(log)))
            .ToDictionary(g => g.Key, g => g.Select(log => log.TestId).ToHashSet(StringComparer.Ordinal));

    public static string GeneratePlantUml(
        ComponentRelationship[] relationships,
        ComponentDiagramOptions? options = null,
        Dictionary<string, RelationshipStats>? stats = null,
        bool useC4 = true) =>
        Generate(relationships, options ?? new ComponentDiagramOptions(), stats, useC4, roles: null);

    /// <summary>
    /// The diagram as the run report's Components tab draws it (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28):
    /// <see cref="GeneratePlantUml"/>'s source without its title, which the tab's heading carries (ComponentDiagram.html
    /// keeps it), with the line each component and each connection is written on, each connection without its label, and,
    /// where <paramref name="logs"/> are given, the tests whose calls each connection draws (<see cref="TestsOf"/>).
    /// </summary>
    internal static EmbeddedComponentDiagram GenerateEmbedded(
        ComponentRelationship[] relationships,
        ComponentDiagramOptions? options = null,
        bool useC4 = true,
        IEnumerable<RequestResponseLog>? logs = null)
    {
        options ??= new ComponentDiagramOptions();
        var roles = new LineRoles();
        var plantUml = Generate(relationships, options, stats: null, useC4, roles);
        var component = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < roles.Components.Count; i++)
            component[roles.Components[i].Name] = i;
        var tests = logs is null ? null : TestsOf(logs, options.ParticipantFilter);
        return new EmbeddedComponentDiagram(
            plantUml,
            string.IsNullOrWhiteSpace(options.Title) ? EmbeddedComponentDiagram.DefaultHeading : options.Title,
            roles.Direction,
            roles.Components.Select(c => c.Line).ToArray(),
            roles.Connections.Select(c => new EmbeddedComponentDiagram.Connection(
                c.Line,
                component[c.Relationship.Caller],
                component[c.Relationship.Service],
                PlantUml.PlantUmlCreator.ReplaceXmlInvalidCharacters(c.Bare),
                tests is not null && tests.TryGetValue((c.Relationship.Caller, c.Relationship.Service, c.Relationship.Protocol), out var ids) ? ids : null))
                .ToArray());
    }

    /// <summary>Where <see cref="Generate"/> wrote each part of a diagram, for <see cref="GenerateEmbedded"/>.</summary>
    private sealed class LineRoles
    {
        public int Direction;
        public readonly List<(int Line, string Name)> Components = [];
        public readonly List<(int Line, ComponentRelationship Relationship, string Bare)> Connections = [];
    }

    private static string Generate(
        ComponentRelationship[] relationships,
        ComponentDiagramOptions options,
        Dictionary<string, RelationshipStats>? stats,
        bool useC4,
        LineRoles? roles)
    {
        var sb = new StringBuilder();
        // The line the next statement starts on, counted from 0 as the page splits the source: every line feed written so
        // far. Counted only for the Components tab's model (roles).
        var counted = 0;
        var lineNumber = 0;
        int NextLine()
        {
            for (; counted < sb.Length; counted++)
                if (sb[counted] == '\n')
                    lineNumber++;
            return lineNumber;
        }
''')

edit(CDG, r'''        sb.AppendLine("@startuml");
        sb.AppendLine("left to right direction");
''', r'''        sb.AppendLine("@startuml");
        if (roles is not null)
            roles.Direction = NextLine();
        sb.AppendLine("left to right direction");
''')

edit(CDG, r'''        // A title does not wrap at any width, at any skinparam, on either engine: a user-configured
        // title is one more unbounded width axis, so it takes the same budget an edge label does.
        sb.AppendLine($"title {WrapLabel(options.Title)}");
        sb.AppendLine();
''', r'''        // A title does not wrap at any width, at any skinparam, on either engine: a user-configured
        // title is one more unbounded width axis, so it takes the same budget an edge label does.
        // The Components tab's drawing has none: the tab's heading carries it (GenerateEmbedded).
        if (roles is null)
        {
            sb.AppendLine($"title {WrapLabel(options.Title)}");
            sb.AppendLine();
        }
''')

edit(CDG, r'''        foreach (var participant in allParticipants)
        {
            var alias = SanitizeAlias(participant);
''', r'''        foreach (var participant in allParticipants)
        {
            // each participant is one statement, whatever its shape
            roles?.Components.Add((NextLine(), participant));
            var alias = SanitizeAlias(participant);
''')

edit(CDG, r'''            // Determine arrow style
            var color = "";
            if (options.ArrowColorMode == ArrowColorMode.DependencyType)
            {
                color = DependencyPalette.GetColor(rel.DependencyCategory, options.DependencyColors);
            }
            else if (stats != null && stats.TryGetValue(relKey, out var arrowStats))
            {
                // Hotspot coloring by P95
                color = arrowStats.P95Ms switch
                {
                    < 50 => "#Green",
                    < 200 => "#Orange",
                    _ => "#Red"
                };

                // Low coverage uses dashed line
                if (arrowStats.IsLowCoverage)
                {
                    sb.AppendLine($"{callerAlias} ..> {serviceAlias} : \"{label}\"");
                    continue;
                }
            }

            if (useC4)
            {
                if (!string.IsNullOrEmpty(color))
                    sb.AppendLine($"Rel({callerAlias}, {serviceAlias}, \"{label}\", $tags=\"{color}\")");
                else
                    sb.AppendLine($"Rel({callerAlias}, {serviceAlias}, \"{label}\")");
            }
            else
            {
                if (!string.IsNullOrEmpty(color))
                    sb.AppendLine($"{callerAlias} -[{color}]-> {serviceAlias} : \"{label}\"");
                else
                    sb.AppendLine($"{callerAlias} --> {serviceAlias} : \"{label}\"");
            }
        }
''', r'''            // Determine arrow style
            var color = "";
            var lowCoverage = false;
            if (options.ArrowColorMode == ArrowColorMode.DependencyType)
            {
                color = DependencyPalette.GetColor(rel.DependencyCategory, options.DependencyColors);
            }
            else if (stats != null && stats.TryGetValue(relKey, out var arrowStats))
            {
                // Hotspot coloring by P95
                color = arrowStats.P95Ms switch
                {
                    < 50 => "#Green",
                    < 200 => "#Orange",
                    _ => "#Red"
                };

                // Low coverage uses dashed line
                lowCoverage = arrowStats.IsLowCoverage;
            }

            // The edge as written, and as the Components tab writes it without its label (Call summaries: Hidden).
            string edge, bare;
            if (lowCoverage)
            {
                bare = $"{callerAlias} ..> {serviceAlias}";
                edge = $"{bare} : \"{label}\"";
            }
            else if (useC4)
            {
                var tags = string.IsNullOrEmpty(color) ? "" : $", $tags=\"{color}\"";
                edge = $"Rel({callerAlias}, {serviceAlias}, \"{label}\"{tags})";
                bare = $"Rel({callerAlias}, {serviceAlias}, \"\"{tags})";
            }
            else
            {
                bare = string.IsNullOrEmpty(color) ? $"{callerAlias} --> {serviceAlias}" : $"{callerAlias} -[{color}]-> {serviceAlias}";
                edge = $"{bare} : \"{label}\"";
            }
            roles?.Connections.Add((NextLine(), rel, bare));
            sb.AppendLine(edge);
        }
''')

create('ComponentDiagram/EmbeddedComponentDiagram.cs', r'''namespace Kronikol.ComponentDiagram;

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
''')

# The option's summary says where the title goes now.
edit('ComponentDiagram/ComponentDiagramOptions.cs',
     '''    /// <summary>Title displayed above the component diagram. Default: <c>"Component Diagram"</c>.</summary>
''', '''    /// <summary>
    /// The component diagram's title: drawn above it in ComponentDiagram.html and, since 5.0.0, the heading of the run
    /// report's Components tab, whose drawing leaves it out. Default: <c>"Component Diagram"</c>.
    /// </summary>
''')

# ---------------------------------------------------------------------------------------------------------------------
# 2. ReportGenerator: the run's embedded diagram, its parameter, the script, the tab's markup and the helpers.
#    Sections 2 to 5 are the stage "rest".
# ---------------------------------------------------------------------------------------------------------------------
section = 2
edit(RG, r'''        // Pre-compute component diagram PlantUML for embedding
        string? componentDiagramPlantUml = null;
        if (options.GenerateComponentDiagram)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            componentOptions.DependencyColors ??= options.DependencyColors;
            var componentLogs = runLogs.Where(x => !(x?.TrackingIgnore ?? true));
            var componentRelationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);
            // The syntax ComponentDiagram.html is written in: until 3.31.8 the run report gave NodeJs the C4 library, which
            // the Node renderer cannot load.
            componentDiagramPlantUml = ComponentDiagramGenerator.GeneratePlantUml(componentRelationships, componentOptions,
                useC4: ComponentDiagramReportGenerator.UsesC4(options.PlantUmlRendering));
        }
''', r'''        // Pre-compute component diagram PlantUML for embedding, as the Components tab draws it: without its title, which the
        // tab's heading carries, and with the tests that made each connection's calls, for its controls (5.0.0,
        // plans/OPTION_C_PLAN.md S11, D26, D28). A run whose calls give the diagram no connection has no tab: there is
        // nothing to draw. ComponentDiagram.html is written from the same calls on its own, title and all.
        EmbeddedComponentDiagram? componentDiagram = null;
        if (options.GenerateComponentDiagram)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            componentOptions.DependencyColors ??= options.DependencyColors;
            var componentLogs = runLogs.Where(x => !(x?.TrackingIgnore ?? true));
            var componentRelationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);
            // The syntax ComponentDiagram.html is written in: until 3.31.8 the run report gave NodeJs the C4 library, which
            // the Node renderer cannot load.
            if (componentRelationships.Length > 0)
                componentDiagram = ComponentDiagramGenerator.GenerateEmbedded(componentRelationships, componentOptions,
                    useC4: ComponentDiagramReportGenerator.UsesC4(options.PlantUmlRendering), logs: componentLogs);
        }
        var componentDiagramPlantUml = componentDiagram?.PlantUml;
''')

edit(RG, 'componentDiagramDrawn: componentDiagramDrawn, ',
     'componentDiagramDrawn: componentDiagramDrawn, componentDiagram: ShouldEmbedComponentDiagram(options) ? componentDiagram : null, ')

edit(RG, r'''    /// (<see cref="ComponentDiagramReportGenerator.DrawEmbedded"/>), which the page shows in its panel in place of drawing
    /// <paramref name="componentDiagramPlantUml"/> itself.
    /// </summary>
''', r'''    /// (<see cref="ComponentDiagramReportGenerator.DrawEmbedded"/>), which the page shows in its panel in place of drawing
    /// <paramref name="componentDiagramPlantUml"/> itself, and with <paramref name="componentDiagram"/>, the diagram as the
    /// Components tab draws it: its source stands for <paramref name="componentDiagramPlantUml"/>, and where the page draws
    /// it, the tab's controls redraw it from its parts (5.0.0, plans/OPTION_C_PLAN.md S11, D28).
    /// </summary>
''')

edit(RG, r'''        ComponentDiagramReportGenerator.DrawnDiagram? componentDiagramDrawn = null,
        bool showScenarioHistory = false,
        WarmUpResult? warmUp = null)
    {
        if (generateBlankOnFailedTests && CannotVouchForSpecifications(features))
''', r'''        ComponentDiagramReportGenerator.DrawnDiagram? componentDiagramDrawn = null,
        bool showScenarioHistory = false,
        WarmUpResult? warmUp = null,
        EmbeddedComponentDiagram? componentDiagram = null)
    {
        if (generateBlankOnFailedTests && CannotVouchForSpecifications(features))
''')

edit(RG, r'''        warmUp ??= trackedLogs is null ? WarmUpResult.None : WarmUpCalls.Find(features, trackedLogs);
        var warmUpByScenario = warmUp.Calls.Values.ToLookup(m => m.ScenarioId, StringComparer.Ordinal);
''', r'''        warmUp ??= trackedLogs is null ? WarmUpResult.None : WarmUpCalls.Find(features, trackedLogs);
        var warmUpByScenario = warmUp.Calls.Values.ToLookup(m => m.ScenarioId, StringComparer.Ordinal);

        // The Components tab's diagram with the parts its controls draw it from: its source is the one the tab draws.
        if (componentDiagram is not null)
            componentDiagramPlantUml = componentDiagram.PlantUml;
''')

# The script, after the timeline's, only where the tab has controls.
edit(RG, r'''        var timelineScript = hasDurations
            ? "<script id=\"kronikol-timeline\">" + LoadResource("report-timeline-script.js") + "</script>"
            : "";
''', r'''        var timelineScript = hasDurations
            ? "<script id=\"kronikol-timeline\">" + LoadResource("report-timeline-script.js") + "</script>"
            : "";
        // the Components tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D28), where the page draws the diagram and the
        // report knows its parts: a diagram the report's renderer drew is a picture the page cannot draw again
        var componentControls = componentDiagram is not null && componentDiagramDrawn is null;
        var componentsScript = componentControls
            ? "<script id=\"kronikol-components\">" + LoadResource("report-components-script.js") + "</script>"
            : "";
''')
edit(RG, '{{scenarioSettingsScript}}{{timelineScript}}\n', '{{scenarioSettingsScript}}{{timelineScript}}{{componentsScript}}\n')

# The tab's markup: heading and controls, the note, the diagram, and the model beside it.
edit(RG, r'''            componentHtml = $"""<div id="component-diagram" class="component-diagram-section">{componentDiagramHtml}</div>""";
        }
''', r'''            // The tab's heading, which is the diagram's title, and where the page draws the diagram from parts the report
            // knows, its controls, the note Filtered writes and the model report-components-script.js draws from (5.0.0,
            // plans/OPTION_C_PLAN.md S11, D26, D28). Show and its note only where the report knows which scenarios made each
            // connection's calls.
            var componentUsage = componentControls ? ComponentUsage(componentDiagram!, features, suite) : null;
            var componentHeader = ComponentHeader(componentDiagram?.Heading ?? EmbeddedComponentDiagram.DefaultHeading, componentControls, componentUsage is not null);
            var componentNote = componentUsage is not null ? TimelineFilteredNote : "";
            componentHtml = $"""<div id="component-diagram" class="component-diagram-section">{componentHeader}{componentNote}{componentDiagramHtml}</div>""";
            if (componentControls)
                componentHtml += $"""<script id="component-diagram-model" type="application/json">{ComponentModelJson(componentDiagram!, componentUsage)}</script>""";
        }
''')

edit(RG, r'''    /// <summary>What Filtered leaves out, said under the timeline's header line while it draws fewer rows than it has
    /// (D27); report-timeline-script.js writes the words.</summary>
    private const string TimelineFilteredNote = "<div class=\"filtered-note\" role=\"status\" hidden></div>";

''', r'''    /// <summary>What Filtered leaves out, said under the timeline's header line while it draws fewer rows than it has
    /// (D27); report-timeline-script.js writes the words. The Components tab says it under its own header line (D28), in
    /// words report-components-script.js writes.</summary>
    private const string TimelineFilteredNote = "<div class=\"filtered-note\" role=\"status\" hidden></div>";

    /// <summary>
    /// The Components tab's header line (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28): its heading, which is the diagram's
    /// title (the drawing leaves the title out), with the timeline's info mark, and where <paramref name="controls"/>, the
    /// tab's controls at the right of the line, in the timeline's groups (<see cref="TimelineSegments"/>): Show, Filtered
    /// or Full, where <paramref name="usage"/> (the report knows which scenarios made each connection's calls); Direction,
    /// Left to right or Top to bottom; and Call summaries, Shown or Hidden. report-components-script.js acts on them.
    /// </summary>
    private static string ComponentHeader(string heading, bool controls, bool usage)
    {
        var info = "The Component Diagram shows the services, databases and queues the scenarios called, and the calls between them. "
            + "Each connection is labelled with the calls made along it and how many tests made them. "
            + (usage ? "Filtered draws only what the scenarios the filters show used; Full draws the whole run. " : "")
            + "Use it to see what a set of tests depends on.";
        var html = new StringBuilder($"<div class=\"timeline-header component-header\">{System.Net.WebUtility.HtmlEncode(heading)} <span class=\"timeline-info\" title=\"{System.Net.WebUtility.HtmlEncode(info)}\">&#x1F6C8;</span>");
        if (!controls)
            return html.Append("</div>").ToString();
        html.Append("<div class=\"timeline-controls\">");
        if (usage)
            html.Append(TimelineSegments("Show", "view",
                ("filtered", "Filtered", "Only the components and connections the scenarios the filters show used"),
                ("full", "Full", "Every component and connection of the run, whatever the filters")));
        html.Append(TimelineSegments("Direction", "dir",
            ("lr", "Left to right", "Each caller to the left of what it calls"),
            ("tb", "Top to bottom", "Each caller above what it calls")));
        html.Append(TimelineSegments("Call summaries", "calls",
            ("shown", "Shown", "Each connection labelled with its calls and how many tests made them"),
            ("hidden", "Hidden", "The connections without their labels")));
        return html.Append("</div></div>").ToString();
    }

    /// <summary>
    /// Which scenarios on the page made the calls each of <paramref name="diagram"/>'s connections draws, for the Components
    /// tab's Filtered (5.0.0, plans/OPTION_C_PLAN.md S11, D28): their stable ids in the page's order, and for each
    /// connection its scenarios as places in that list. Null where that is not known: a connection whose tests the report
    /// was not given, or no connection made by a scenario on the page (the calls carry ids no scenario has), where Filtered
    /// would draw nothing whatever the filters.
    /// </summary>
    private static (string[] StableIds, int[][] ByConnection)? ComponentUsage(EmbeddedComponentDiagram diagram, Feature[] features, string? suite)
    {
        if (diagram.Connections.Any(c => c.TestIds is null))
            return null;
        var used = diagram.Connections.SelectMany(c => c.TestIds!).ToHashSet(StringComparer.Ordinal);
        var stableIds = new List<string>();
        var placeOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var feature in features)
            foreach (var scenario in feature.Scenarios)
                if (used.Contains(scenario.Id) && placeOf.TryAdd(scenario.Id, stableIds.Count))
                    stableIds.Add(ScenarioStableId.Compute(suite, feature.DisplayName, scenario.DisplayName, scenario.OutlineId, scenario.ExampleValues));
        var byConnection = diagram.Connections
            .Select(c => c.TestIds!.Where(placeOf.ContainsKey).Select(t => placeOf[t]).Distinct().OrderBy(i => i).ToArray())
            .ToArray();
        return byConnection.Any(s => s.Length > 0) ? (stableIds.ToArray(), byConnection) : null;
    }

    /// <summary>
    /// The Components tab's model of its diagram, for report-components-script.js (5.0.0, plans/OPTION_C_PLAN.md S11, D28):
    /// the line of the direction, of each component and of each connection in the source the tab draws, each connection's
    /// two components (places in the components) and its line without the label, and where <paramref name="usage"/> is
    /// known, each connection's scenarios and their stable ids. The page reads nothing back from the source or a name.
    /// </summary>
    private static string ComponentModelJson(EmbeddedComponentDiagram diagram, (string[] StableIds, int[][] ByConnection)? usage)
    {
        var connections = diagram.Connections.Select((c, i) =>
        {
            var connection = new Dictionary<string, object> { ["line"] = c.Line, ["from"] = c.From, ["to"] = c.To, ["bare"] = c.Bare };
            if (usage is { } known)
                connection["scenarios"] = known.ByConnection[i];
            return connection;
        }).ToArray();
        var model = new Dictionary<string, object>
        {
            ["direction"] = diagram.DirectionLine,
            ["components"] = diagram.ComponentLines,
            ["connections"] = connections
        };
        if (usage is { } all)
            model["scenarios"] = all.StableIds;
        return System.Text.Json.JsonSerializer.Serialize(model);
    }

''')

# ---------------------------------------------------------------------------------------------------------------------
# 3. The merged report: the same embedded form, with the shards' calls where they carried them.
# ---------------------------------------------------------------------------------------------------------------------
edit(MERGE, r'''        string? componentDiagramPlantUml = null;
        if (report.ComponentRelationships.Length > 0)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            // The report's colours when the component options name none, as the run report and ComponentDiagram.html
            // take them: a merged report drew the default palette for the same options.
            componentOptions.DependencyColors ??= options.DependencyColors;
            // Browser-rendered reports use the non-C4 PlantUML dialect.
            componentDiagramPlantUml = ComponentDiagramGenerator.GeneratePlantUml(report.ComponentRelationships, componentOptions, useC4: false);
        }
''', r'''        EmbeddedComponentDiagram? componentDiagram = null;
        if (report.ComponentRelationships.Length > 0)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            // The report's colours when the component options name none, as the run report and ComponentDiagram.html
            // take them: a merged report drew the default palette for the same options.
            componentOptions.DependencyColors ??= options.DependencyColors;
            // Browser-rendered reports use the non-C4 PlantUML dialect, drawn as a run's Components tab draws it (5.0.0,
            // plans/OPTION_C_PLAN.md S11, D28): its Show control only where the shards carried the calls that say which
            // scenarios made each connection's.
            componentDiagram = ComponentDiagramGenerator.GenerateEmbedded(report.ComponentRelationships, componentOptions, useC4: false,
                logs: report.Interactions.Length > 0 ? report.Interactions : null);
        }
        var componentDiagramPlantUml = componentDiagram?.PlantUml;
''')
edit(MERGE, r'''            componentDiagramPlantUml: componentDiagramPlantUml,
''', r'''            componentDiagramPlantUml: componentDiagramPlantUml,
            componentDiagram: componentDiagram,
''')

# ---------------------------------------------------------------------------------------------------------------------
# 4. The tab's card is the timeline's card (S11d's rule, mock/c-extra.css 103).
# ---------------------------------------------------------------------------------------------------------------------
edit(CSS, r'''           what Filtered leaves out, and every colour is a token. A failed row's name opens with the cross a failed
           scenario's heading shows, so the failure is not told by its colour alone. */
        .scenario-timeline {
''', r'''           what Filtered leaves out, and every colour is a token. A failed row's name opens with the cross a failed
           scenario's heading shows, so the failure is not told by its colour alone. The Components tab's card is the same
           card, with the same header line, controls and note (S11, D26, D28). */
        .scenario-timeline, .component-diagram-section {
''')

# ---------------------------------------------------------------------------------------------------------------------
# 5. The page's half.
# ---------------------------------------------------------------------------------------------------------------------
create('Reports/report-components-script.js', r'''// The Components tab's controls (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28), which the generator writes at the right of
// the tab's header line where the page draws the diagram: Show, Filtered (the start) or Full, where the report knows which
// scenarios made each connection's calls; Direction, Left to right (the start) or Top to bottom; and Call summaries, Shown
// (the start) or Hidden. Filtered draws the connections the scenarios the filters show made, and the components at their
// ends, and says so while the filters hide a scenario; Full draws the whole run. Top to bottom leaves the source's
// direction line out. Hidden writes each connection without its label, once for two components joined in one colour.
// Each form is made from the source as written, which puml-data keeps, with the model the generator writes beside the
// diagram (#component-diagram-model): the line of the direction, of each component and of each connection, each
// connection's two components, its line without the label, and its scenarios by stable id. Nothing is read back from
// the source's text or a name; the preview matched each scenario's dependencies to the names its drawing gave the
// components. Until the page's renderer takes the diagram, the form is written into data-plantuml, which it draws; after,
// a new form is drawn out of sight beside the drawing and swapped in when it is ready, so the drawing stays until then and
// a render that lands after a newer one is dropped. Each form is its own source, so the renderer's cache keeps each, and
// a form drawn before comes back at once.
(function () {
    var KEYS = ['view', 'dir', 'calls'];
    function keyOf(button) {
        for (var i = 0; i < KEYS.length; i++) if (button.hasAttribute('data-' + KEYS[i])) return KEYS[i];
        return null;
    }
    // as the filters leave it: each filter ends by setting display on the scenario elements (applyVisibility)
    function showing(scenario) { return scenario.style.display !== 'none'; }
    // as the page's renderer reads it: a closed <details>' content still has boxes in Chrome
    function isShown(el) { return typeof el.checkVisibility === 'function' ? el.checkVisibility() : el.getClientRects().length > 0; }

    function start() {
        var host = document.getElementById('component-diagram');
        var el = host ? host.querySelector('.plantuml-browser') : null;
        var controls = host ? host.querySelector('.timeline-controls') : null;
        var modelEl = document.getElementById('component-diagram-model');
        if (!el || !controls || !modelEl) return;
        var model;
        try { model = JSON.parse(modelEl.textContent); } catch (e) { return; }
        var note = host.querySelector(':scope > .filtered-note');
        // without a Show group (the report does not know who made which call) the tab draws the whole run
        var state = { view: 'full', dir: 'lr', calls: 'shown' };
        Array.prototype.forEach.call(controls.querySelectorAll('.tl-seg-btn[aria-pressed="true"]'), function (b) {
            var key = keyOf(b);
            if (key) state[key] = b.getAttribute('data-' + key);
        });
        // the elements the filters show and hide, by stable id: a scenario, or the parameterized group around an
        // example's row (which the flat table and the grouped one both carry)
        var byId = {};
        Array.prototype.forEach.call(document.querySelectorAll('[data-stable-id]'), function (e) {
            var scenario = e.closest('details.scenario');
            if (!scenario) return;
            var id = e.getAttribute('data-stable-id'), list = byId[id] || (byId[id] = []);
            if (list.indexOf(scenario) < 0) list.push(scenario);
        });
        var stableIds = model.scenarios || [];
        function used(connection) {
            return (connection.scenarios || []).some(function (i) { return (byId[stableIds[i]] || []).some(showing); });
        }
        // the scenarios the filters show, and all of them, in runs, as the filter readout counts them
        function counts() {
            var shown = 0, total = 0, features = document.getElementsByClassName('feature');
            for (var f = 0; f < features.length; f++) {
                var scenarios = features[f].getElementsByClassName('scenario');
                for (var s = 0; s < scenarios.length; s++) {
                    var runs = parseInt(scenarios[s].getAttribute('data-runs') || '1', 10) || 1;
                    total += runs;
                    if (showing(scenarios[s])) shown += runs;
                }
            }
            return { shown: shown, total: total };
        }

        var lines = null;    // the source as written, line by line
        var wanted = null;   // the form the controls and the filters ask for
        var drawn = null;    // the form the drawing shows, once the renderer has drawn it
        var asked = null;    // the form being drawn out of sight, while one is
        var generation = 0;  // the newest form asked for: a render that lands after a newer one is dropped

        // the form the controls and the filters ask for, and what the note says of it
        function form() {
            var count = counts();
            var filtering = state.view === 'filtered' && count.shown < count.total;
            var drop = {}, ends = {}, kept = 0;
            model.connections.forEach(function (c) {
                if (filtering && !used(c)) { drop[c.line] = true; return; }
                kept++;
                ends[c.from] = ends[c.to] = true;
            });
            if (filtering) model.components.forEach(function (line, i) { if (!ends[i]) drop[line] = true; });
            if (state.dir === 'tb') drop[model.direction] = true;
            var bare = {};
            if (state.calls === 'hidden') model.connections.forEach(function (c) { bare[c.line] = c.bare; });
            var out = [], seen = {};
            for (var i = 0; i < lines.length; i++) {
                if (drop[i]) continue;
                var line = lines[i];
                if (bare[i] !== undefined) {
                    // two connections joining one pair in one colour are one line once their labels are gone
                    if (seen[bare[i]]) continue;
                    seen[bare[i]] = true;
                    line = bare[i] + (line.charAt(line.length - 1) === '\r' ? '\r' : '');
                }
                out.push(line);
            }
            return { source: out.join('\n'), filtering: filtering, shown: count.shown, empty: kept === 0 };
        }

        function tell(f) {
            if (!note) return;
            note.hidden = !f.filtering;
            if (!f.filtering) { note.textContent = ''; return; }
            var who = f.shown === 1 ? 'the 1 scenario that matches the filters' : 'the ' + f.shown + ' scenarios that match the filters';
            note.textContent = f.shown === 0 ? 'Filtered: no scenario matches the filters.'
                : f.empty ? 'Filtered: ' + who + ' made none of the calls this diagram draws.'
                : 'Filtered: only the components and connections used by ' + who + '.'
                    + (state.calls === 'shown' ? ' The call counts on the connections are for the whole run.' : '');
        }

        // draws a form in a hidden child of the diagram's element, as the note toggles redraw a diagram, and swaps it in
        function redraw(source) {
            var mine = ++generation;
            asked = source;
            window._renderTmpCounter = (window._renderTmpCounter || 0) + 1;
            var target = document.createElement('div');
            target.id = el.id + '-render-tmp-' + window._renderTmpCounter;
            target.style.display = 'none';
            el.appendChild(target);
            var done = false, watch = null, timer = null;
            function finish(swap) {
                if (done) return;
                done = true;
                if (watch) watch.disconnect();
                clearTimeout(timer);
                if (mine === generation) asked = null;
                if (!swap || mine !== generation) {
                    if (target.parentNode) target.parentNode.removeChild(target);
                    return;
                }
                if (window._describeEngineFailure) window._describeEngineFailure(target, source);
                el.innerHTML = target.innerHTML;
                el.setAttribute('data-plantuml', source);
                drawn = source;
                if (window._iflowBindLinks) window._iflowBindLinks(el, source);
                requestAnimationFrame(function () { if (window._addZoomButton) window._addZoomButton(el); });
            }
            // an <svg>, or the engine's failure written as text, completes it
            watch = new MutationObserver(function () {
                if (target.querySelector('svg') || (target.textContent || '').trim()) finish(true);
            });
            watch.observe(target, { childList: true, subtree: true });
            timer = setTimeout(function () { finish(false); }, 60000);
            try { window.plantuml.render(source.split('\n'), target.id); } catch (e) { finish(false); }
        }

        function sync() {
            if (!lines) return;
            var f = form();
            tell(f);
            // no scenario the filters show made a call this diagram draws: the drawing is put away, and the note says why
            el.hidden = f.empty;
            if (f.empty) return;
            wanted = f.source;
            // not taken by the renderer yet: it draws what data-plantuml holds when it takes the diagram
            if (!el.dataset.queued) { el.setAttribute('data-plantuml', wanted); return; }
            // the renderer's first drawing is on its way: compared again when it lands
            if (drawn === null) return;
            if (wanted === drawn) {
                // back to what shows: a form on its way is dropped when it lands
                if (asked !== null) { generation++; asked = null; }
                return;
            }
            if (wanted === asked) return;
            // drawn now if the diagram shows, else when it next does
            if (isShown(el)) redraw(wanted);
        }

        // the renderer's first drawing: it drew what data-plantuml held when it took the diagram
        function landed() {
            if (drawn !== null || el.dataset.rendered !== '1') return;
            drawn = el.getAttribute('data-plantuml');
            sync();
        }
        new MutationObserver(landed).observe(el, { attributes: true, attributeFilter: ['data-rendered'] });
        // a change made while the tab was hidden is drawn when the diagram next shows
        if (window.IntersectionObserver) new IntersectionObserver(function (entries) {
            if (entries.some(function (e) { return e.isIntersecting; })) sync();
        }).observe(host);

        controls.addEventListener('click', function (e) {
            var button = e.target && e.target.closest ? e.target.closest('.tl-seg-btn') : null;
            var key = button ? keyOf(button) : null;
            if (!key) return;
            var value = button.getAttribute('data-' + key);
            if (state[key] === value) return;
            state[key] = value;
            Array.prototype.forEach.call(button.parentNode.querySelectorAll('.tl-seg-btn'), function (b) {
                b.setAttribute('aria-pressed', b === button ? 'true' : 'false');
            });
            sync();
        });
        // a change of the filters, while Filtered shows, once they settle: typing in the search draws once
        var settle = null;
        document.addEventListener('kron-filter-change', function () {
            if (state.view !== 'filtered') return;
            clearTimeout(settle);
            settle = setTimeout(sync, 150);
        });

        // the source as written, from the entry the renderer reads (a link's filters were applied before this ran)
        var z = window._getPumlZ ? window._getPumlZ(el) : null;
        if (!z || typeof window.decompressGzipBase64 !== 'function') return;
        window.decompressGzipBase64(z).then(function (source) {
            lines = source.split('\n');
            landed();
            sync();
        }).catch(function (e) { console.error('Kronikol: the component diagram could not be read:', e); });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
''')

changed = sorted(p for p in files if files[p] != originals[p])
for path in changed:
    open(R + path, 'w', encoding='utf-8', newline='').write(files[path])
for path, content in created.items():
    open(R + path, 'w', encoding='utf-8', newline='').write(content)
print('ok (' + STAGE + '):', ', '.join(changed + sorted(created)))
