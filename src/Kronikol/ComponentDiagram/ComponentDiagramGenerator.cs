using Kronikol.Constants;
using System.Text;
using System.Text.RegularExpressions;
using Kronikol.Tracking;

namespace Kronikol.ComponentDiagram;

/// <summary>
/// Generates PlantUML component diagram source from extracted service relationships.
/// Supports both plain PlantUML and C4 diagram styles.
/// </summary>
public static partial class ComponentDiagramGenerator
{
    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex SanitizeAliasRegex();

    /// <summary>
    /// Longest display line an edge label is drawn on before the generator breaks it with a <c>\n</c>.
    /// <para>
    /// This exists because the two engines Kronikol renders through disagree: the TeaVM JavaScript build
    /// used for <c>BrowserJs</c>/<c>NodeJs</c> honours <c>skinparam wrapWidth</c> on an arrow label, and
    /// <b>real Java PlantUML does not wrap arrow labels at all</b> — the same source that drew 840×863 in
    /// a report drew 6697×586 through the server renderer, past the 4096-pixel <c>PLANTUML_LIMIT_SIZE</c>
    /// that plantuml.com and every default install crop at. The crop is silent: PlantUML keeps the
    /// top-left corner and discards the rest, so a user sees an architecture overview with its
    /// dependencies missing and no error to explain it (user-reported). The line breaks therefore have to
    /// be in the source, not delegated to a skinparam.
    /// </para>
    /// <para>
    /// 100 characters is roughly 600 pixels at the arrow font size — wide enough that ordinary labels
    /// ("HTTP: GET, POST - 252 calls across 241 tests") are untouched and keep their exact bytes, and
    /// narrow enough that a label at the <see cref="PlantUml.PlantUmlStatementLimits.MaxMessageStatementChars"/>
    /// ceiling draws as about twenty short lines instead of one very long one. It does not fight
    /// <c>wrapWidth</c> on the JS side: that engine re-wraps each of these lines to 200 pixels anyway, so
    /// the browser report looks as it always did.
    /// </para>
    /// </summary>
    internal const int MaxLabelLineChars = PlantUml.DiagramWidth.MaxLabelLineChars;

    /// <summary>
    /// Longest display line a participant's name is drawn on. This is the diagram's other unbounded width
    /// axis, and the narrower one: measured against real PlantUML a <c>&lt;&lt;system&gt;&gt;</c> rectangle
    /// grows at about 7.3 pixels per character and crops at around 528, and the hexagon an
    /// <see cref="DependencyType.AI"/> dependency draws as grows at about 14 and crops at around 284.
    /// <c>skinparam wrapWidth</c> is no defence here either — it only breaks at whitespace, and the names
    /// that get long (hosts, fully-qualified type names, connection descriptors) have none.
    /// <para>
    /// 80 is well past any real service name, so no existing diagram changes, and it pins the widest shape
    /// at roughly 1100 pixels however long the name gets.
    /// </para>
    /// </summary>
    internal const int MaxNameLineChars = PlantUml.DiagramWidth.MaxNameLineChars;

    public static ComponentRelationship[] ExtractRelationships(
        IEnumerable<RequestResponseLog> logs,
        Func<string, bool>? participantFilter = null)
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

        var groups = filtered.GroupBy(log => (log.CallerName, log.ServiceName, Protocol: GetProtocol(log)));

