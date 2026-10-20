using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;

namespace Kronikol.Reports.Merge;

/// <summary>
/// Renders a (typically merged) <see cref="MergeableReport"/> into a single combined
/// <c>TestRunReport.html</c>, reproducing the same report a single combined test run would have
/// produced: per-scenario sequence/activity diagrams, the merged component diagram, internal-flow
/// popups and flame charts, and the CI metadata banner. Like a run, it writes the labs page beside the report
/// when there is history or a diagnostic for it, and draws those views in the report only where the options ask.
/// </summary>
public static class MergeableReportRenderer
{
    /// <summary>
    /// Renders <paramref name="report"/> to <paramref name="outputPath"/> and returns the written path.
    /// </summary>
    /// <param name="report">The report to render (usually the output of <see cref="MergeableReportMerger.Merge"/>).</param>
    /// <param name="outputPath">Absolute or relative path of the HTML file to write.</param>
    /// <param name="title">Report title. Defaults to "Test Run Report".</param>
    /// <param name="options">Optional configuration influencing component-diagram styling and internal-flow popup behaviour,
    /// and what the report and the labs page carry, as a run's options do: the History and Diagnostics tabs where
    /// <see cref="ReportConfigurationOptions.ShowHistorySection"/> and
    /// <see cref="ReportConfigurationOptions.ShowReportDiagnosticsSection"/> ask (both on by default since 5.0.0), the
    /// scenarios' own history where <see cref="ReportConfigurationOptions.ShowScenarioHistory"/> asks, and on the labs page,
    /// <c>{name}.labs.html</c> beside the report, unless <see cref="ReportConfigurationOptions.GenerateLabsReport"/> is off.</param>
    /// <param name="history">The merged run read against a cross-run ledger, when <c>merge --history</c> named one: drawn on the
    /// labs page, and in the report where its options ask (<see cref="ReportConfigurationOptions.EmbedHistoryInReport"/> keeps
    /// it out of both).</param>
    /// <remarks>The labs page is written when the merge read history or its shards recorded diagnostics
    /// (<see cref="MergeableReport.Diagnostics"/>), which since 4.6.0 also reach the report's diagnostics section when it is
    /// asked for. Each attachment with an absolute path to a file is copied into <c>attachments/</c> beside the report and
    /// linked there, as a run's are; <see cref="MergeFiles"/> gives a shard's attachments that path. Every attachment the
    /// report cannot hold is said in one <see cref="DiagnosticKind.AttachmentFailure"/> entry, first in the diagnostics the
    /// report and the labs page draw.</remarks>
    public static string Render(MergeableReport report, string outputPath, string? title = null, ReportConfigurationOptions? options = null, History.HistoryVerdicts? history = null)
    {
        // The shards' files go beside the merged report, as a run's go beside its own: MergeFiles pointed each at its
        // shard's folder, and a shard's own relative names would be dead links here.
        if (DestinationDirectory(outputPath) is { Length: > 0 } destinationDirectory)
            report = CarryAttachments(report, destinationDirectory);
        return RenderCarried(report, outputPath, title, options, history);
    }

    /// <summary>The folder <see cref="Render"/> writes <paramref name="outputPath"/> into, where the attachments go.</summary>
    internal static string? DestinationDirectory(string outputPath) => Path.GetDirectoryName(Path.GetFullPath(outputPath));

    /// <summary>
    /// <see cref="Render"/> for a report whose attachments <see cref="CarryAttachments"/> already placed: <c>kronikol
    /// merge</c> carries them first, so the entry about the files it could not carry is on the report it then writes as data,
    /// digest and labs page as well as on this HTML.
    /// </summary>
    internal static string RenderCarried(MergeableReport report, string outputPath, string? title = null, ReportConfigurationOptions? options = null, History.HistoryVerdicts? history = null)
    {
        options ??= new ReportConfigurationOptions();
        title ??= "Test Run Report";

        var hasInternalFlow = report.InternalFlowSegments.Count > 0 || report.WholeTestFlow.Count > 0;

        EmbeddedComponentDiagram? componentDiagram = null;
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

        var internalFlowDataScript = BuildInternalFlowDataScript(report, options, componentDiagramPlantUml);

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
        var destinationDirectory = DestinationDirectory(outputPath);
        using var scope = string.IsNullOrEmpty(destinationDirectory)
            ? null
            : ReportGenerator.ScopeReportsDirectory(destinationDirectory);

        var written = ReportGenerator.GenerateHtmlReportCore(
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
            componentDiagram: componentDiagram,
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
            suite: report.Suite ?? "",
            // The shards' own diagnostics: until 4.6.0 the merged report's section listed none of them (F3).
            diagnostics: report.Diagnostics,
            // The calls of no scenario the shards wrote: the merged data file carried them, and until 4.7.3 its HTML drew
            // no Background calls section.
            background: BackgroundAttribution.Summarise(report.Interactions, report.Features),
            history: options.EmbedHistoryInReport && (options.ShowScenarioHistory || options.ShowHistorySection) ? history : null,
            showHistorySection: options.ShowHistorySection,
            showReportDiagnostics: options.ShowReportDiagnosticsSection,
            renderCollapsedSubStepsOnExpand: options.RenderCollapsedSubStepsOnExpand,
            showScenarioHistory: options.ShowScenarioHistory,
            // The shards' own marks, carried as the data file carries them.
            warmUp: report.WarmUp);

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

        // The labs page beside the report, linked to it by the name it was written under.
        if (LabsPageWritten(report, options, history))
        {
            var labsHistory = options.EmbedHistoryInReport ? history : null;
            File.WriteAllText(LabsPagePath(destination), LabsReportGenerator.Build(new LabsPage(title, report.Features, report.Suite ?? "", labsHistory,
                report.Diagnostics, Path.GetFileName(destination), EndedAt: new DateTimeOffset(report.EndTime.ToUniversalTime()),
                CustomCss: options.CustomCss, CustomFaviconBase64: options.CustomFaviconBase64, CustomLogoHtml: options.CustomLogoHtml)));
        }

        return destination;
    }

