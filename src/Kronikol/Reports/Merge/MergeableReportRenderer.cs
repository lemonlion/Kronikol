using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;

namespace Kronikol.Reports.Merge;

/// <summary>
/// Renders a (typically merged) <see cref="MergeableReport"/> into a single combined
/// <c>TestRunReport.html</c>, reproducing the same report a single combined test run would have
/// produced: per-scenario sequence/activity diagrams, the merged component diagram, internal-flow
/// popups and flame charts, and the CI metadata banner.
/// </summary>
public static class MergeableReportRenderer
{
    /// <summary>
    /// Renders <paramref name="report"/> to <paramref name="outputPath"/> and returns the written path.
    /// </summary>
    /// <param name="report">The report to render (usually the output of <see cref="MergeableReportMerger.Merge"/>).</param>
    /// <param name="outputPath">Absolute or relative path of the HTML file to write.</param>
    /// <param name="title">Report title. Defaults to "Test Run Report".</param>
    /// <param name="options">Optional configuration influencing component-diagram styling and internal-flow popup behaviour.</param>
    public static string Render(MergeableReport report, string outputPath, string? title = null, ReportConfigurationOptions? options = null)
    {
        options ??= new ReportConfigurationOptions();
        title ??= "Test Run Report";

        var hasInternalFlow = report.InternalFlowSegments.Count > 0 || report.WholeTestFlow.Count > 0;

        var internalFlowDataScript = "";
        if (report.InternalFlowSegments.Count > 0)
        {
            // Box the JsonElement values; the serializer emits them as raw JSON, reconstituting the map.
            var map = report.InternalFlowSegments.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
            internalFlowDataScript =
                DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
                + InternalFlowHtmlGenerator.WrapSegmentData(map);
        }

        string? componentDiagramPlantUml = null;
        if (report.ComponentRelationships.Length > 0)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            // Browser-rendered reports use the non-C4 PlantUML dialect.
            componentDiagramPlantUml = ComponentDiagramGenerator.GeneratePlantUml(report.ComponentRelationships, componentOptions, useC4: false);
        }

        var fileName = Path.GetFileName(outputPath);
        if (string.IsNullOrEmpty(fileName))
            fileName = "TestRunReport.html";

        // Write where the caller asked, rather than writing under <BaseDirectory>/Reports and copying.
        //
        // The copy left the merged report in the tool's own directory on every run — so `kronikol merge`
        // littered wherever it happened to be installed, and two merges of different shards into
        // different destinations raced each other over one intermediate name. That race was invisible for
        // as long as a failed write silently salvaged to `<name>2.html`; it surfaced the moment an
        // unwritable output started being reported, which is the point of reporting it.
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        using var scope = string.IsNullOrEmpty(destinationDirectory)
            ? null
            : ReportGenerator.ScopeReportsDirectory(destinationDirectory);

        var written = ReportGenerator.GenerateHtmlReport(
            report.Diagrams,
            report.Features,
            report.StartTime,
            report.EndTime,
            stylesheet: null,
            fileName: fileName,
            title: title,
            includeTestRunData: true,
            internalFlowTracking: hasInternalFlow,
            internalFlowDataScript: internalFlowDataScript,
            wholeTestSegments: null,
            // Without these every merged scenario carrying a sequence diagram was labelled "no
            // interactions": the check fell through to the ambient log of the *merging* process,
            // which is always empty.
            trackedLogs: report.Interactions.Length > 0 ? report.Interactions : null,
            wholeTestVisualization: report.WholeTestVisualization,
            ciMetadata: report.CiMetadata,
            componentDiagramPlantUml: componentDiagramPlantUml,
            precomputedWholeTestContent: report.WholeTestFlow.Count > 0 ? report.WholeTestFlow : null,
            browserRenderWorkers: options.BrowserRenderWorkers,
            browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes,
            browserFragmentMaxHeight: options.BrowserFragmentMaxHeight,
            notePayloadFormat: options.NotePayloadFormat,
            fullSearchIndex: options.FullSearchIndex,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false),
            // The shards' suite, so the HTML's data-stable-id attributes agree with the merged data file.
            //
            // `?? ""` rather than `?? null`, and the distinction is the whole point: GenerateHtmlReport
            // reads a null suite as "the caller did not say" and resolves one from the running assembly,
            // while the merged JSON beside it takes the same null and writes unscoped ids. A merge
            // produces a null suite deliberately — MergeableReportMerger keeps the suite only when every
            // shard agrees — so on that path the HTML would scope its ids to whatever process happened to
            // run the merge and every `#sid-` link out of the data file would miss. The empty string says
            // "deliberately no suite", which both writers agree means the pre-3.1.0 id.
            suite: report.Suite ?? "");

        // Normally a no-op now that the write is scoped to the destination. Kept for the one case the
        // scope cannot cover — an outputPath with no directory part at all — where the write lands under
        // the default reports directory and still has to reach the name the caller asked for.
        var destination = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetFullPath(written), destination, StringComparison.OrdinalIgnoreCase))
        {
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);
            File.Copy(written, destination, overwrite: true);
        }

        return destination;
    }

    /// <summary>
    /// Serializes <paramref name="report"/> back to the mergeable JSON format it was read from, so a
    /// combined run is itself a queryable report and a valid input to a later merge.
    /// </summary>
    /// <remarks>
    /// The format version does not move: the merged file carries the same keys a shard does, and
    /// <c>httpInteractions</c> is the key the standard report has always used - an older
    /// <c>kronikol query</c> reads it without knowing anything changed.
    /// </remarks>
    public static string Serialize(MergeableReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return ReportGenerator.GenerateMergeableReportJson(
            report.Features,
            report.StartTime,
            report.EndTime,
            report.Diagrams.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind),
            report.ComponentRelationships,
            // The serializer writes a JsonElement verbatim, which reconstitutes the map unchanged.
            report.InternalFlowSegments.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value),
            report.WholeTestFlow.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            report.WholeTestVisualization,
            report.CiMetadata,
            report.Diagnostics,
            report.Interactions.Length > 0 ? report.Interactions : null,
            // The version that ran the tests, not the one merging them.
            report.KronikolVersion,
            report.StepPaths,
            report.Annotations,
            // The shards' suite, not this machine's: the merge recomputes every stableId, so without it
            // a merged report's ids match neither its own shards nor a baseline promoted from it.
            suite: report.Suite,
            // And the shards' environment, for the same reason. Null here means "the shards did not
            // agree, or did not say", and the key is left out rather than filled in with the
            // environment of whatever machine happens to be collecting the artifacts.
            environment: report.Environment ?? RunEnvironment.Unrecorded);
    }

    /// <summary>
    /// Reads several mergeable report JSON files and merges them into one in-memory report, ready to
    /// <see cref="Render"/> and to <see cref="Serialize"/>.
    /// </summary>
    public static MergeableReport MergeFiles(IEnumerable<string> inputJsonPaths)
    {
        var reports = inputJsonPaths.Select(MergeableReportReader.ReadFile).ToList();
        if (reports.Count == 0)
            throw new ArgumentException("No input report files were supplied.", nameof(inputJsonPaths));
        return MergeableReportMerger.Merge(reports);
    }

    /// <summary>
    /// Convenience helper: reads several mergeable report JSON files, merges them, and renders the
    /// combined HTML report to <paramref name="outputPath"/>.
    /// </summary>
    public static string MergeFilesToHtml(IEnumerable<string> inputJsonPaths, string outputPath, string? title = null, ReportConfigurationOptions? options = null) =>
        Render(MergeFiles(inputJsonPaths), outputPath, title, options);
}
