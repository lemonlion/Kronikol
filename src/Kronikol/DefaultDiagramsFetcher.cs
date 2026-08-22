using System.Text;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol;

/// <summary>
/// Provides factory methods that create diagram fetchers from captured <see cref="RequestResponseLog"/> entries.
/// Used by report generators to obtain rendered PlantUML diagrams per test ID.
/// </summary>
public static class DefaultDiagramsFetcher
{
    private static DiagramAsCode[]? _diagrams;

    /// <summary>
    /// Clears the process-wide diagram cache so the next fetcher call regenerates diagrams from the
    /// logs currently held by <see cref="RequestResponseLogger"/>. Diagrams are memoised for the
    /// lifetime of the process because the standard test-framework adapters generate exactly one
    /// report per run; hosts that generate several reports in one process (live/incremental
    /// reporting, <c>kronikol ingest</c>, multi-run dashboards) must call this between runs.
    /// </summary>
    public static void Reset() => _diagrams = null;

    /// <summary>Whether a diagram set is currently cached (see <see cref="Reset"/>).</summary>
    public static bool HasCachedDiagrams => _diagrams is not null;

    public static Func<DiagramAsCode[]> GetDiagramsFetcher(DiagramsFetcherOptions? options = null)
    {
        options ??= new DiagramsFetcherOptions();

        if (_diagrams is not null)
            return () => _diagrams;

        return () =>
        {
            try
            {
                return _diagrams = options.PlantUmlRendering switch
                {
                    PlantUmlRendering.BrowserJs => GetPlantUmlBrowserDiagrams(options),
                    PlantUmlRendering.Local => GetLocallyRenderedDiagrams(options),
                    PlantUmlRendering.NodeJs => GetNodeJsRenderedDiagrams(options),
                    _ => GetServerRenderedDiagrams(options)
                };
            }
            catch (InvalidOperationException)
            {
                // A misconfiguration — no LocalDiagramRenderer, no image directory, a format the chosen
                // rendering mode cannot produce — is the caller's bug, not a runtime failure, and the
                // message tells them exactly what to set. Swallowing it would leave them staring at a
                // diagram-less report with no idea why.
                throw;
            }
            catch (Exception ex)
            {
                // The last line of defence. Per-test and per-render isolation below mean this should be
                // unreachable, but a report with no diagrams still beats no report at all.
                var cause = Unwrap(ex);
                ReportDiagnosticsScope.Record(DiagnosticKind.RenderFailure, "No diagram could be produced", cause);
                Console.WriteLine($"WARNING: no diagram could be produced: {cause.GetType().Name}: {cause.Message}");
                return _diagrams = [];
            }
        };
    }

    /// <summary>
    /// The PlantUML a scenario gets when its own diagram could not be produced or rendered: a red note
    /// naming the failure, in place of the diagram. One broken scenario costs the reader that scenario's
    /// picture and nothing else.
    /// </summary>
    internal static string RenderErrorPlantUml(Exception exception) =>
        "@startuml\nhnote across <<renderError>> #ffdddd\n"
        + EscapeNoteText($"\u26a0 diagram could not be generated: {exception.GetType().Name}: {exception.Message}")
        + "\nend note\n@enduml";

    /// <summary>Keeps a note body on one PlantUML line and out of the parser's way.</summary>
    private static string EscapeNoteText(string text) =>
        text.Replace("\r", string.Empty).Replace("\n", " ").Trim();

    /// <summary>
    /// The exception worth reporting. Diagram production fans out internally, so what surfaces is an
    /// <see cref="AggregateException"/> whose message ("One or more errors occurred.") tells the reader
    /// nothing; the first inner exception is the one that actually broke.
    /// </summary>
    internal static Exception Unwrap(Exception exception) =>
        exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
            ? Unwrap(aggregate.InnerExceptions[0])
            : exception;

    /// <summary>Records a per-scenario diagram failure and returns the placeholder diagram to render instead.</summary>
    private static DiagramAsCode PlaceholderFor(string testId, Exception exception, string what)
    {
        var cause = Unwrap(exception);
        ReportDiagnosticsScope.Record(DiagnosticKind.RenderFailure, $"{what} failed", cause, testId);
        Console.WriteLine($"WARNING: {what} failed for scenario {testId}: {cause.GetType().Name}: {cause.Message}");
        return new DiagramAsCode(testId, string.Empty, RenderErrorPlantUml(cause));
    }