        return groups.Select(g =>
        {
            var methods = new HashSet<string>(g.Select(log => GetMethodName(log)));
            var callCount = g.Count();
            var testCount = g.Select(log => log.TestId).Distinct().Count();
            var dependencyCategory = g.Select(log => log.DependencyCategory).FirstOrDefault(c => c is not null);
            return new ComponentRelationship(g.Key.CallerName, g.Key.ServiceName, g.Key.Protocol, methods, callCount, testCount, dependencyCategory);
        }).ToArray();
    }

    public static string GeneratePlantUml(
        ComponentRelationship[] relationships,
        ComponentDiagramOptions? options = null,
        Dictionary<string, RelationshipStats>? stats = null,
        bool useC4 = true)
    {
        options ??= new ComponentDiagramOptions();
        var sb = new StringBuilder();

        // Build service → DependencyType map from relationships
        var serviceTypes = new Dictionary<string, DependencyType>();
        foreach (var rel in relationships)
        {
            if (!serviceTypes.ContainsKey(rel.Service))
            {
                var type = DependencyPalette.Resolve(rel.DependencyCategory);
                serviceTypes[rel.Service] = type;
            }
        }

        sb.AppendLine("@startuml");
        sb.AppendLine("left to right direction");

        if (useC4)
        {
            sb.AppendLine("!include <C4/C4_Context>");
        }
        else
        {
            sb.AppendLine("skinparam defaultTextAlignment center");
            sb.AppendLine("skinparam wrapWidth 200");
            sb.AppendLine("skinparam shadowing false");
            sb.AppendLine("skinparam rectangle<<person>> {");
            sb.AppendLine("  BackgroundColor #08427B");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #073B6F");
            sb.AppendLine("  RoundCorner 25");
            sb.AppendLine("  StereotypeFontColor #08427B");
            sb.AppendLine("  StereotypeFontSize 0");
            sb.AppendLine("}");
            sb.AppendLine("skinparam rectangle<<system>> {");
            sb.AppendLine("  BackgroundColor #438DD5");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #3C7FC0");
            sb.AppendLine("  RoundCorner 25");
            sb.AppendLine("  StereotypeFontColor #438DD5");
            sb.AppendLine("  StereotypeFontSize 0");
            sb.AppendLine("}");
            sb.AppendLine("skinparam database {");
            sb.AppendLine("  BackgroundColor #E74C3C");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #C0392B");
            sb.AppendLine("}");
            sb.AppendLine("skinparam collections {");
            sb.AppendLine("  BackgroundColor #F39C12");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #D68910");
            sb.AppendLine("}");
            sb.AppendLine("skinparam queue {");
            sb.AppendLine("  BackgroundColor #9B59B6");
            sb.AppendLine("  FontColor #FFFFFF");
            sb.AppendLine("  BorderColor #7D3C98");
            sb.AppendLine("}");
            sb.AppendLine("skinparam arrow {");
            sb.AppendLine("  Color #666666");
            sb.AppendLine("  FontColor #666666");
            sb.AppendLine("  FontSize 11");
            sb.AppendLine("}");
        }

        if (!string.IsNullOrWhiteSpace(options.PlantUmlTheme))
            sb.AppendLine($"!theme {options.PlantUmlTheme}");

        sb.AppendLine();
        // A title does not wrap at any width, at any skinparam, on either engine: a user-configured
        // title is one more unbounded width axis, so it takes the same budget an edge label does.
        sb.AppendLine($"title {WrapLabel(options.Title)}");
        sb.AppendLine();

        // Discover all unique participants
        var allCallers = new HashSet<string>(relationships.Select(r => r.Caller));
        var allServices = new HashSet<string>(relationships.Select(r => r.Service));
        var pureCallers = new HashSet<string>(allCallers.Except(allServices)); // membership only

        // Deterministic first-seen order (callers, then services) — parity-hardening: a HashSet's
        // iteration order is not stable across runtimes/process runs, which would desync golden
        // fixtures and the periodic cross-runtime parity-diff (JAVA_PORT_PLAN §6.5, HIGH hazard).
        var allParticipants = relationships.Select(r => r.Caller)
            .Concat(relationships.Select(r => r.Service))
            .Distinct()
            .ToList();

        foreach (var participant in allParticipants)
        {
            var alias = SanitizeAlias(participant);
            var isPureCaller = pureCallers.Contains(participant);
            var depType = serviceTypes.GetValueOrDefault(participant, DependencyType.HttpApi);

            var name = WrapName(participant);

            if (useC4)
            {
                // The C4 macros apply the bold through the style rather than through creole, so a name
                // broken across lines needs no marker of its own here.
                if (isPureCaller)
                    sb.AppendLine($"Person({alias}, \"{name}\")");
                else
                    sb.AppendLine(GetC4SystemDeclaration(depType, alias, name));
            }
            else
            {
                var bold = BoldPerLine(name);
                if (isPureCaller)
                {
                    sb.AppendLine($"rectangle \"{bold}\\n<size:10>[Person]</size>\" as {alias} <<person>>");
                }
                else
                {
                    var shape = GetComponentShape(depType);
                    if (shape == "rectangle")
                        sb.AppendLine($"rectangle \"{bold}\\n<size:10>[Software System]</size>\" as {alias} <<system>>");
                    else
                        sb.AppendLine($"{shape} \"{name}\" as {alias}");
                }
            }
        }

        sb.AppendLine();

        foreach (var rel in relationships)
        {
            var callerAlias = SanitizeAlias(rel.Caller);
            var serviceAlias = SanitizeAlias(rel.Service);
            var relKey = $"iflow-rel-{ComponentFlowSegmentBuilder.SanitizeKey(rel.Caller)}-{ComponentFlowSegmentBuilder.SanitizeKey(rel.Service)}";

            // An edge label grows with the method list and with whatever a RelationshipLabelFormatter returns, and
            // the engine walks the whole label once per character on its stack: a render worker with V8's optimizing
            // compilers off ran out of stack from 550 characters and drew a stack-overflow picture in place of the
            // whole diagram (#162). So the label, as written, is held to MaxComponentEdgeLabelChars: the method list
            // loses whole entries from its end, keeping the counts and the stats, and a formatter's label, which is
            // the consumer's own format, is cut at its end. Wrapped before it is measured: the `\n` escapes count
            // toward what the engine walks. The same label goes into both emitted forms, `caller -[#colour]->
            // service : "…"` and C4's `Rel(caller, service, "…", $tags="#colour")`.
            var linkOpen = $"[[#{relKey} ";
            string label;
            if (options.RelationshipLabelFormatter is not null)
            {
                label = CapLinkText(WrapLabel(options.RelationshipLabelFormatter(rel)), linkOpen);
                label = PlantUml.PlantUmlStatementLimits.TruncateLabel(label, PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars);
            }
            else if (stats != null && stats.TryGetValue(relKey, out var relStats))
            {
                var statsPart = $"P50: {relStats.MedianMs:F0}ms | P95: {relStats.P95Ms:F0}ms | P99: {relStats.P99Ms:F0}ms";

                var errorPart = relStats.ErrorRate > 0
                    ? $" | {relStats.ErrorRate * 100:F0}% errors"
                    : "";

                label = FitMethodList(rel, methodsPart =>
                    $"{linkOpen}{methodsPart}]]\\n{statsPart}{errorPart}\\n{rel.CallCount} calls across {rel.TestCount} tests", linkOpen);
            }
            else
            {
                label = FitMethodList(rel, methodsPart => $"{methodsPart} - {rel.CallCount} calls across {rel.TestCount} tests");
            }

            // The sequence-diagram message limit stays behind it as a backstop for the whole statement.
            var edgeOverhead = callerAlias.Length + serviceAlias.Length + 40;
            label = PlantUml.PlantUmlStatementLimits.TruncateLabel(
                label, PlantUml.PlantUmlStatementLimits.MaxMessageStatementChars - edgeOverhead);

            // Determine arrow style
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

        sb.AppendLine();
        sb.AppendLine("@enduml");

        // Names and labels come from the captured calls (an ingested run's from its files): nothing XML cannot hold.
        return PlantUml.PlantUmlCreator.ReplaceXmlInvalidCharacters(sb.ToString());
    }

    /// <summary>
    /// Breaks <paramref name="label"/> onto display lines of at most <see cref="MaxLabelLineChars"/>
    /// characters, so that the engines which do not wrap arrow labels draw a block of text rather than
    /// one line as wide as the label is long. Returns the label unchanged when every line already fits —
    /// which is nearly always, and is what keeps ordinary diagrams byte-identical.
    /// <para>
    /// The label's own <c>\n</c> escapes are structure (the stats label is deliberately three lines), so
    /// each of them is wrapped independently and the breaks between them are preserved exactly.
    /// </para>
    /// </summary>
    private static string WrapLabel(string label) => Wrap(label, MaxLabelLineChars);

    /// <summary>
    /// The edge's label, as <paramref name="build"/> writes it around the method list, with as many of the
    /// relationship's operations as fit under <see cref="PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars"/>
    /// once wrapped (every display break counted). A list that does not fit loses whole entries from its end and says
    /// how many, <c>…, +38 more</c>, so what <paramref name="build"/> puts after the list is kept: the call and test
    /// counts, and the stats form's closed link and stats lines. When not even the first entry fits whole, it is cut
    /// with <see cref="PlantUml.PlantUmlStatementLimits.TruncateLabel"/>, its own marker standing for the rest. A label
    /// that fits comes back exactly as it was.
    /// </summary>
    private static string FitMethodList(ComponentRelationship rel, Func<string, string> build, string? linkOpen = null)
    {
        var cap = PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars;
        var marker = PlantUml.PlantUmlStatementLimits.TruncationMarker;
        var methods = rel.Methods.OrderBy(m => m).ToArray();
        var head = $"{rel.Protocol}: ";
        string Written(string listing)
        {
            var label = WrapLabel(build(listing));
            return linkOpen is null ? label : CapLinkText(label, linkOpen);
        }

        var whole = Written(head + string.Join(", ", methods));
        if (whole.Length <= cap || methods.Length == 0)
            return whole.Length <= cap ? whole : PlantUml.PlantUmlStatementLimits.TruncateLabel(whole, cap);

        // A wrap only lengthens a label (a break takes a space's place, or splits a word with two characters), so a
        // count whose unwrapped label is over the cap is passed over without building it.
        var frame = build(string.Empty).Length;
        var joined = new int[methods.Length + 1];
        for (var i = 0; i < methods.Length; i++)
            joined[i + 1] = joined[i] + (i > 0 ? 2 : 0) + methods[i].Length;
        for (var kept = methods.Length - 1; kept >= 1; kept--)
        {
            var more = $", +{methods.Length - kept} more";
            if (frame + head.Length + joined[kept] + 2 + marker.Length + more.Length > cap)
                continue;
            var candidate = Written($"{head}{string.Join(", ", methods.Take(kept))}, {marker}{more}");
            if (candidate.Length <= cap)
                return candidate;
        }

        string FirstCut(int length) => head + PlantUml.PlantUmlStatementLimits.TruncateLabel(methods[0], length)
            + (methods.Length > 1 ? $", +{methods.Length - 1} more" : string.Empty);
        string? fitted = null;
        for (int low = 1, high = methods[0].Length - 1; low <= high;)
        {
            var length = (low + high) / 2;
            var candidate = Written(FirstCut(length));
            if (candidate.Length <= cap) { fitted = candidate; low = length + 1; }
            else high = length - 1;
        }
        // Only a protocol name or stats text as long as the cap leaves no room for any of the list.
        return fitted ?? PlantUml.PlantUmlStatementLimits.TruncateLabel(whole, cap);
    }

    /// <summary>
    /// Cuts the text of the edge's internal-flow link, the method list, to
    /// <see cref="PlantUml.PlantUmlStatementLimits.MaxLinkedLabelChars"/> as written, display breaks included,
    /// and keeps the link closed and the stats lines after it. A Chromium worker, where BrowserJs renders,
    /// overflows its stack parsing a longer link and draws nothing of the diagram. Since 4.14.5 the whole label is held
    /// to the shorter <see cref="PlantUml.PlantUmlStatementLimits.MaxComponentEdgeLabelChars"/>
    /// (<see cref="FitMethodList"/>), which the engine walks link or no link, so for the method list this cap no longer
    /// binds; it stays for a formatter's label that writes the link itself. A label without the link comes back unchanged.
    /// </summary>
    private static string CapLinkText(string label, string linkOpen)
    {
        if (!label.StartsWith(linkOpen, StringComparison.Ordinal))
            return label;
        var close = label.IndexOf("]]", linkOpen.Length, StringComparison.Ordinal);
        if (close < 0)
            return label;
        var text = label[linkOpen.Length..close];
        var capped = PlantUml.PlantUmlStatementLimits.TruncateLabel(text, PlantUml.PlantUmlStatementLimits.MaxLinkedLabelChars);
        return capped.Length == text.Length ? label : string.Concat(linkOpen, capped, label.AsSpan(close));
    }

    /// <summary>
    /// A participant's name, cut to <see cref="PlantUml.PlantUmlStatementLimits.MaxParticipantNameChars"/> (its alias
    /// keeps it distinct, <see cref="PlantUml.PlantUmlStatementLimits.AliasSource"/>) and broken onto display lines of at
    /// most <see cref="MaxNameLineChars"/> characters. Unchanged — byte for byte — for every name short enough to fit,
    /// which is all of them in practice.
    /// </summary>
    private static string WrapName(string name) => Wrap(PlantUml.PlantUmlStatementLimits.CapName(name), MaxNameLineChars);

    /// <summary>
    /// Wraps each of <paramref name="text"/>'s existing display lines to <paramref name="budget"/>
    /// characters, and returns it unchanged when they all already fit. The wrapper itself is shared
    /// with the sequence, activity and marker-note emitters — see <see cref="PlantUml.DiagramWidth"/>,
    /// which records why <c>skinparam wrapWidth</c> cannot stand in for it.
    /// </summary>
    private static string Wrap(string text, int budget) => PlantUml.DiagramWidth.Wrap(text, budget);

    /// <summary>
    /// Re-opens creole bold on each display line of <paramref name="name"/>. Creole bold is line-scoped:
    /// <c>**a\nb**</c> loses the weight on every line <em>and</em> draws a literal <c>**</c> at the end,
    /// so the markers have to be closed and reopened rather than wrapped around the whole name. A name
    /// that did not wrap comes back as the <c>**name**</c> it always was.
    /// </summary>
    private static string BoldPerLine(string name) =>
        "**" + string.Join("**\\n**", name.Split("\\n")) + "**";

    private static string GetComponentShape(DependencyType type) => type switch
    {
        DependencyType.Database => "database",
        DependencyType.Storage => "database",
        DependencyType.Cache => "collections",
        DependencyType.MessageQueue => "queue",
        DependencyType.AI => "hexagon",
        _ => "rectangle"
    };

    private static string GetC4SystemDeclaration(DependencyType type, string alias, string name) => type switch
    {
        DependencyType.Database or DependencyType.Storage => $"SystemDb({alias}, \"{name}\")",
        DependencyType.MessageQueue => $"SystemQueue({alias}, \"{name}\")",
        _ => $"System({alias}, \"{name}\")"
    };

    private static string GetProtocol(RequestResponseLog log)
    {
        if (log.DependencyCategory is not null)
            return log.DependencyCategory;

        return log.MetaType == RequestResponseMetaType.Event
            ? log.Method.Value?.ToString() ?? "Event"
            : DependencyCategories.HTTP;
    }

    private static string GetMethodName(RequestResponseLog log) =>
        log.Method.Value?.ToString() ?? "Unknown";

    private static string SanitizeAlias(string name) =>
        SanitizeAliasRegex().Replace(PlantUml.PlantUmlStatementLimits.AliasSource(name).Camelize(), "_");
}
