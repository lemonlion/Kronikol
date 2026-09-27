using System.Text;
using Kronikol.InternalFlow;
using Kronikol.PlantUml;
using Kronikol.Reports;
using Kronikol.Tracking;

namespace Kronikol.ComponentDiagram;

/// <summary>
/// Generates a standalone HTML page containing the component/architecture diagram
/// derived from captured test interactions.
/// </summary>
public static class ComponentDiagramReportGenerator
{
    public record ComponentDiagramResult(string HtmlFilePath, string PlantUml);

    public static ComponentDiagramResult GenerateComponentDiagramReport(
        IEnumerable<RequestResponseLog> logs,
        ReportConfigurationOptions reportOptions,
        Dictionary<string, InternalFlowSegment>? perBoundarySegments = null,
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null)
    {
        var options = reportOptions.ComponentDiagramOptions ?? new ComponentDiagramOptions();
        options.DependencyColors ??= reportOptions.DependencyColors;
        var plantUmlServerBaseUrl = reportOptions.PlantUmlServerBaseUrl;
        var imageFormat = reportOptions.PlantUmlImageFormat;
        if (reportOptions.PlantUmlRendering == PlantUmlRendering.NodeJs)
        {
            // The Node renderer emits SVG only — the per-scenario diagrams already render as SVG under
            // NodeJs regardless of PlantUmlImageFormat; the component diagram must do the same instead
            // of asking the renderer for PNG and throwing.
            imageFormat = imageFormat is PlantUmlImageFormat.Base64Png or PlantUmlImageFormat.Base64Svg
                ? PlantUmlImageFormat.Base64Svg
                : PlantUmlImageFormat.Svg;
        }

        var localDiagramRenderer = reportOptions.PlantUmlRendering switch
        {
            PlantUmlRendering.Local => reportOptions.LocalDiagramRenderer,
            PlantUmlRendering.NodeJs => PlantUml.NodeJsPlantUmlRenderer.Render,
            _ => null
        };
        var useBrowserJs = reportOptions.PlantUmlRendering == PlantUmlRendering.BrowserJs;

        var logsArray = logs as RequestResponseLog[] ?? logs.ToArray();
        var relationships = ComponentDiagramGenerator.ExtractRelationships(logsArray, options.ParticipantFilter);

        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(relationships, options, useC4: UsesC4(reportOptions.PlantUmlRendering));

        var directory = Reports.ReportGenerator.ResolveReportsDirectory(reportOptions);
        Directory.CreateDirectory(directory);

        var imgSrc = useBrowserJs
            ? null
            : GetImageSource(plantUml, plantUmlServerBaseUrl, imageFormat, localDiagramRenderer, directory, options.FileName);

        var html = GenerateHtml(plantUml, options.Title, imgSrc, imageFormat, useBrowserJs, reportOptions);
        var htmlPath = Path.Combine(directory, $"{options.FileName}.html");
        File.WriteAllText(htmlPath, html);
        // This writer resolves its own directory and never passes through ReportGenerator.WriteFile, so
        // it tells the run's manifest what it wrote itself (a no-op outside a run).
        RunFileCollector.Record(htmlPath);

        return new ComponentDiagramResult(htmlPath, plantUml);
    }

    /// <summary>
    /// Whether the component diagram is written with the C4 library for <paramref name="rendering"/>: only where the Java
    /// engine draws it (Server, Local). Both JavaScript engines (the page's and the Node renderer's) run plantuml.js,
    /// which cannot load the C4 stdlib: until 3.29.6 the include hung the render until its timeout, and since then it
    /// draws the engine's error picture, so they are given the plain component syntax.
    /// </summary>
    internal static bool UsesC4(PlantUmlRendering rendering) => rendering is PlantUmlRendering.Server or PlantUmlRendering.Local;

    /// <summary>The run report's embedded component diagram as the report's renderer drew it: inline SVG, an image source, or why neither.</summary>
    internal sealed record DrawnDiagram(string? InlineSvg, string? ImageSource, string? Failure);