    /// <summary>
    /// Renders one diagram, isolating the render call: a renderer that throws for this diagram (a node
    /// process that timed out, a PlantUML server 5xx, a font that is missing) costs this diagram only.
    /// The placeholder is offered to the same renderer so an image-based report still shows the note;
    /// if that fails too the caller gets an empty image source and the placeholder as code-behind, which
    /// every renderer in the report can display.
    /// </summary>
    private static DiagramAsCode RenderIsolated(
        string testId, string plantUml, Func<string, DiagramAsCode> render, string what)
    {
        try
        {
            return render(plantUml);
        }
        catch (Exception ex)
        {
            var placeholder = PlaceholderFor(testId, ex, what);
            try
            {
                return render(placeholder.CodeBehind) with { CodeBehind = placeholder.CodeBehind };
            }
            catch (Exception)
            {
                return placeholder;
            }
        }
    }

    /// <summary>
    /// Produces the PlantUML for every scenario, isolating each scenario: the whole set is built in one
    /// pass (the fast, unchanged path), and only if that throws is it rebuilt scenario by scenario so the
    /// one that fails is the only one that loses its diagram.
    /// </summary>
    /// <remarks>
    /// Before this, any exception raised while formatting one scenario — a custom formatting processor
    /// that could not parse one body, a malformed note — propagated out of the whole set and
    /// <em>every</em> diagram in the report was gone. That is the failure this method exists to prevent.
    /// </remarks>
    private static PlantUmlCreator.PlantUmlForTest[] GetPlantUmlPerTestIdIsolated(DiagramsFetcherOptions options)
    {
        try
        {
            return GetPlantUmlPerTestId(options, lazyLoadImages: options.LazyLoadDiagramImages);
        }
        catch (Exception ex)
        {
            ReportDiagnosticsScope.Record(DiagnosticKind.RenderFailure,
                "Building the diagrams in one pass failed; retrying scenario by scenario", Unwrap(ex));

            var result = new List<PlantUmlCreator.PlantUmlForTest>();
            var logs = TrackedLogs();
            foreach (var group in logs.GroupBy(l => l.TestId, StringComparer.Ordinal))
            {
                var testLogs = group.ToArray();
                try
                {
                    result.AddRange(GetPlantUmlPerTestId(options, lazyLoadImages: options.LazyLoadDiagramImages, logs: testLogs));
                }
                catch (Exception inner)
                {
                    var cause = Unwrap(inner);
                    ReportDiagnosticsScope.Record(DiagnosticKind.RenderFailure, "Building the diagram failed", cause, group.Key);
                    Console.WriteLine($"WARNING: could not build the diagram for scenario {group.Key}: {cause.GetType().Name}: {cause.Message}");
                    var placeholder = RenderErrorPlantUml(cause);
                    result.Add(new PlantUmlCreator.PlantUmlForTest(
                        group.Key,
                        testLogs.FirstOrDefault()?.TestName ?? group.Key,
                        [(placeholder, PlantUmlTextEncoder.Encode(placeholder))],
                        testLogs,
                        []));
                }
            }

            return result.ToArray();
        }
    }

