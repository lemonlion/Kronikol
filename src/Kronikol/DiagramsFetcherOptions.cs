namespace Kronikol;

/// <summary>
/// Configuration options for diagram fetching and PlantUML rendering.
/// Controls image format, rendering method, focus field styling, and diagram customisation.
/// </summary>
public record DiagramsFetcherOptions
{
    public string PlantUmlServerBaseUrl { get; set; } = "https://plantuml.com/plantuml";
    public Func<string, string>? RequestPostFormattingProcessor { get; set; }
    public Func<string, string>? RequestPreFormattingProcessor { get; set; }
    public Func<string, string>? RequestMidFormattingProcessor { get; set; }
    public Func<string, string>? ResponsePostFormattingProcessor { get; set; }
    public Func<string, string>? ResponsePreFormattingProcessor { get; set; }
    public Func<string, string>? ResponseMidFormattingProcessor { get; set; }
    public IEnumerable<string> ExcludedHeaders { get; set; } = [];
    public bool SeparateSetup { get; set; }
    public bool HighlightSetup { get; set; } = true;
    public string SetupHighlightColor { get; set; } = "#F6F6F6";
    public bool LazyLoadDiagramImages { get; set; } = true;
    public FocusEmphasis FocusEmphasis { get; set; } = FocusEmphasis.Bold;
    public FocusDeEmphasis FocusDeEmphasis { get; set; } = FocusDeEmphasis.LightGray;
    public string? PlantUmlTheme { get; set; }
    public PlantUmlImageFormat PlantUmlImageFormat { get; set; } = PlantUmlImageFormat.Png;
    public Func<string, PlantUmlImageFormat, byte[]>? LocalDiagramRenderer { get; set; }
    public string? LocalDiagramImageDirectory { get; set; }
    public DiagramFormat DiagramFormat { get; set; } = DiagramFormat.PlantUml;
    public PlantUmlRendering PlantUmlRendering { get; set; } = PlantUmlRendering.BrowserJs;
    public bool InlineSvgRendering { get; set; }
    public bool InternalFlowTracking { get; set; }
    public bool SequenceDiagramArrowColors { get; set; } = true;
    public bool SequenceDiagramParticipantColors { get; set; }
    public Dictionary<string, string>? DependencyColors { get; set; }
    public Dictionary<string, string>? ServiceTypeOverrides { get; set; }
    public GraphQlBodyFormat GraphQlBodyFormat { get; set; } = GraphQlBodyFormat.FormattedWithMetadata;

    /// <summary>See <see cref="ReportConfigurationOptions.CollapseConsecutiveIdenticalCalls"/>.</summary>
    public bool CollapseConsecutiveIdenticalCalls { get; set; }

    /// <summary>See <see cref="ReportConfigurationOptions.CollapseThreshold"/>.</summary>
    public int CollapseThreshold { get; set; } = 2;

    /// <summary>See <see cref="ReportConfigurationOptions.MaxArrowsPerDiagram"/>.</summary>
    public int? MaxArrowsPerDiagram { get; set; }
}