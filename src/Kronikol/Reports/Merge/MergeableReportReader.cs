using System.Globalization;
using System.Text.Json;
using Kronikol.ComponentDiagram;
using static Kronikol.DefaultDiagramsFetcher;

namespace Kronikol.Reports.Merge;

/// <summary>
/// Parses an enriched "mergeable" test-run report JSON file (produced with
/// <see cref="ReportConfigurationOptions.GenerateMergeableData"/>) back into a <see cref="MergeableReport"/>.
/// </summary>
public static class MergeableReportReader
{
    /// <summary>Reads and parses a mergeable report from a file path.</summary>
    public static MergeableReport ReadFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses a mergeable report from a JSON string.</summary>
    public static MergeableReport Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("features", out _))
            throw new FormatException("Not a recognised Kronikol test-run report (missing 'features').");

        if (!root.TryGetProperty("mergeableFormatVersion", out _) && !root.TryGetProperty("componentRelationships", out _))
            throw new FormatException(
                "This report was not produced with GenerateMergeableData enabled, so it lacks the data needed to " +
                "reconstruct a full combined report. Re-run the tests with ReportConfigurationOptions.GenerateMergeableData = true.");

        var features = new List<Feature>();
        var diagrams = new List<DiagramAsCode>();

        foreach (var fe in EnumerateArray(root, "features"))
        {
            var scenarios = new List<Scenario>();
            foreach (var se in EnumerateArray(fe, "scenarios"))
            {
                var scenario = ReadScenario(se);
                scenarios.Add(scenario);

                if (se.TryGetProperty("diagrams", out var diags) && diags.ValueKind == JsonValueKind.Array)
                    foreach (var d in diags.EnumerateArray())
                        diagrams.Add(new DiagramAsCode(scenario.Id, "", d.GetString() ?? ""));
            }

            features.Add(new Feature
            {
                DisplayName = GetString(fe, "name") ?? "",
                Endpoint = GetString(fe, "endpoint"),
                Description = GetString(fe, "description"),
                Labels = ReadStringArray(fe, "labels"),
                Scenarios = scenarios.ToArray()
            });
        }

        return new MergeableReport
        {
            KronikolVersion = GetString(root, "kronikolVersion") ?? "",
            StartTime = ReadDate(root, "startTime"),
            EndTime = ReadDate(root, "endTime"),
            Features = features.ToArray(),
            Diagrams = diagrams.ToArray(),
            ComponentRelationships = ReadRelationships(root),
            InternalFlowSegments = ReadObjectMap(root, "internalFlowSegments"),
            WholeTestFlow = ReadWholeTestFlow(root),
            WholeTestVisualization = ReadEnum(GetString(root, "wholeTestVisualization"), WholeTestFlowVisualization.None),
            CiMetadata = ReadCiMetadata(root)
        };
    }

    private static Scenario ReadScenario(JsonElement se) => new()
    {
        Id = GetString(se, "id") ?? "",
        DisplayName = GetString(se, "name") ?? "",
        Result = ReadEnum(GetString(se, "result"), ExecutionResult.Passed),
        Duration = se.TryGetProperty("durationSeconds", out var d) && d.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(d.GetDouble())
            : null,
        IsHappyPath = se.TryGetProperty("isHappyPath", out var hp) && hp.ValueKind == JsonValueKind.True,
        ErrorMessage = GetString(se, "errorMessage"),
        ErrorStackTrace = GetString(se, "errorStackTrace"),
        Labels = ReadStringArray(se, "labels"),
        Categories = ReadStringArray(se, "categories"),
        Rule = GetString(se, "rule"),
        OutlineId = GetString(se, "outlineId"),
        ExampleValues = ReadStringDictionary(se, "exampleValues"),
        ExampleDisplayName = GetString(se, "exampleDisplayName"),
        Attachments = ReadAttachments(se, "attachments"),
        BackgroundSteps = ReadSteps(se, "backgroundSteps"),
        Steps = ReadSteps(se, "steps")
    };

    private static ScenarioStep[]? ReadSteps(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var steps = arr.EnumerateArray().Select(s => new ScenarioStep
        {
            Keyword = GetString(s, "keyword"),
            Text = GetString(s, "text") ?? "",
            Status = s.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? ReadEnum(st.GetString(), ExecutionResult.Passed)
                : null,
            Duration = s.TryGetProperty("durationSeconds", out var sd) && sd.ValueKind == JsonValueKind.Number
                ? TimeSpan.FromSeconds(sd.GetDouble())
                : null,
            SubSteps = ReadSteps(s, "subSteps"),
            Attachments = ReadAttachments(s, "attachments")
        }).ToArray();

        return steps.Length > 0 ? steps : null;
    }

    private static FileAttachment[]? ReadAttachments(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = arr.EnumerateArray()
            .Select(a => new FileAttachment(GetString(a, "name") ?? "", GetString(a, "relativePath") ?? ""))
            .ToArray();
        return list.Length > 0 ? list : null;
    }

    private static ComponentRelationship[] ReadRelationships(JsonElement root)
    {
        if (!root.TryGetProperty("componentRelationships", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray().Select(r => new ComponentRelationship(
            Caller: GetString(r, "caller") ?? "",
            Service: GetString(r, "service") ?? "",
            Protocol: GetString(r, "protocol") ?? "",
            Methods: new HashSet<string>(ReadStringArray(r, "methods") ?? []),
            CallCount: r.TryGetProperty("callCount", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0,
            TestCount: r.TryGetProperty("testCount", out var tc) && tc.ValueKind == JsonValueKind.Number ? tc.GetInt32() : 0,
            DependencyCategory: GetString(r, "dependencyCategory"))).ToArray();
    }

    private static Dictionary<string, WholeTestFlowFragment> ReadWholeTestFlow(JsonElement root)
    {
        var result = new Dictionary<string, WholeTestFlowFragment>();
        if (!root.TryGetProperty("wholeTestFlow", out var obj) || obj.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in obj.EnumerateObject())
        {
            result[prop.Name] = new WholeTestFlowFragment(
                GetString(prop.Value, "activityHtml") ?? "",
                GetString(prop.Value, "flameHtml") ?? "",
                prop.Value.TryGetProperty("spanCount", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32() : 0);
        }
        return result;
    }

    private static Dictionary<string, JsonElement> ReadObjectMap(JsonElement root, string name)
    {
        var result = new Dictionary<string, JsonElement>();
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return result;
        foreach (var prop in obj.EnumerateObject())
            result[prop.Name] = prop.Value.Clone();
        return result;
    }

    private static CiMetadata? ReadCiMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("ciMetadata", out var m) || m.ValueKind != JsonValueKind.Object)
            return null;
        return new CiMetadata(
            Provider: ReadEnum(GetString(m, "provider"), CiEnvironment.None),
            BuildNumber: GetString(m, "buildNumber"),
            Branch: GetString(m, "branch"),
            CommitSha: GetString(m, "commitSha"),
            PipelineUrl: GetString(m, "pipelineUrl"),
            Repository: GetString(m, "repository"),
            RunId: GetString(m, "runId"));
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : [];

    private static string? GetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string[]? ReadStringArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var list = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray();
        return list.Length > 0 ? list : null;
    }

    private static Dictionary<string, string>? ReadStringDictionary(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return null;
        var dict = new Dictionary<string, string>();
        foreach (var prop in obj.EnumerateObject())
            dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
        return dict.Count > 0 ? dict : null;
    }

    private static DateTime ReadDate(JsonElement root, string name)
    {
        var s = GetString(root, name);
        return s is not null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : default;
    }

    private static TEnum ReadEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct =>
        value is not null && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