    /// <summary>
    /// Draws the run report's embedded component diagram the way this page draws the standalone one, for a report whose
    /// diagrams are drawn when it is written: the Node renderer's SVG under <c>NodeJs</c>, the delegate's image under
    /// <c>Local</c>, the server's under <c>Server</c>. Null under <c>BrowserJs</c>, where the page draws it. Inline SVG
    /// when <see cref="ReportConfigurationOptions.InlineSvgRendering"/> is on (internal-flow tracking turns it on), else
    /// an image source, never a file of its own. A render that fails costs the panel its picture and not the report: it
    /// comes back as the reason, and the run records a <see cref="DiagnosticKind.RenderFailure"/>.
    /// </summary>
    /// <remarks>
    /// Until 3.31.8 the run report embedded the diagram for the browser to draw under every renderer, and only a
    /// <c>BrowserJs</c> page carries the engine, so the panel opened blank; under <c>NodeJs</c> it was also written with
    /// the C4 library, which neither JavaScript engine can load (DIAGRAM_COLOURS_PLAN §12.6).
    /// </remarks>
    internal static DrawnDiagram? DrawEmbedded(string plantUml, ReportConfigurationOptions options)
    {
        if (options.PlantUmlRendering == PlantUmlRendering.BrowserJs)
            return null;

        try
        {
            var inline = options.InlineSvgRendering;
            switch (options.PlantUmlRendering)
            {
                case PlantUmlRendering.NodeJs:
                {
                    var svg = Encoding.UTF8.GetString(PlantUml.NodeJsPlantUmlRenderer.Render(plantUml, PlantUmlImageFormat.Svg));
                    return inline
                        ? new DrawnDiagram(DefaultDiagramsFetcher.StripXmlDeclaration(svg), null, null)
                        : new DrawnDiagram(null, $"data:image/svg+xml;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(svg))}", null);
                }
                case PlantUmlRendering.Local:
                {
                    var render = options.LocalDiagramRenderer ?? throw new InvalidOperationException(
                        "PlantUmlRendering.Local requires a LocalDiagramRenderer to be configured.");
                    var png = !inline && options.PlantUmlImageFormat is PlantUmlImageFormat.Png or PlantUmlImageFormat.Base64Png;
                    var bytes = render(plantUml, png ? PlantUmlImageFormat.Png : PlantUmlImageFormat.Svg);
                    if (inline)
                        return new DrawnDiagram(DefaultDiagramsFetcher.StripXmlDeclaration(Encoding.UTF8.GetString(bytes)), null, null);
                    return new DrawnDiagram(null, $"data:{(png ? "image/png" : "image/svg+xml")};base64,{Convert.ToBase64String(bytes)}", null);
                }
                default:
                {
                    var format = inline || options.PlantUmlImageFormat is PlantUmlImageFormat.Svg or PlantUmlImageFormat.Base64Svg ? "svg" : "png";
                    return new DrawnDiagram(null, $"{options.PlantUmlServerBaseUrl}/{format}/{PlantUmlTextEncoder.Encode(plantUml)}", null);
                }
            }
        }
        catch (Exception ex)
        {
            var cause = DefaultDiagramsFetcher.Unwrap(ex);
            ReportDiagnosticsScope.Record(DiagnosticKind.RenderFailure, "Drawing the embedded component diagram failed", cause);
            return new DrawnDiagram(null, null, $"The component diagram could not be drawn: {cause.GetType().Name}: {cause.Message}");
        }
    }

    private static string GetImageSource(
        string plantUml,
        string plantUmlServerBaseUrl,
        PlantUmlImageFormat imageFormat,
        Func<string, PlantUmlImageFormat, byte[]>? localDiagramRenderer,
        string directory,
        string fileName)
    {
        if (localDiagramRenderer is not null)
        {
            var renderFormat = imageFormat switch
            {
                PlantUmlImageFormat.Base64Png => PlantUmlImageFormat.Png,
                PlantUmlImageFormat.Base64Svg => PlantUmlImageFormat.Svg,
                _ => imageFormat
            };
            var imageBytes = localDiagramRenderer(plantUml, renderFormat);
            var isBase64 = imageFormat is PlantUmlImageFormat.Base64Png or PlantUmlImageFormat.Base64Svg;

            if (isBase64)
            {
                var mimeType = renderFormat == PlantUmlImageFormat.Png ? "image/png" : "image/svg+xml";
                return $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";
            }

            var extension = renderFormat == PlantUmlImageFormat.Png ? ".png" : ".svg";
            var imageFileName = $"{fileName}{extension}";
            File.WriteAllBytes(Path.Combine(directory, imageFileName), imageBytes);
            // The HTML above links to this file by name: it is the run's, and moves with it.
            RunFileCollector.Record(Path.Combine(directory, imageFileName));
            return imageFileName;
        }

        var encoded = PlantUmlTextEncoder.Encode(plantUml);
        var formatPath = imageFormat switch
        {
            PlantUmlImageFormat.Svg or PlantUmlImageFormat.Base64Svg => "svg",
            _ => "png"
        };
        return $"{plantUmlServerBaseUrl}/{formatPath}/{encoded}";
    }

    private static string GenerateHtml(
        string plantUml,
        string title,
        string? imgSrc,
        PlantUmlImageFormat imageFormat,
        bool useBrowserJs = false,
        ReportConfigurationOptions? reportOptions = null)
    {
        // Diagram rendering: browser SVG or server <img>
        string diagramHtml;
        if (useBrowserJs)
        {
            var compressed = InternalFlow.InternalFlowHtmlGenerator.CompressToBase64(plantUml);
            diagramHtml = $"<div class=\"plantuml-browser\" id=\"comp-diagram\" data-plantuml-z=\"{compressed}\" data-diagram-type=\"plantuml\"></div>";
        }
        else
        {
            diagramHtml = $"""<img src="{imgSrc}" alt="{title}" style="max-width: 100%;" />""";
        }

        var contextMenuStyles = "";
        var contextMenuScripts = "";

        if (useBrowserJs)
        {
            contextMenuStyles = DiagramContextMenu.GetStyles()
                              + DiagramContextMenu.GetInlineSvgStyles();
            var renderOptions = reportOptions ?? new ReportConfigurationOptions();
            contextMenuScripts = DiagramContextMenu.GetPlantUmlBrowserRenderScript(renderOptions.BrowserRenderWorkers, renderOptions.BrowserRenderCacheMegabytes, renderOptions.BrowserFragmentMaxHeight)
                               + DiagramContextMenu.GetContextMenuScript();
        }

        return $$"""
                <html>
                    <head>
                        <meta charset="utf-8">
                        <link rel="icon" href="{{Constants.DefaultFavicon.DataUri}}">
                        <style>
                            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 2rem; }
                            h1 { color: #333; }
                            .diagram-image { margin: 1rem 0; text-align: center; }
                            .diagram-image img { max-width: 100%; height: auto; }
                            {{contextMenuStyles}}
                        </style>
                        {{contextMenuScripts}}
                    </head>
                    <body>
                        <h1>{{title}}</h1>
                        <div class="diagram-image">
                            {{diagramHtml}}
                        </div>
                    </body>
                </html>
                """;
    }

    private static string SanitizeKey(string name) =>
        name.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
}
