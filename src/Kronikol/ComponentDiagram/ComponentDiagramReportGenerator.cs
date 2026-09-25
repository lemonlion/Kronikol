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
        // Both JS engines (browser and Node) render plantuml.js, which cannot load the C4 stdlib: until 3.29.6 the
        // include hung the render until its timeout, and since then it draws the engine's error picture. Use the
        // plain component syntax there.
        var useJsEngine = useBrowserJs || reportOptions.PlantUmlRendering == PlantUmlRendering.NodeJs;

        var logsArray = logs as RequestResponseLog[] ?? logs.ToArray();
        var relationships = ComponentDiagramGenerator.ExtractRelationships(logsArray, options.ParticipantFilter);

        var plantUml = ComponentDiagramGenerator.GeneratePlantUml(relationships, options, useC4: !useJsEngine);

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
