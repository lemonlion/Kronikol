using System.Text;
using Kronikol.PlantUml;
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
            return _diagrams = options.PlantUmlRendering switch
            {
                PlantUmlRendering.BrowserJs => GetPlantUmlBrowserDiagrams(options),
                PlantUmlRendering.Local => GetLocallyRenderedDiagrams(options),
                PlantUmlRendering.NodeJs => GetNodeJsRenderedDiagrams(options),
                _ => GetServerRenderedDiagrams(options)
            };
        };
    }

    private static DiagramAsCode[] GetServerRenderedDiagrams(DiagramsFetcherOptions options)
    {
        if (options.PlantUmlImageFormat is PlantUmlImageFormat.Base64Png or PlantUmlImageFormat.Base64Svg)
            throw new InvalidOperationException(
                $"PlantUmlImageFormat.{options.PlantUmlImageFormat} requires PlantUmlRendering.Local to be configured. " +
                "Install the Kronikol.PlantUml.Ikvm package and use IkvmPlantUmlRenderer.Render.");

        if (options.InlineSvgRendering)
            return GetServerRenderedInlineSvgDiagrams(options);

        var perTestId = GetPlantUmlPerTestId(options, lazyLoadImages: options.LazyLoadDiagramImages);

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                new DiagramAsCode(test.TestId,
                    $"{options.PlantUmlServerBaseUrl}/{options.PlantUmlImageFormat.ToString().ToLowerInvariant()}/{plantUml.PlantUmlEncoded}",
                    plantUml.PlainText)))
            .ToArray();
    }

    private static DiagramAsCode[] GetServerRenderedInlineSvgDiagrams(DiagramsFetcherOptions options)
    {
        var perTestId = GetPlantUmlPerTestId(options, lazyLoadImages: false);
        using var httpClient = new HttpClient();

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
            {
                var svgUrl = $"{options.PlantUmlServerBaseUrl}/svg/{plantUml.PlantUmlEncoded}";
                var svgContent = httpClient.GetStringAsync(svgUrl).GetAwaiter().GetResult();
                return new DiagramAsCode(test.TestId, StripXmlDeclaration(svgContent), plantUml.PlainText);
            }))
            .ToArray();
    }

    private static DiagramAsCode[] GetLocallyRenderedDiagrams(DiagramsFetcherOptions options)
    {
        if (options.LocalDiagramRenderer is null)
            throw new InvalidOperationException(
                "PlantUmlRendering.Local requires a LocalDiagramRenderer to be configured. " +
                "Install the Kronikol.PlantUml.Ikvm package and set LocalDiagramRenderer = IkvmPlantUmlRenderer.Render.");

        var perTestId = GetPlantUmlPerTestId(options, lazyLoadImages: options.LazyLoadDiagramImages);

        if (options.InlineSvgRendering)
            return RenderLocallyAsInlineSvg(perTestId, options);

        return RenderLocally(perTestId, options);
    }

    private static PlantUmlCreator.PlantUmlForTest[] GetPlantUmlPerTestId(DiagramsFetcherOptions options, bool lazyLoadImages)
    {
        return GetPlantUmlPerTestId(options, lazyLoadImages,
            maxEncodedDiagramLength: options.PlantUmlRendering is PlantUmlRendering.BrowserJs or PlantUmlRendering.NodeJs or PlantUmlRendering.Local ? 8000 : 2000,
            clientSideSplitting: options.PlantUmlRendering is PlantUmlRendering.BrowserJs);
    }

    private static PlantUmlCreator.PlantUmlForTest[] GetPlantUmlPerTestId(DiagramsFetcherOptions options, bool lazyLoadImages, int maxEncodedDiagramLength, int truncateNotesAfterLines = 0, bool excludeAllHeaders = false, bool clientSideSplitting = false)
    {
        return PlantUmlCreator.GetPlantUmlImageTagsPerTestId(
            RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true)),
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
            {
                var imageBytes = options.LocalDiagramRenderer!(plantUml.PlainText, renderFormat);

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

                return new DiagramAsCode(test.TestId, imgSrc, plantUml.PlainText);
            }))
            .ToArray();
    }

    private static DiagramAsCode[] GetPlantUmlBrowserDiagrams(DiagramsFetcherOptions options)
    {
        var perTestId = GetPlantUmlPerTestId(options, lazyLoadImages: false);

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
                new DiagramAsCode(test.TestId, string.Empty, plantUml.PlainText)))
            .ToArray();
    }

    private static DiagramAsCode[] GetNodeJsRenderedDiagrams(DiagramsFetcherOptions options)
    {
        var perTestId = GetPlantUmlPerTestId(options, lazyLoadImages: false);

        if (options.InlineSvgRendering)
        {
            return perTestId
                .SelectMany(test => test.PlantUmls.Select(plantUml =>
                {
                    var imageBytes = NodeJsPlantUmlRenderer.Render(plantUml.PlainText, PlantUmlImageFormat.Svg);
                    var svgContent = Encoding.UTF8.GetString(imageBytes);
                    return new DiagramAsCode(test.TestId, StripXmlDeclaration(svgContent), plantUml.PlainText);
                }))
                .ToArray();
        }

        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
            {
                var imageBytes = NodeJsPlantUmlRenderer.Render(plantUml.PlainText, PlantUmlImageFormat.Svg);
                var base64 = Convert.ToBase64String(imageBytes);
                return new DiagramAsCode(test.TestId, $"data:image/svg+xml;base64,{base64}", plantUml.PlainText);
            }))
            .ToArray();
    }

    private static DiagramAsCode[] RenderLocallyAsInlineSvg(PlantUmlCreator.PlantUmlForTest[] perTestId, DiagramsFetcherOptions options)
    {
        return perTestId
            .SelectMany(test => test.PlantUmls.Select(plantUml =>
            {
                var imageBytes = options.LocalDiagramRenderer!(plantUml.PlainText, PlantUmlImageFormat.Svg);
                var svgContent = Encoding.UTF8.GetString(imageBytes);
                return new DiagramAsCode(test.TestId, StripXmlDeclaration(svgContent), plantUml.PlainText);
            }))
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