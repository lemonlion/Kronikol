using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using YamlDotNet.RepresentationModel;

namespace Example.Api.Tests.Integration.Helpers;

public record ReportFiles(string? SpecificationsHtml, string? TestRunReportHtml, string? SpecificationsYaml);

public record ParsedParameterizedGroup(string ScenarioName, string[] ColumnHeaders, int RowCount, bool HasSubTables, bool HasExpandables);

public record ParsedScenario(string Name, bool IsHappyPath, string[] PlantUmlSources);

public static class ReportParser
{
    public static ReportFiles GetReportFiles(string reportsFolderPath)
    {
        if (!Directory.Exists(reportsFolderPath))
            return new ReportFiles(null, null, null);

        var files = Directory.GetFiles(reportsFolderPath);
        return new ReportFiles(
            files.FirstOrDefault(f => f.EndsWith("Specifications.html")),
            files.FirstOrDefault(f => f.EndsWith("TestRunReport.html")),
            files.FirstOrDefault(f => f.EndsWith("Specifications.yml")));
    }

    public static async Task<string[]> ExtractPlantUmlSourcesAsync(string htmlFilePath)
    {
        var html = await File.ReadAllTextAsync(htmlFilePath);
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        // Server-rendered mode: raw-plantuml pre
        var sources = document.QuerySelectorAll("div.raw-plantuml pre")
            .Select(el => WebUtility.HtmlDecode(el.InnerHtml).Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        if (sources.Length > 0)
            return sources;

        // BrowserJs / InlineSvg mode: data-plantuml inside scenario containers
        // Filter to sequence diagrams only (exclude activity/component diagrams)
        sources = document.QuerySelectorAll("details.scenario [data-plantuml], div.scenario [data-plantuml]")
            .Select(el => WebUtility.HtmlDecode(el.GetAttribute("data-plantuml") ?? "").Trim())
            .Where(s => s.Length > 0 && IsSequenceDiagram(s))
            .ToArray();

        if (sources.Length > 0)
            return sources;

        // BrowserJs / InlineSvg mode: data-plantuml-z (gzip+base64 compressed)
        sources = document.QuerySelectorAll("details.scenario [data-plantuml-z], div.scenario [data-plantuml-z]")
            .Select(el => DecompressFromBase64(el.GetAttribute("data-plantuml-z") ?? ""))
            .Where(s => s.Length > 0 && IsSequenceDiagram(s))
            .ToArray();

        if (sources.Length > 0)
            return sources;

        // JSON script block mode: puml-data (diagram data consolidated in single script element)
        return ExtractFromPumlDataBlock(document)
            .Where(IsSequenceDiagram)
            .ToArray();
    }

    public static async Task<ParsedScenario[]> ExtractScenariosAsync(string htmlFilePath)
    {
        var html = await File.ReadAllTextAsync(htmlFilePath);
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        var results = new List<ParsedScenario>();

        foreach (var scenario in document.QuerySelectorAll("details.scenario, div.scenario"))
        {
            var h3 = scenario.QuerySelector("summary.h3") ?? scenario.QuerySelector("h3");
            var name = h3?.TextContent.Replace("Happy Path", "").Trim() ?? "";
            var isHappyPath = scenario.ClassList.Contains("happy-path")
                          || scenario.QuerySelector("span.label")?.TextContent.Contains("Happy Path", StringComparison.OrdinalIgnoreCase) == true;

            var plantUmlSources = scenario.QuerySelectorAll("div.raw-plantuml pre")
                .Select(el => el.TextContent.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            // BrowserJs / InlineSvg fallback
            if (plantUmlSources.Length == 0)
            {
                plantUmlSources = scenario.QuerySelectorAll("[data-plantuml]")
                    .Select(el => WebUtility.HtmlDecode(el.GetAttribute("data-plantuml") ?? "").Trim())
                    .Where(s => s.Length > 0)
                    .ToArray();
            }

            // Compressed fallback (data-plantuml-z)
            if (plantUmlSources.Length == 0)
            {
                plantUmlSources = scenario.QuerySelectorAll("[data-plantuml-z]")
                    .Select(el => DecompressFromBase64(el.GetAttribute("data-plantuml-z") ?? ""))
                    .Where(s => s.Length > 0)
                    .ToArray();
            }

            // JSON script block fallback (puml-data)
            if (plantUmlSources.Length == 0)
            {
                var pumlMap = GetPumlDataMap(document);
                if (pumlMap.Count > 0)
                {
                    plantUmlSources = scenario.QuerySelectorAll("[id^='puml-']")
                        .Select(el => pumlMap.TryGetValue(el.Id ?? "", out var v) ? DecompressFromBase64(v) : "")
                        .Where(s => s.Length > 0)
                        .ToArray();
                }
            }

            results.Add(new ParsedScenario(name, isHappyPath, plantUmlSources));
        }

        return results.ToArray();
    }

    public static async Task<string?> ExtractTitleAsync(string htmlFilePath)
    {
        var html = await File.ReadAllTextAsync(htmlFilePath);
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));
        return document.QuerySelector("h1")?.TextContent.Trim();
    }