    /// <summary>The tracked logs the diagrams are built from.</summary>
    private static RequestResponseLog[] TrackedLogs() =>
        RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true)).ToArray();

    private static DiagramAsCode[] GetServerRenderedDiagrams(DiagramsFetcherOptions options)
    {
        if (options.PlantUmlImageFormat is PlantUmlImageFormat.Base64Png or PlantUmlImageFormat.Base64Svg)
            throw new InvalidOperationException(
                $"PlantUmlImageFormat.{options.PlantUmlImageFormat} requires PlantUmlRendering.Local to be configured. " +
                "Install the Kronikol.PlantUml.Ikvm package and use IkvmPlantUmlRenderer.Render.");

        if (options.InlineSvgRendering)
            return GetServerRenderedInlineSvgDiagrams(options);

        var perTestId = GetPlantUmlPerTestIdIsolated(options);

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                new DiagramAsCode(test.TestId,
                    $"{options.PlantUmlServerBaseUrl}/{options.PlantUmlImageFormat.ToString().ToLowerInvariant()}/{plantUml.PlantUmlEncoded}",
                    plantUml.PlainText)))
            .ToArray();
    }

    private static DiagramAsCode[] GetServerRenderedInlineSvgDiagrams(DiagramsFetcherOptions options)
    {
        var perTestId = GetPlantUmlPerTestIdIsolated(options);
        using var httpClient = new HttpClient();

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                RenderIsolated(test.TestId, plantUml.PlainText, source =>
                {
                    var svgUrl = $"{options.PlantUmlServerBaseUrl}/svg/{PlantUmlTextEncoder.Encode(source)}";
                    var svgContent = httpClient.GetStringAsync(svgUrl).GetAwaiter().GetResult();
                    return new DiagramAsCode(test.TestId, StripXmlDeclaration(svgContent), source);
                }, "Rendering the diagram on the PlantUML server")))
            .ToArray();
    }

    private static DiagramAsCode[] GetLocallyRenderedDiagrams(DiagramsFetcherOptions options)
    {
        if (options.LocalDiagramRenderer is null)
            throw new InvalidOperationException(
                "PlantUmlRendering.Local requires a LocalDiagramRenderer to be configured. " +
                "Install the Kronikol.PlantUml.Ikvm package and set LocalDiagramRenderer = IkvmPlantUmlRenderer.Render.");

        var perTestId = GetPlantUmlPerTestIdIsolated(options);

        if (options.InlineSvgRendering)
            return RenderLocallyAsInlineSvg(perTestId, options);

        return RenderLocally(perTestId, options);
    }

    private static PlantUmlCreator.PlantUmlForTest[] GetPlantUmlPerTestId(DiagramsFetcherOptions options, bool lazyLoadImages, IEnumerable<RequestResponseLog>? logs = null)
    {
        return GetPlantUmlPerTestId(options, lazyLoadImages,
            maxEncodedDiagramLength: options.PlantUmlRendering is PlantUmlRendering.BrowserJs or PlantUmlRendering.NodeJs or PlantUmlRendering.Local ? 8000 : 2000,
            clientSideSplitting: options.PlantUmlRendering is PlantUmlRendering.BrowserJs,
            logs: logs);
    }

    private static PlantUmlCreator.PlantUmlForTest[] GetPlantUmlPerTestId(DiagramsFetcherOptions options, bool lazyLoadImages, int maxEncodedDiagramLength, int truncateNotesAfterLines = 0, bool excludeAllHeaders = false, bool clientSideSplitting = false, IEnumerable<RequestResponseLog>? logs = null)
    {
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            logs ?? TrackedLogs(),
            requestPostFormattingProcessor: options.RequestPostFormattingProcessor,
            responsePostFormattingProcessor: options.ResponsePostFormattingProcessor,
            requestPreFormattingProcessor: options.RequestPreFormattingProcessor,
            responsePreFormattingProcessor: options.ResponsePreFormattingProcessor,
            requestMidFormattingProcessor: options.RequestMidFormattingProcessor,
            responseMidFormattingProcessor: options.ResponseMidFormattingProcessor,
            excludedHeaders: options.ExcludedHeaders.ToArray(),
            separateSetup: options.SeparateSetup,
            highlightSetup: options.HighlightSetup,
            setupHighlightColor: options.SetupHighlightColor,
            lazyLoadImages: lazyLoadImages,
            focusEmphasis: options.FocusEmphasis,
            focusDeEmphasis: options.FocusDeEmphasis,
            plantUmlTheme: options.PlantUmlTheme,
            internalFlowTracking: options.InternalFlowTracking,
            maxEncodedDiagramLength: maxEncodedDiagramLength,
            truncateNotesAfterLines: truncateNotesAfterLines,
            excludeAllHeaders: excludeAllHeaders,
            sequenceDiagramArrowColors: options.SequenceDiagramArrowColors,
            sequenceDiagramParticipantColors: options.SequenceDiagramParticipantColors,
            dependencyColors: options.DependencyColors,
            serviceTypeOverrides: options.ServiceTypeOverrides,
            graphQlBodyFormat: options.GraphQlBodyFormat,
            clientSideSplitting: clientSideSplitting,
            collapseConsecutiveIdenticalCalls: options.CollapseConsecutiveIdenticalCalls,
            collapseThreshold: options.CollapseThreshold,
            maxArrowsPerDiagram: options.MaxArrowsPerDiagram).ToArray();
    }

    public static (DiagramAsCode[] TruncatedDiagrams, DiagramAsCode[] FullDiagrams) GetCiSummaryDiagrams(DiagramsFetcherOptions options, int truncateNotesAfterLines = 10)
    {
        var truncated = GetPlantUmlPerTestId(options, lazyLoadImages: false,
            maxEncodedDiagramLength: PlantUmlCreator.DefaultMaxEncodedDiagramLength,
            truncateNotesAfterLines: truncateNotesAfterLines,
            excludeAllHeaders: true);

        var full = GetPlantUmlPerTestId(options, lazyLoadImages: false,
            maxEncodedDiagramLength: PlantUmlCreator.DefaultMaxEncodedDiagramLength);

        return (
            truncated.SelectMany(test => test.PlantUmls.Select(plantUml =>
                new DiagramAsCode(test.TestId, string.Empty, plantUml.PlainText))).ToArray(),
            full.SelectMany(test => test.PlantUmls.Select(plantUml =>
                new DiagramAsCode(test.TestId, string.Empty, plantUml.PlainText))).ToArray()
        );
    }

    private static DiagramAsCode[] RenderLocally(PlantUmlCreator.PlantUmlForTest[] perTestId, DiagramsFetcherOptions options)
    {
        var isBase64 = options.PlantUmlImageFormat is PlantUmlImageFormat.Base64Png or PlantUmlImageFormat.Base64Svg;
        var isFile = !isBase64;

        if (isFile && string.IsNullOrWhiteSpace(options.LocalDiagramImageDirectory))
            throw new InvalidOperationException(
                "LocalDiagramImageDirectory must be set when using LocalDiagramRenderer with Png or Svg format. " +
                "Set it to a directory path where diagram images should be saved (e.g. the 'images' subfolder next to your reports).");

        if (isFile)
            Directory.CreateDirectory(options.LocalDiagramImageDirectory!);

        var renderFormat = options.PlantUmlImageFormat switch
        {
            PlantUmlImageFormat.Base64Png => PlantUmlImageFormat.Png,
            PlantUmlImageFormat.Base64Svg => PlantUmlImageFormat.Svg,
            _ => options.PlantUmlImageFormat
        };

        var extension = renderFormat == PlantUmlImageFormat.Png ? ".png" : ".svg";
        var mimeType = renderFormat == PlantUmlImageFormat.Png ? "image/png" : "image/svg+xml";
        var imagesFolderName = isFile ? Path.GetFileName(options.LocalDiagramImageDirectory!) : null;
        var counter = 0;

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                RenderIsolated(test.TestId, plantUml.PlainText, source =>
                {
                    var imageBytes = options.LocalDiagramRenderer!(source, renderFormat);

                    string imgSrc;
                    if (isBase64)
                    {
                        imgSrc = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
                    }
                    else
                    {
                        var fileName = $"diagram_{Interlocked.Increment(ref counter)}{extension}";
                        var filePath = Path.Combine(options.LocalDiagramImageDirectory!, fileName);
                        File.WriteAllBytes(filePath, imageBytes);
                        imgSrc = $"{imagesFolderName}/{fileName}";
                    }

                    return new DiagramAsCode(test.TestId, imgSrc, source);
                }, "Rendering the diagram locally")))
            .ToArray();
    }

    private static DiagramAsCode[] GetPlantUmlBrowserDiagrams(DiagramsFetcherOptions options)
    {
        var perTestId = GetPlantUmlPerTestIdIsolated(options);

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                new DiagramAsCode(test.TestId, string.Empty, plantUml.PlainText)))
            .ToArray();
    }

    private static DiagramAsCode[] GetNodeJsRenderedDiagrams(DiagramsFetcherOptions options)
    {
        var perTestId = GetPlantUmlPerTestIdIsolated(options);
        var inputs = perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml => (test.TestId, Source: plantUml.PlainText)))
            .ToList();

        Func<string, string, string, DiagramAsCode> make = options.InlineSvgRendering
            ? (testId, svg, source) => new DiagramAsCode(testId, StripXmlDeclaration(svg), source)
            : (testId, svg, source) => new DiagramAsCode(testId, $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}", source);

        return RenderNodeBatchIsolated(inputs, make, "Rendering the diagram with node.js");
    }

    /// <summary>
    /// The batch counterpart of <see cref="RenderIsolated"/> for the Node renderer: every diagram of the
    /// report goes through one <c>node</c> process (<see cref="NodeJsPlantUmlRenderer.RenderMany"/>), and
    /// the per-diagram isolation is kept — a diagram the engine refuses gets the placeholder note, which
    /// is itself rendered in a second (small) batch so an image-based report still shows it; if even that
    /// fails, the placeholder stands as code-behind. A process that cannot run at all (no node, no engine)
    /// costs every diagram its picture, exactly as every per-diagram spawn would have failed before.
    /// </summary>
    private static DiagramAsCode[] RenderNodeBatchIsolated(
        List<(string TestId, string Source)> inputs, Func<string, string, string, DiagramAsCode> make, string what)
    {
        var output = new DiagramAsCode?[inputs.Count];
        if (inputs.Count == 0) return [];

        IReadOnlyList<NodeJsPlantUmlRenderer.NodeRenderResult>? results = null;
        Exception? processFailure = null;
        try { results = NodeJsPlantUmlRenderer.RenderMany(inputs.Select(i => i.Source).ToList()); }
        catch (Exception ex) { processFailure = ex; }

        var retry = new List<(int Index, DiagramAsCode Placeholder)>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var r = results is not null && i < results.Count ? results[i] : null;
            if (r is { Svg: not null })
            {
                output[i] = make(inputs[i].TestId, r.Svg, inputs[i].Source);
                continue;
            }
            var failure = processFailure ?? new InvalidOperationException(r?.Error ?? "Node.js PlantUML render returned no result.");
            retry.Add((i, PlaceholderFor(inputs[i].TestId, failure, what)));
        }

        if (retry.Count > 0 && processFailure is null)
        {
            IReadOnlyList<NodeJsPlantUmlRenderer.NodeRenderResult>? retried = null;
            try { retried = NodeJsPlantUmlRenderer.RenderMany(retry.Select(p => p.Placeholder.CodeBehind).ToList()); }
            catch (Exception) { retried = null; }
            for (var k = 0; k < retry.Count; k++)
            {
                var (index, placeholder) = retry[k];
                var r = retried is not null && k < retried.Count ? retried[k] : null;
                output[index] = r is { Svg: not null }
                    ? make(placeholder.TestRuntimeId, r.Svg, placeholder.CodeBehind) with { CodeBehind = placeholder.CodeBehind }
                    : placeholder;
            }
        }
        else
        {
            foreach (var (index, placeholder) in retry) output[index] = placeholder;
        }

        return output!;
    }

    private static DiagramAsCode[] RenderLocallyAsInlineSvg(PlantUmlCreator.PlantUmlForTest[] perTestId, DiagramsFetcherOptions options)
    {
        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                RenderIsolated(test.TestId, plantUml.PlainText, source =>
                {
                    var imageBytes = options.LocalDiagramRenderer!(source, PlantUmlImageFormat.Svg);
                    var svgContent = Encoding.UTF8.GetString(imageBytes);
                    return new DiagramAsCode(test.TestId, StripXmlDeclaration(svgContent), source);
                }, "Rendering the diagram locally")))
            .ToArray();
    }

    private static string StripXmlDeclaration(string svg)
    {
        if (svg.StartsWith("<?xml", StringComparison.Ordinal))
        {
            var end = svg.IndexOf("?>", StringComparison.Ordinal);
            if (end >= 0)
                svg = svg[(end + 2)..].TrimStart();
        }
        return svg;
    }

    /// <summary>
    /// Represents a diagram rendered as code, containing the test runtime ID, image source, and the PlantUML code behind it.
    /// </summary>
    public record DiagramAsCode(string TestRuntimeId, string ImgSrc, string CodeBehind);
}