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

            if (useC4)
            {
                if (isPureCaller)
                    sb.AppendLine($"Person({alias}, \"{participant}\")");
                else
                    sb.AppendLine(GetC4SystemDeclaration(depType, alias, participant));
            }
            else
            {
                if (isPureCaller)
                {
                    sb.AppendLine($"rectangle \"**{participant}**\\n<size:10>[Person]</size>\" as {alias} <<person>>");
                }
                else
                {
                    var shape = GetComponentShape(depType);
                    if (shape == "rectangle")
                        sb.AppendLine($"rectangle \"**{participant}**\\n<size:10>[Software System]</size>\" as {alias} <<system>>");
                    else
                        sb.AppendLine($"{shape} \"{participant}\" as {alias}");
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

    private static string GetComponentShape(DependencyType type) => type switch
    {
        DependencyType.Database => "database",
        DependencyType.Storage => "database",
        DependencyType.Cache => "collections",
        DependencyType.MessageQueue => "queue",
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
