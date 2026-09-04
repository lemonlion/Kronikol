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
            trackedLogs: null,
            wholeTestVisualization: report.WholeTestVisualization,
            ciMetadata: report.CiMetadata,
            componentDiagramPlantUml: componentDiagramPlantUml,
            precomputedWholeTestContent: report.WholeTestFlow.Count > 0 ? report.WholeTestFlow : null,
            browserRenderWorkers: options.BrowserRenderWorkers,
            browserRenderCacheMegabytes: options.BrowserRenderCacheMegabytes,
            browserFragmentMaxHeight: options.BrowserFragmentMaxHeight,
            notePayloadFormat: options.NotePayloadFormat,
            fullSearchIndex: options.FullSearchIndex,
            toggleDefaults: ReportToggleDefaultsResolver.Resolve(options, specifications: false));

        // GenerateHtmlReport always writes under <BaseDir>/Reports/<fileName>; relocate to the
        // caller's requested path when different.
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
    /// Convenience helper: reads several mergeable report JSON files, merges them, and renders the
    /// combined HTML report to <paramref name="outputPath"/>.
    /// </summary>
    public static string MergeFilesToHtml(IEnumerable<string> inputJsonPaths, string outputPath, string? title = null, ReportConfigurationOptions? options = null)
    {
        var reports = inputJsonPaths.Select(MergeableReportReader.ReadFile).ToList();
        if (reports.Count == 0)
            throw new ArgumentException("No input report files were supplied.", nameof(inputJsonPaths));
        var merged = MergeableReportMerger.Merge(reports);
        return Render(merged, outputPath, title, options);
    }
}