    /// <summary>Whether <see cref="Render"/> writes the labs page for these inputs: the switch is on and there is history it may
    /// show or a diagnostic to list.</summary>
    internal static bool LabsPageWritten(MergeableReport report, ReportConfigurationOptions options, History.HistoryVerdicts? history) =>
        options.GenerateLabsReport && LabsReportGenerator.HasContent(options.EmbedHistoryInReport ? history : null, report.Diagnostics);

    /// <summary>The labs page beside the merged report at <paramref name="htmlPath"/>: <c>Combined.labs.html</c> for <c>Combined.html</c>.</summary>
    internal static string LabsPagePath(string htmlPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(htmlPath)) ?? "", LabsReportGenerator.FileName(Path.GetFileNameWithoutExtension(htmlPath)));

    /// <summary>
    /// The popup data a merged report carries: the merged segment map, through the same wrapper as a live run's, with
    /// its id list computed over the merged diagrams and the component diagram the page embeds. A merge never re-keys
    /// a segment, so the ids in the shards' diagrams still name their segments. Empty when no shard had one.
    /// </summary>
    internal static string BuildInternalFlowDataScript(MergeableReport report, ReportConfigurationOptions options, string? componentDiagramPlantUml)
    {
        if (report.InternalFlowSegments.Count == 0)
            return "";

        // Box the JsonElement values; the serializer emits them as raw JSON, reconstituting the map.
        var map = report.InternalFlowSegments.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
        var linkSources = report.Diagrams.Select(d => (string?)d.CodeBehind).Append(componentDiagramPlantUml).ToArray();
        return DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
               + InternalFlowHtmlGenerator.WrapSegmentData(map, linkSources);
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
        // Compressed when any shard was (MergeableReport.PayloadsCompressed).
        return ReportGenerator.SerializeReportJson(Model(report, report.PayloadsCompressed));
    }

    /// <summary>
    /// Writes the merged data file compressed into <paramref name="destination"/> (plans/REPORT_FORMAT_PLAN.md 4.3): every
    /// payload as text, whatever the shards held (F28), in one zstd frame behind Kronikol's marker.
    /// </summary>
    internal static void WriteCompressed(MergeableReport report, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(report);
        ReportGenerator.WriteCompressedReport(Model(report, compressPayloads: false), destination);
    }

    private static Dictionary<string, object?> Model(MergeableReport report, bool compressPayloads)
    {
        return ReportGenerator.BuildMergeableReportModel(
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
            environment: report.Environment ?? RunEnvironment.Unrecorded,
            compressPayloads: compressPayloads,
            // The shards' own marks, never recomputed over the merged calls.
            warmUp: report.WarmUp);
    }

    /// <summary>
    /// Reads several mergeable report JSON files and merges them into one in-memory report, ready to
    /// <see cref="Render"/> and to <see cref="Serialize"/>. A shard names its attachment files relative to its own folder;
    /// each that is there is read as the absolute path of that file, which <see cref="Render"/> copies beside the report.
    /// </summary>
    public static MergeableReport MergeFiles(IEnumerable<string> inputJsonPaths)
    {
        var reports = new List<MergeableReport>();
        foreach (var path in inputJsonPaths)
        {
            // Per file, so a failure names the file. A merge is handed N shards and the read threw from
            // whichever one was malformed, several frames away: `Failed to read a report: The format of
            // the HTTP method is invalid.` out of fifteen artifacts is not a diagnosis, it is a hunt.
            try
            {
                var report = MergeableReportReader.ReadFile(path);
                PointAttachmentsAtTheirShard(report, Path.GetDirectoryName(Path.GetFullPath(path)) ?? "");
                reports.Add(report);
            }
            catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or ArgumentException
                                                 or Kronikol.Compression.ReportDecodeException)
            {
                throw new FormatException($"{path}: {exception.Message}", exception);
            }
        }

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

    /// <summary>
    /// Points a shard's attachments at its own folder. A shard names its files relative to the folder it was written to
    /// (<c>attachments/shot.png</c>), and the merged report is written elsewhere, where <see cref="Render"/> copies them. A
    /// link or an absolute path is left as it is. A file that is not there is pointed at too: left relative, it named
    /// whatever the merged report's own folder held under that name, which can be another shard's file of the same name
    /// (#148 F5).
    /// </summary>
    private static void PointAttachmentsAtTheirShard(MergeableReport report, string shardDirectory) =>
        ForEachAttachmentList(report, attachments => attachments.Select(attachment =>
        {
            if (attachment.IsUrl || Path.IsPathRooted(attachment.RelativePath))
                return attachment;
            try
            {
                return attachment with { RelativePath = Path.GetFullPath(Path.Combine(shardDirectory, attachment.RelativePath)) };
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return attachment;
            }
        }).ToArray());

    /// <summary>
    /// Copies each file the merged report links into <c>attachments/</c> beside it and links the copy, as a run's generator
    /// does for its own (<see cref="ReportGenerator.CopyAttachmentsToReportsFolder"/>), the path read by the same rule
    /// (<see cref="AttachmentPlacement"/>). A file already in that folder (a merge written beside one of its shards) is
    /// linked where it is. A name another file holds there takes a number, so two shards' <c>shot.png</c> are both kept and
    /// nothing is written over; a file with the same bytes is reused. A file that is not there, or cannot be copied, keeps
    /// its path, and every such file is said in one <see cref="DiagnosticKind.AttachmentFailure"/> entry, put first on the
    /// report returned (#148): <c>kronikol query</c>'s <c>!</c> line shows the first entry of a kind, and a shard's own
    /// entry, about its own report, would otherwise stand for the merged report's.
    /// </summary>
    internal static MergeableReport CarryAttachments(MergeableReport report, string destinationDirectory)
    {
        var folder = Path.Combine(destinationDirectory, AttachmentPlacement.FolderName);
        var placed = new Dictionary<string, (string? Link, Exception? Error)>(StringComparer.FromComparison(AttachmentPlacement.PathComparison));
        var account = new AttachmentAccount(destinationDirectory);
        string[] bases = [destinationDirectory];
        ForEachAttachmentList(report, attachments => attachments.Select(attachment =>
        {
            var source = AttachmentPlacement.Classify(attachment, destinationDirectory, bases);
            switch (source.Kind)
            {
                case AttachmentPlacement.SourceKind.Link:
                    return attachment;
                case AttachmentPlacement.SourceKind.InReport:
                    return attachment with { RelativePath = source.Link, Problem = null };
                case AttachmentPlacement.SourceKind.Missing:
                    account.NotFound(source.Shown, source.Path, wasThere: false);
                    return attachment with { Problem = AttachmentProblem.NotFound };
                case AttachmentPlacement.SourceKind.Unusable:
                    account.NotUsable(source.Shown);
                    return attachment with { Problem = AttachmentProblem.NotUsable };
            }

            if (!placed.TryGetValue(source.Path, out var place))
                placed[source.Path] = place = Place(source.Path);
            if (place.Link is null)
            {
                account.NotCopied(source.Shown, place.Error!);
                return attachment with { Problem = AttachmentProblem.NotCopied };
            }

            return attachment with { RelativePath = place.Link, Problem = null };
        }).ToArray());

        return account.Describe() is { } message
            ? report with { Diagnostics = [new DiagnosticEntry(DiagnosticKind.AttachmentFailure, message), .. report.Diagnostics] }
            : report;

        (string? Link, Exception? Error) Place(string source)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var stem = Path.GetFileNameWithoutExtension(source);
                var extension = Path.GetExtension(source);
                for (var n = 0; ; n++)
                {
                    var name = n == 0 ? stem + extension : $"{stem}-{n}{extension}";
                    var destination = Path.Combine(folder, name);
                    if (!File.Exists(destination))
                    {
                        File.Copy(source, destination);
                        return (AttachmentPlacement.FolderName + "/" + name, null);
                    }

                    if (SameBytes(source, destination))
                        return (AttachmentPlacement.FolderName + "/" + name, null);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (null, ex);
            }
        }

        static bool SameBytes(string a, string b)
        {
            using var first = File.OpenRead(a);
            using var second = File.OpenRead(b);
            return first.Length == second.Length
                   && System.Security.Cryptography.SHA256.HashData(first).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(second));
        }
    }

    /// <summary>Replaces each attachment list of <paramref name="report"/>: the scenarios', the background steps' and the steps', sub-steps included.</summary>
    private static void ForEachAttachmentList(MergeableReport report, Func<FileAttachment[], FileAttachment[]> replace) =>
        AttachmentPlacement.ForEachList(report.Features.SelectMany(f => f.Scenarios), replace);
}