    public static async Task<string> ReadYamlAsync(string yamlFilePath) =>
        await File.ReadAllTextAsync(yamlFilePath);

    public record ParsedYamlScenario(string Name, string[] BackgroundSteps, string[] Steps);

    /// <summary>
    /// Extracts each scenario's ordered top-level step texts (keyword + text, e.g.
    /// "Given a valid post request for the Cake endpoint") from the specifications YAML
    /// data report. Sub-steps (assertion tracking) are deliberately excluded - only the
    /// scenario's own steps are returned.
    /// </summary>
    /// <remarks>
    /// This used to scan lines and slice off a fixed indent, which meant it read the file as text rather
    /// than as YAML: a quoted scalar came back with its quotes and backslashes still in it, and a
    /// multi-line feature description was not noticed at all. It was also, for the same reason, unable
    /// to tell that the file it was reading did not parse - every archived Specifications.yml in this
    /// repository fails to load, at the second line of the Cake feature's description. Reading it with
    /// the YAML parser this project already references makes the test fail when the generator writes
    /// something invalid, which is the point of having it.
    /// </remarks>
    public static async Task<ParsedYamlScenario[]> ExtractScenarioStepsFromYamlAsync(string yamlFilePath)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(await File.ReadAllTextAsync(yamlFilePath)));

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            return [];

        if (!root.Children.TryGetValue(new YamlScalarNode("Features"), out var featuresNode))
            return [];

        return ((YamlSequenceNode)featuresNode).Children
            .OfType<YamlMappingNode>()
            .SelectMany(feature => Sequence(feature, "Scenarios").OfType<YamlMappingNode>())
            .Select(scenario => new ParsedYamlScenario(
                Text(scenario, "Scenario") ?? "",
                [.. Sequence(scenario, "BackgroundSteps").Select(StepText)],
                [.. Sequence(scenario, "Steps").Select(StepText)]))
            .ToArray();
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlSequenceNode sequence
            ? sequence.Children
            : [];

    /// <summary>
    /// A step is a plain string, or a mapping with its text under <c>Step</c> when it carries sub-steps.
    /// Either way this returns only the step's own text; the sub-steps beneath it are not the caller's
    /// business.
    /// </summary>
    private static string StepText(YamlNode step) => step switch
    {
        YamlScalarNode scalar => scalar.Value ?? "",
        YamlMappingNode mapping => Text(mapping, "Step") ?? "",
        _ => ""
    };

    private static string? Text(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;

    public record DiagramImgInfo(string Src, bool HasLazyLoading);

    public static async Task<DiagramImgInfo[]> ExtractDiagramImgsAsync(string htmlFilePath)
    {
        var html = await File.ReadAllTextAsync(htmlFilePath);
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        return document.QuerySelectorAll("details.example img, details.example-diagrams img")
            .Select(img => new DiagramImgInfo(
                img.GetAttribute("src") ?? "",
                img.GetAttribute("loading") == "lazy"))
            .Where(d => d.Src.Length > 0)
            .ToArray();
    }

    public static async Task<ParsedParameterizedGroup[]> ExtractParameterizedGroupsAsync(string htmlFilePath)
    {
        var html = await File.ReadAllTextAsync(htmlFilePath);
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        var results = new List<ParsedParameterizedGroup>();

        foreach (var scenario in document.QuerySelectorAll("details.scenario-parameterized"))
        {
            var summary = scenario.QuerySelector("summary");
            var name = summary?.TextContent.Trim() ?? "";

            var table = scenario.QuerySelector("table.param-test-table");
            if (table is null) continue;

            // Extract column headers — skip the first row (#) and last two (Status, Duration)
            var headers = table.QuerySelectorAll("thead th")
                .Select(th => th.TextContent.Trim())
                .Where(h => h != "#" && h != "Status" && h != "Duration" && h != "Input Parameters")
                .ToArray();

            var rowCount = table.QuerySelectorAll(":scope > tbody > tr").Length;
            var hasSubTables = table.QuerySelectorAll(".cell-subtable").Length > 0;
            var hasExpandables = table.QuerySelectorAll("details.param-expand").Length > 0;

            results.Add(new ParsedParameterizedGroup(name, headers, rowCount, hasSubTables, hasExpandables));
        }

        return results.ToArray();
    }

    private static bool IsSequenceDiagram(string puml) =>
        puml.Contains("participant ") || puml.Contains("actor ") || puml.Contains("entity ");

    private static string DecompressFromBase64(string base64)
    {
        if (string.IsNullOrEmpty(base64))
            return "";

        var bytes = Convert.FromBase64String(base64);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd().Trim();
    }

    private static Dictionary<string, string> GetPumlDataMap(IDocument document)
    {
        var script = document.QuerySelector("script#puml-data");
        if (script is null || string.IsNullOrWhiteSpace(script.TextContent))
            return new Dictionary<string, string>();

        return JsonSerializer.Deserialize<Dictionary<string, string>>(script.TextContent)
               ?? new Dictionary<string, string>();
    }

    private static string[] ExtractFromPumlDataBlock(IDocument document)
    {
        return GetPumlDataMap(document)
            .Values
            .Select(DecompressFromBase64)
            .Where(s => s.Length > 0)
            .ToArray();
    }
}
