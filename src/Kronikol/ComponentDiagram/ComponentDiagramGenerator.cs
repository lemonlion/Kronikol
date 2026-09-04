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
    internal const int MaxLabelLineChars = 100;

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
    internal const int MaxNameLineChars = 80;

    /// <summary>Characters that mean a word is creole markup — a link, a tag — and must never be cut.</summary>
    private static readonly char[] UnsplittableMarkup = ['[', ']', '<', '>', '\\'];

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
        sb.AppendLine($"title {options.Title}");
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

            string label;
            if (options.RelationshipLabelFormatter is not null)
            {
                label = options.RelationshipLabelFormatter(rel);
            }
            else if (stats != null && stats.TryGetValue(relKey, out var relStats))
            {
                var methodsPart = rel.Protocol == DependencyCategories.HTTP
                    ? $"HTTP: {string.Join(", ", rel.Methods.OrderBy(m => m))}"
                    : $"{rel.Protocol}: {string.Join(", ", rel.Methods.OrderBy(m => m))}";

                var statsPart = $"P50: {relStats.MedianMs:F0}ms | P95: {relStats.P95Ms:F0}ms | P99: {relStats.P99Ms:F0}ms";

                var errorPart = relStats.ErrorRate > 0
                    ? $" | {relStats.ErrorRate * 100:F0}% errors"
                    : "";

                label = $"[[#iflow-rel-{ComponentFlowSegmentBuilder.SanitizeKey(rel.Caller)}-{ComponentFlowSegmentBuilder.SanitizeKey(rel.Service)} {methodsPart}]]\\n{statsPart}{errorPart}\\n{rel.CallCount} calls across {rel.TestCount} tests";
            }
            else
            {
                var methodsPart = rel.Protocol == DependencyCategories.HTTP
                    ? $"HTTP: {string.Join(", ", rel.Methods.OrderBy(m => m))}"
                    : $"{rel.Protocol}: {string.Join(", ", rel.Methods.OrderBy(m => m))}";
                label = $"{methodsPart} - {rel.CallCount} calls across {rel.TestCount} tests";
            }

            // An edge label grows with the method list and with whatever a RelationshipLabelFormatter
            // returns. Component diagrams use a different parser from sequence diagrams and its limits are
            // unmeasured, so this is a defensive ceiling at the sequence-diagram message limit rather than
            // a measured one: real labels are two orders of magnitude below it, and an over-long statement
            // costs the whole diagram, not the one edge. The allowance covers both emitted forms —
            // `caller -[#colour]-> service : "…"` and C4's `Rel(caller, service, "…", $tags="#colour")`.
            // Wrapped before it is capped, not after: the `\n` escapes are two characters each and count
            // toward the statement the parser measures, so capping the wrapped label is what actually
            // keeps the emitted line inside the limit.
            label = WrapLabel(label);

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

        return sb.ToString();
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
    /// A participant's name, broken onto display lines of at most <see cref="MaxNameLineChars"/>
    /// characters. Unchanged — byte for byte — for every name short enough to fit, which is all of them
    /// in practice.
    /// </summary>
    private static string WrapName(string name) => Wrap(name, MaxNameLineChars);

    /// <summary>
    /// Wraps each of <paramref name="text"/>'s existing display lines to <paramref name="budget"/>
    /// characters, and returns it unchanged when they all already fit.
    /// </summary>
    private static string Wrap(string text, int budget)
    {
        if (text.Length <= budget)
            return text;

        var lines = text.Split("\\n");
        var wrapped = new List<string>(lines.Length);
        var changed = false;

        foreach (var line in lines)
        {
            var before = wrapped.Count;
            WrapOneLine(line, budget, wrapped);
            changed |= wrapped.Count - before != 1 || wrapped[before] != line;
        }

        return changed ? string.Join("\\n", wrapped) : text;
    }

    /// <summary>
    /// Re-opens creole bold on each display line of <paramref name="name"/>. Creole bold is line-scoped:
    /// <c>**a\nb**</c> loses the weight on every line <em>and</em> draws a literal <c>**</c> at the end,
    /// so the markers have to be closed and reopened rather than wrapped around the whole name. A name
    /// that did not wrap comes back as the <c>**name**</c> it always was.
    /// </summary>
    private static string BoldPerLine(string name) =>
        "**" + string.Join("**\\n**", name.Split("\\n")) + "**";

    /// <summary>Appends <paramref name="line"/> to <paramref name="into"/>, broken between its atoms.</summary>
    private static void WrapOneLine(string line, int budget, List<string> into)
    {
        if (line.Length <= budget)
        {
            into.Add(line);
            return;
        }

        var current = new StringBuilder();

        foreach (var atom in Atoms(line, budget))
        {
            if (current.Length > 0 && current.Length + 1 + atom.Length > budget)
            {
                into.Add(current.ToString());
                current.Clear();
            }

            if (atom.Length <= budget || !CanSplit(atom))
            {
                if (current.Length > 0) current.Append(' ');
                current.Append(atom);
                continue;
            }

            // A word no line could hold and no space in it to break at. Cutting it is the only way to
            // bound the width, and it is safe precisely because CanSplit ruled out markup. The pieces
            // are whole lines rather than words, so no space is introduced into the middle of a word.
            if (current.Length > 0)
            {
                into.Add(current.ToString());
                current.Clear();
            }

            for (var at = 0; at < atom.Length; at += budget)
            {
                var piece = atom.Substring(at, Math.Min(budget, atom.Length - at));
                if (at + budget < atom.Length)
                    into.Add(piece);
                else
                    current.Append(piece); // the tail carries on, so the next atom can join it
            }
        }

        if (current.Length > 0)
            into.Add(current.ToString());
    }

    /// <summary>
    /// The units a label line may be broken between, largest first: one per comma-separated item — the
    /// method list is the only thing in these labels long enough to wrap, and a line of whole operations
    /// is what makes the overview readable — falling back to words inside an item too long to stand alone.
    /// </summary>
    private static IEnumerable<string> Atoms(string line, int budget)
    {
        foreach (var item in CommaSeparatedItems(line))
        {
            if (item.Length <= budget)
            {
                yield return item;
                continue;
            }

            foreach (var word in item.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                yield return word;
        }
    }

    /// <summary>Splits on <c>", "</c>, keeping each comma with the item it terminates.</summary>
    private static IEnumerable<string> CommaSeparatedItems(string line)
    {
        var start = 0;
        for (var i = 0; i + 1 < line.Length; i++)
        {
            if (line[i] != ',' || line[i + 1] != ' ') continue;
            yield return line[start..(i + 1)];
            start = i + 2;
        }

        if (start < line.Length)
            yield return line[start..];
    }

    /// <summary>
    /// Whether a word can be cut mid-way. Creole markup cannot: half of <c>[[#anchor</c> is literal text
    /// and half of <c>&lt;size:10&gt;</c> is a broken tag, and a stranded <c>\</c> would eat the <c>n</c>
    /// of the break that follows it.
    /// </summary>
    private static bool CanSplit(string word) => word.IndexOfAny(UnsplittableMarkup) < 0;

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
        SanitizeAliasRegex().Replace(name.Camelize(), "_");
}
