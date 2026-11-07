using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TestStack.BDDfy;
using TestStack.BDDfy.Reporters.Html;

namespace Kronikol.BDDfy.xUnit3;

/// <summary>
/// BDDfy batch processor that generates test tracking diagrams after all scenarios in a batch have completed.
/// </summary>
public class DiagramEnhancingBatchProcessor : IBatchProcessor
{
    private static readonly FieldInfo? ScenarioTitleField =
        typeof(Scenario).GetField("<Title>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? ScenarioTitleProperty =
        typeof(Scenario).GetProperty(nameof(Scenario.Title));

    private readonly DiagramsFetcherOptions? _fetcherOptions;

    public DiagramEnhancingBatchProcessor(DiagramsFetcherOptions? fetcherOptions = null)
    {
        _fetcherOptions = fetcherOptions;
    }

    public void Process(IEnumerable<Story> stories)
    {
        var storiesList = stories.ToList();

        FixScenarioTitles(storiesList);
        GenerateBDDfyHtmlReport(storiesList);
        InjectDiagrams(storiesList);
    }

    private static void FixScenarioTitles(List<Story> stories)
    {
        var scenarioInfos = BDDfyScenarioCollector.GetAll();
        var idToTitle = new Dictionary<string, string>();
        foreach (var info in scenarioInfos)
        {
            if (info.BDDfyScenarioId != null)
                idToTitle[info.BDDfyScenarioId] = info.ScenarioTitle;
        }

        foreach (var story in stories)
        {
            // Apply collected titles
            if (idToTitle.Count > 0)
            {
                foreach (var scenario in story.Scenarios)
                {
                    if (idToTitle.TryGetValue(scenario.Id, out var fixedTitle))
                        SetScenarioTitle(scenario, fixedTitle);
                }
            }

            // BDDfy's ClassicReportBuilder calls .Single() on scenarios grouped by title,
            // so duplicate titles within a story cause a crash. Append an index suffix to
            // any duplicates to ensure every scenario in a story has a unique title.
            var titleCounts = new Dictionary<string, int>();
            foreach (var scenario in story.Scenarios)
            {
                var title = scenario.Title;
                if (!titleCounts.TryGetValue(title, out var count))
                    count = 0;
                titleCounts[title] = count + 1;
            }

            var duplicateTitles = new HashSet<string>(titleCounts.Where(x => x.Value > 1).Select(x => x.Key));
            if (duplicateTitles.Count > 0)
            {
                var titleIndex = new Dictionary<string, int>();
                foreach (var scenario in story.Scenarios)
                {
                    var title = scenario.Title;
                    if (!duplicateTitles.Contains(title)) continue;

                    if (!titleIndex.TryGetValue(title, out var index))
                        index = 0;
                    titleIndex[title] = index + 1;

                    SetScenarioTitle(scenario, $"{title} [{index + 1}]");
                }
            }
        }
    }

    private static void SetScenarioTitle(Scenario scenario, string title)
    {
        if (ScenarioTitleField != null)
            ScenarioTitleField.SetValue(scenario, title);
        else
            ScenarioTitleProperty?.SetValue(scenario, title);
    }

    private static void GenerateBDDfyHtmlReport(List<Story> stories)
    {
        try
        {
            var reporter = new HtmlReporter(
                new DefaultHtmlReportConfiguration(),
                new ClassicReportBuilder());
            reporter.Process(stories);
        }
        catch
        {
            // If report generation fails, continue without the HTML report.
            // The TestRunReport is the primary output.
        }
    }

    private void InjectDiagrams(List<Story> stories)
    {
        var bddifyReportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BDDfy.html");
        if (!File.Exists(bddifyReportPath)) return;

        var diagrams = DefaultDiagramsFetcher.GetDiagramsFetcher(_fetcherOptions)();
        var scenarioInfos = BDDfyScenarioCollector.GetAll();

        // Build mapping: BDDfy scenario ID -> diagram(s)
        var bddifyIdToDiagrams = new Dictionary<string, List<DefaultDiagramsFetcher.DiagramAsCode>>();
        foreach (var info in scenarioInfos)
        {
            if (info.BDDfyScenarioId == null) continue;
            var matchingDiagrams = diagrams.Where(d => d.TestRuntimeId == info.TestId).ToList();
            if (matchingDiagrams.Count > 0)
                bddifyIdToDiagrams[info.BDDfyScenarioId] = matchingDiagrams;
        }

        if (bddifyIdToDiagrams.Count == 0) return;

        var html = File.ReadAllText(bddifyReportPath);

        // BDDfy's ClassicReportBuilder does not HTML-encode step text, so test data
        // containing HTML (e.g. XSS payloads like <script>alert(1)</script>) is rendered
        // as live markup. Sanitise step content by encoding any raw HTML inside step spans.
        html = Regex.Replace(
            html,
            @"(<li\s+class='step[^']*'[^>]*>\s*<span>)(.*?)(</span>\s*</li>)",
            m => m.Groups[1].Value + WebUtility.HtmlEncode(m.Groups[2].Value) + m.Groups[3].Value,
            RegexOptions.Singleline);

        // Also sanitise the scenario title divs which can contain unescaped test data
        html = Regex.Replace(
            html,
            @"(<div\s+class='[^']*canToggle\s+scenarioTitle'[^>]*>)(.*?)(</div>)",
            m => m.Groups[1].Value + WebUtility.HtmlEncode(m.Groups[2].Value) + m.Groups[3].Value,
            RegexOptions.Singleline);

        // BDDfy assigns scenario/step IDs per-story (e.g. scenario-1, step-1-1), so
        // different stories can produce duplicate IDs. jQuery's toggle uses these IDs,
        // and duplicate IDs cause the wrong element to be toggled. Fix by making all
        // scenario and step IDs globally unique. Each id='...' gets a unique suffix,
        // and the immediately preceding data-toggle-target is updated to match.
        var idCounter = 0;
        html = Regex.Replace(
            html,
            @"data-toggle-target='((?:scenario|step)-[\d-]+)'(.*?)id='(\1)'",
            m =>
            {
                var uniqueId = $"{m.Groups[1].Value}-g{idCounter++}";
                return $"data-toggle-target='{uniqueId}'{m.Groups[2].Value}id='{uniqueId}'";
            },
            RegexOptions.Singleline);

        // Inject diagram CSS and overrides before </head>
        var diagramCss = """
            <style>
            .tracking-diagram { margin: 10px 0 15px 20px; }
            .tracking-diagram img { max-width: 100%; cursor: pointer; }
            .tracking-diagram details { margin: 5px 0; }
            .tracking-diagram pre { background: #f5f5f5; padding: 10px; overflow-x: auto; font-size: 0.85em; }
            .tracking-diagram h4 { margin: 0.5em 0 0.25em; font-size: 0.95em; color: #555; }
            </style>
            """;
        html = html.Replace("</head>", diagramCss + "</head>");

        // Inject diagram images inside each scenario's step list (before the closing </ul>)
        // so that BDDfy's jQuery toggle mechanism shows/hides diagrams together with steps.
        foreach (var (scenarioId, scenarioDiagrams) in bddifyIdToDiagrams)
        {
            var diagramHtml = new StringBuilder();
            diagramHtml.Append("<li class=\"tracking-diagram\"><h4>Sequence Diagrams</h4>");
            foreach (var diagram in scenarioDiagrams)
                diagramHtml.Append(Reports.ReportLinks.DiagramEntryHtml(diagram, _fetcherOptions?.LazyLoadDiagramImages ?? true,
                    Path.GetDirectoryName(bddifyReportPath)!, Reports.ReportLinks.Last));
            diagramHtml.Append("</li>");

            // BDDfy's HTML structure: <ul class='steps' id='scenario-1'>...</ul>
            // Insert diagram HTML as final <li> inside the <ul>, before its closing tag
            var pattern = $@"(<ul[^>]*\bid\s*=\s*['""]?{Regex.Escape(scenarioId)}['""]?[^>]*>.*?)(</ul>)";
            html = Regex.Replace(html, pattern, $"$1{diagramHtml}$2", RegexOptions.Singleline);
        }

        File.WriteAllText(bddifyReportPath, html);
    }
}
