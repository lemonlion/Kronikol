using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Tracking;

namespace Kronikol.Reports;

/// <summary>
/// Generates HTML, JSON, XML, YAML, and CI summary reports from test features and diagrams.
/// This is the primary report generation entry point called by framework adapters.
/// </summary>
public static class ReportGenerator
{
    internal static string KronikolVersion { get; } =
        typeof(ReportGenerator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ReportGenerator).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    internal static bool ShouldEmbedComponentDiagram(ReportConfigurationOptions options) =>
        (options.ComponentDiagramOptions ?? new ComponentDiagramOptions()).EmbedInTestRunReport;
    private static readonly Lazy<string> AdvancedSearchJs = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("advanced-search.js", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource advanced-search.js not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static void CreateStandardReportsWithDiagrams(Feature[] features, DateTime startRunTime, DateTime endRunTime, ReportConfigurationOptions options)
    {
        // Guard: skip report generation entirely when there are zero scenarios.
        // This prevents the xUnit v3 test-discovery pass (which triggers
        // ITestPipelineStartup but runs no tests) from overwriting a valid
        // report from a previous run with an empty one.
        if (features.Length == 0 || features.All(f => f.Scenarios is null || f.Scenarios.Length == 0))
        {
            if (RequestResponseLogger.RequestAndResponseLogs.Length > 0)
            {
                Console.WriteLine("⚠ WARNING: No test contexts were enqueued, but tracking logs exist. " +
                    "Reports will be empty. Ensure DiagrammedTestRun.TestContexts.Enqueue(TestContext.Current) " +
                    "is called in every test's DisposeAsync().");

                if (options.DiagnosticMode)
                    DiagnosticReportGenerator.Generate(RequestResponseLogger.RequestAndResponseLogs, features, options);
            }

            return;
        }

        if (options.ExpectedTestCount != null)
        {
            var scenarioCount = features.SelectMany(f => f.Scenarios).Count();
            if (scenarioCount < options.ExpectedTestCount())
            {
                options.GenerateSpecificationsReport = false;
                options.GenerateSpecificationsData = false;
            }
        }

        if (options.InternalFlowTracking && options.DiagramFormat == DiagramFormat.PlantUml)
        {
            if (options.PlantUmlRendering is PlantUmlRendering.Server or PlantUmlRendering.Local or PlantUmlRendering.NodeJs)
            {
                options.InlineSvgRendering = true;
                options.PlantUmlImageFormat = PlantUmlImageFormat.Svg;
            }
        }

        var fetcherOptions = new DiagramsFetcherOptions
        {
            PlantUmlServerBaseUrl = options.PlantUmlServerBaseUrl,
            RequestPostFormattingProcessor = options.RequestResponsePostProcessor,
            ResponsePostFormattingProcessor = options.RequestResponsePostProcessor,
            RequestMidFormattingProcessor = options.RequestResponseMidProcessor,
            ResponseMidFormattingProcessor = options.RequestResponseMidProcessor,
            ExcludedHeaders = options.ExcludedHeaders,
            SeparateSetup = options.SeparateSetup,
            HighlightSetup = options.HighlightSetup,
            SetupHighlightColor = options.SetupHighlightColor,
            LazyLoadDiagramImages = options.LazyLoadDiagramImages,
            FocusEmphasis = options.FocusEmphasis,
            FocusDeEmphasis = options.FocusDeEmphasis,
            PlantUmlTheme = options.PlantUmlTheme,
            PlantUmlImageFormat = options.PlantUmlImageFormat,
            LocalDiagramRenderer = options.LocalDiagramRenderer,
            LocalDiagramImageDirectory = options.LocalDiagramImageDirectory,
            DiagramFormat = options.DiagramFormat,
            PlantUmlRendering = options.PlantUmlRendering,
            InlineSvgRendering = options.InlineSvgRendering,
            InternalFlowTracking = options.InternalFlowTracking,
            SequenceDiagramArrowColors = options.SequenceDiagramArrowColors,
            SequenceDiagramParticipantColors = options.SequenceDiagramParticipantColors,
            DependencyColors = options.DependencyColors,
            ServiceTypeOverrides = options.ServiceTypeOverrides,
            GraphQlBodyFormat = options.GraphQlBodyFormat
        };
        var diagrams = DefaultDiagramsFetcher.GetDiagramsFetcher(fetcherOptions)();

        var internalFlowDataScript = "";
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null;
        Dictionary<string, InternalFlowSegment>? perBoundarySegments = null;
        RequestResponseLog[]? trackedLogs = null;
        if (options.InternalFlowTracking)
        {
            trackedLogs = RequestResponseLogger.RequestAndResponseLogs
                .Where(x => !(x?.TrackingIgnore ?? true))
                .ToArray();

            var spans = InternalFlowSpanCollector.CollectSpans(
                options.InternalFlowSpanGranularity,
                options.InternalFlowActivitySources);

            perBoundarySegments = InternalFlowSegmentBuilder.BuildSegments(trackedLogs, spans);

            internalFlowDataScript = DiagramContextMenu.GetInternalFlowConfigScript(options.InternalFlowHasDataBehavior)
                + InternalFlowHtmlGenerator.GenerateSegmentDataScript(
                perBoundarySegments,
                options.InternalFlowDiagramStyle,
                options.InternalFlowShowFlameChart,
                options.InternalFlowFlameChartPosition,
                options.InternalFlowNoDataBehavior,
                options.InternalFlowSpanGranularity,
                options.InternalFlowActivitySources);

            if (options.WholeTestFlowVisualization != WholeTestFlowVisualization.None)
            {
                wholeTestSegments = InternalFlowSegmentBuilder.BuildWholeTestSegments(trackedLogs, spans);
            }
        }

        var ciMetadata = CiMetadataDetector.Detect();

        var specsDataExtension = GetDataFormatExtension(options.SpecificationsDataFormat);
        var testRunDataExtension = GetDataFormatExtension(options.TestRunReportDataFormat);

        // Pre-compute component diagram PlantUML for embedding
        string? componentDiagramPlantUml = null;
        if (options.GenerateComponentDiagram)
        {
            var componentOptions = options.ComponentDiagramOptions ?? new ComponentDiagramOptions();
            componentOptions.DependencyColors ??= options.DependencyColors;
            var componentLogs = RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true));
            var componentRelationships = ComponentDiagramGenerator.ExtractRelationships(componentLogs, componentOptions.ParticipantFilter);
            var useBrowserJs = options.PlantUmlRendering == PlantUmlRendering.BrowserJs;
            componentDiagramPlantUml = ComponentDiagramGenerator.GeneratePlantUml(componentRelationships, componentOptions, useC4: !useBrowserJs);
        }

        // Copy attachment files into the Reports directory so that HTML links resolve
        // when reports are uploaded to GitHub Pages or CI artifacts.
        var reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
        Directory.CreateDirectory(reportsDir);
        CopyAttachmentsToReportsFolder(features, reportsDir);

        var actions = new List<Action>();

        if (options.GenerateSpecificationsReport)
        {
            actions.Add(() => GenerateHtmlReport(diagrams, features, startRunTime, endRunTime, options.HtmlSpecificationsCustomStyleSheet, $"{options.HtmlSpecificationsFileName}.html", options.SpecificationsTitle, false, generateBlankOnFailedTests: true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScript, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, showStepNumbers: options.SpecificationsShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames));
        }

        if (options.GenerateTestRunReport)
        {
            actions.Add(() => GenerateHtmlReport(diagrams, features, startRunTime, endRunTime, null, $"{options.HtmlTestRunReportFileName}.html", GetTestRunReportTitle(options), true, lazyLoadImages: options.LazyLoadDiagramImages, diagramFormat: options.DiagramFormat, plantUmlRendering: options.PlantUmlRendering, inlineSvgRendering: options.InlineSvgRendering, internalFlowTracking: options.InternalFlowTracking, internalFlowDataScript: internalFlowDataScript, wholeTestSegments: wholeTestSegments, trackedLogs: trackedLogs, wholeTestVisualization: options.WholeTestFlowVisualization, ciMetadata: ciMetadata, showStepNumbers: options.TestRunReportShowStepNumbers, customCss: options.CustomCss, customFaviconBase64: options.CustomFaviconBase64, customLogoHtml: options.CustomLogoHtml, groupParameterizedTests: options.GroupParameterizedTests, maxParameterColumns: options.MaxParameterColumns, titleizeParameterNames: options.TitleizeParameterNames, componentDiagramPlantUml: ShouldEmbedComponentDiagram(options) ? componentDiagramPlantUml : null));
        }

        if (options.GenerateSpecificationsData)
        {
            actions.Add(() => GenerateSpecificationsData(features, $"{options.YamlSpecificationsFileName}.{specsDataExtension}", options.SpecificationsTitle, options.SpecificationsDataFormat, true));
        }

        if (options.GenerateTestRunReportData)
        {
            actions.Add(() => GenerateTestRunReportData(features, startRunTime, endRunTime, $"{options.HtmlTestRunReportFileName}.{testRunDataExtension}", options.TestRunReportDataFormat, diagrams, trackedLogs));
        }

        if (options.GenerateTestRunReportSchema)
        {
            actions.Add(() => GenerateTestRunReportSchema($"{options.HtmlTestRunReportFileName}.schema.{GetSchemaExtension(options.TestRunReportDataFormat)}", options.TestRunReportDataFormat));
        }

        if (options.GenerateComponentDiagram)
        {
            actions.Add(() => ComponentDiagramReportGenerator.GenerateComponentDiagramReport(
                RequestResponseLogger.RequestAndResponseLogs.Where(x => !(x?.TrackingIgnore ?? true)),
                options,
                perBoundarySegments: perBoundarySegments,
                wholeTestSegments: wholeTestSegments));
        }

        Parallel.Invoke(actions.ToArray());

        var diagnostics = ReportDiagnostics.Analyse(
            RequestResponseLogger.RequestAndResponseLogs, features,
            includeSourceDiscovery: options.ActivitySourceDiscovery);
        foreach (var message in diagnostics)
            Console.WriteLine(message);

        if (options.DiagnosticMode)
            DiagnosticReportGenerator.Generate(RequestResponseLogger.RequestAndResponseLogs, features, options);

        if (options.WriteCiSummary)
        {
            var (truncatedDiagrams, fullDiagrams) = DefaultDiagramsFetcher.GetCiSummaryDiagrams(fetcherOptions);
            var markdown = CiSummaryGenerator.GenerateMarkdown(features, truncatedDiagrams, fullDiagrams, startRunTime, endRunTime, options.MaxCiSummaryDiagrams,
                options.DiagramFormat, options.PlantUmlServerBaseUrl, options.LocalDiagramRenderer);

            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "CiSummary.md"), markdown);

            var ciEnvironment = CiEnvironmentDetector.Detect();
            CiSummaryWriter.Write(markdown, ciEnvironment);
        }

        if (options.PublishCiArtifacts)
        {
            var ciEnv = CiEnvironmentDetector.Detect();
            var ciReportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            if (Directory.Exists(ciReportsDir))
            {
                var reportFiles = Directory.GetFiles(ciReportsDir)
                    .Where(f => f.EndsWith(".html") || f.EndsWith(".yml") || f.EndsWith(".md") || f.EndsWith(".json") || f.EndsWith(".xml"))
                    .ToArray();
                CiArtifactPublisher.Publish(reportFiles, ciEnv, options.CiArtifactName, options.CiArtifactRetentionDays);
            }
        }
    }

    public static string GetTestRunReportTitle(ReportConfigurationOptions options)
    {
        if (!string.IsNullOrEmpty(options.TestRunReportTitle))
            return options.TestRunReportTitle;
        var prefix = options.ComponentDiagramOptions?.Title;
        if (string.IsNullOrEmpty(prefix))
            prefix = options.FixedNameForReceivingService;
        return string.IsNullOrEmpty(prefix) ? "Test Run Report" : $"{prefix} - Test Run Report";
    }

    public static string GenerateHtmlReport(DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        Feature[] features,
        DateTime startRunTime,
        DateTime endRunTime,
        string? stylesheet,
        string fileName,
        string title,
        bool includeTestRunData,
        bool generateBlankOnFailedTests = false,
        bool lazyLoadImages = true,
        DiagramFormat diagramFormat = DiagramFormat.PlantUml,
        PlantUmlRendering plantUmlRendering = PlantUmlRendering.BrowserJs,
        bool inlineSvgRendering = false,
        bool internalFlowTracking = false,
        string internalFlowDataScript = "",
        Dictionary<string, InternalFlowSegment>? wholeTestSegments = null,
        RequestResponseLog[]? trackedLogs = null,
        WholeTestFlowVisualization wholeTestVisualization = WholeTestFlowVisualization.None,
        CiMetadata? ciMetadata = null,
        bool showStepNumbers = false,
        string? customCss = null,
        string? customFaviconBase64 = null,
        string? customLogoHtml = null,
        bool groupParameterizedTests = true,
        int maxParameterColumns = 10,
        bool titleizeParameterNames = true,
        string? componentDiagramPlantUml = null)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        var scenarioFeatureMapHelper = """
                                      var _filterCache;
                                      function fc() {
                                          if (_filterCache) return _filterCache;
                                          var scenarios = document.getElementsByClassName('scenario');
                                          var features = document.getElementsByClassName('feature');
                                          var items = [];
                                          var fMap = new Map();
                                          for (var fi = 0; fi < features.length; fi++) {
                                              var sc = features[fi].getElementsByClassName('scenario');
                                              for (var si = 0; si < sc.length; si++) {
                                                  var s = sc[si];
                                                  var raw = s.getAttribute('data-dependencies') || '';
                                                  var d = raw ? new Set(raw.split(',')) : new Set();
                                                  var item = { el: s, deps: d, status: s.getAttribute('data-status') || '', isHappy: s.classList.contains('happy-path'), f: features[fi], searchText: s.getAttribute('data-search') || '', hp: false, dep: false, st: false, sr: false, dur: false, cat: false };
                                                  items.push(item);
                                                  fMap.set(s, features[fi]);
                                              }
                                          }
                                          _filterCache = { items: items, features: features, scenarios: scenarios, fMap: fMap };
                                          return _filterCache;
                                      }
                                      function applyVisibility(c) {
                                          for (var i = 0; i < c.items.length; i++) {
                                              var d = c.items[i];
                                              var hidden = d.hp || d.dep || d.st || d.sr || d.dur || d.cat;
                                              d.el.style.display = hidden ? 'none' : '';
                                          }
                                          var rules = document.getElementsByClassName('rule');
                                          for (var i = 0; i < rules.length; i++) {
                                              var rs = rules[i].getElementsByClassName('scenario');
                                              var hasVisibleInRule = false;
                                              for (var j = 0; j < rs.length; j++) {
                                                  if (rs[j].style.display !== 'none') { hasVisibleInRule = true; break; }
                                              }
                                              rules[i].style.display = hasVisibleInRule ? '' : 'none';
                                          }
                                          for (var i = 0; i < c.features.length; i++) {
                                              var f = c.features[i];
                                              var sc = f.getElementsByClassName('scenario');
                                              var hasVisible = false;
                                              for (var j = 0; j < sc.length; j++) {
                                                  if (sc[j].style.display !== 'none') { hasVisible = true; break; }
                                              }
                                              f.style.display = hasVisible ? '' : 'none';
                                          }
                                      }
                                      """;

        var toggleHappyPathsFunction = """
                                       function toggle_happy_paths(btn) {
                                           btn.classList.toggle('happy-path-active');
                                           filter_happy_paths();
                                       }
                                       
                                       function filter_happy_paths() {
                                           var c = fc();
                                           var active = document.querySelector('.happy-path-toggle.happy-path-active') !== null;
                                       
                                           for (var i = 0; i < c.items.length; i++) {
                                               c.items[i].hp = active && !c.items[i].isHappy;
                                           }
                                           applyVisibility(c);
                                           update_url_hash();
                                       }
                                       """;
        var searchFunction = """
                             var searchTimeoutId;

                             function toggle_search_help() {
                                 var panel = document.querySelector('.search-help-panel');
                                 if (panel) panel.style.display = panel.style.display === 'none' ? '' : 'none';
                             }
                             
                             function search_scenarios() {
                                 if (searchTimeoutId)
                                     clearTimeout(searchTimeoutId);
                             
                                 searchTimeoutId = setTimeout(function () {
                                     run_search_scenarios();
                                 }, 300);
                             }
                             
                             function run_search_scenarios() {
                                 var c = fc();
                                 let input = document.getElementById('searchbar').value;
                                 input = input.toLowerCase().trim();

                                 // Advanced search path: when input contains &&, ||, or !!
                                 if (isAdvancedSearch(input)) {
                                     if (input.length === 0) {
                                         for (let i = 0; i < c.items.length; i++) {
                                             c.items[i].sr = false;
                                             c.items[i].el.removeAttribute('open');
                                         }
                                         applyVisibility(c);
                                         update_url_hash();
                                         return;
                                     }

                                     let advancedFailed = false;
                                     let matchCount = 0;
                                     let singleMatch = null;
                                     let advSearchTokens = [];
                                     for (let i = 0; i < c.items.length; i++) {
                                         let item = c.items[i];
                                         let tags = new Set();
                                         let cats = (item.el.getAttribute('data-categories') || '').toLowerCase();
                                         let labels = (item.el.getAttribute('data-labels') || '').toLowerCase();
                                         if (cats) cats.split(',').forEach(function(t) { tags.add(t.trim()); });
                                         if (labels) labels.split(',').forEach(function(t) { tags.add(t.trim()); });

                                         let result = advancedSearchMatch(input, item.searchText, tags, item.status);
                                         if (result === null) {
                                             advancedFailed = true;
                                             break;
                                         }
                                         item.sr = !result;
                                         if (result) {
                                             matchCount++;
                                             singleMatch = item.el;
                                         }
                                     }

                                     if (!advancedFailed) {
                                         applyVisibility(c);
                                         update_url_hash();

                                         if (matchCount === 1 && singleMatch) {
                                             singleMatch.setAttribute('open', '');
                                             let diagrams = singleMatch.querySelector('details.example-diagrams');
                                             if (diagrams) diagrams.setAttribute('open', '');
                                         }

                                         // Parameterized group row highlighting for advanced search
                                         let tokens = advancedSearchTokenise(input);
                                         advSearchTokens = tokens.filter(function(t) { return t.type === 'text' || t.type === 'phrase'; }).map(function(t) { return t.value; });
                                         if (advSearchTokens.length > 0) {
                                             document.querySelectorAll('details.scenario-parameterized').forEach(function(group) {
                                                 if (group.style.display === 'none') return;
                                                 var rows = group.querySelectorAll('tr[data-row-search]');
                                                 var firstMatchRow = null;
                                                 for (var ri = 0; ri < rows.length; ri++) {
                                                     var tbl = rows[ri].closest('table');
                                                     if (tbl && tbl.style.display === 'none') { rows[ri].classList.remove('row-search-match'); continue; }
                                                     var rowText = rows[ri].getAttribute('data-row-search') || '';
                                                     var allMatch = true;
                                                     for (var j = 0; j < advSearchTokens.length; j++) {
                                                         if (!rowText.includes(advSearchTokens[j])) { allMatch = false; break; }
                                                     }
                                                     if (allMatch && !firstMatchRow) { firstMatchRow = rows[ri]; }
                                                     rows[ri].classList.toggle('row-search-match', allMatch);
                                                 }
                                                 if (firstMatchRow && !firstMatchRow.classList.contains('row-active')) {
                                                     firstMatchRow.click();
                                                 }
                                             });
                                         } else {
                                             document.querySelectorAll('tr.row-search-match').forEach(function(r) { r.classList.remove('row-search-match'); });
                                         }
                                         return;
                                     }
                                     // If advancedFailed, fall through to legacy path
                                 }
                             
                                 // Legacy search path
                                 // Extract @tag expressions
                                 let tagExpr = null;
                                 let textInput = input;
                                 if (input.indexOf('@') !== -1) {
                                     let tagParts = [];
                                     let textParts = [];
                                     let tokens = input.split(/\s+/);
                                     let inTag = false;
                                     for (let t = 0; t < tokens.length; t++) {
                                         let tok = tokens[t];
                                         if (tok.startsWith('@') || tok === 'and' || tok === 'or' || tok === 'not' || tok === '(' || tok === ')') {
                                             tagParts.push(tok);
                                             inTag = true;
                                         } else if (inTag && (tok === 'and' || tok === 'or' || tok === 'not')) {
                                             tagParts.push(tok);
                                         } else {
                                             textParts.push(tok);
                                             inTag = false;
                                         }
                                     }
                                     if (tagParts.length > 0) {
                                         tagExpr = tagParts.join(' ');
                                         textInput = textParts.join(' ');
                                     }
                                 }

                                 let searchTokens = parseSearchTokensIncludingQuotes(textInput);
                             
                                 if (searchTokens.length === 0 && !tagExpr) {
                                     for (let i = 0; i < c.items.length; i++) {
                                         c.items[i].sr = false;
                                         c.items[i].el.removeAttribute('open');
                                     }
                                     applyVisibility(c);
                                     update_url_hash();
                                     return;
                                 }
                             
                                 // Match at the scenario level
                                 let matchCount = 0;
                                 let singleMatch = null;
                                 for (let i = 0; i < c.items.length; i++) {
                                     let textMatch = true;
                                     if (searchTokens.length > 0) {
                                         let text = c.items[i].searchText;
                                         for (let j = 0; j < searchTokens.length; j++) {
                                             if (!text.includes(searchTokens[j])) {
                                                 textMatch = false;
                                                 break;
                                             }
                                         }
                                     }
                                     let tagMatch = true;
                                     if (tagExpr) {
                                         let cats = (c.items[i].el.getAttribute('data-categories') || '').toLowerCase();
                                         let labels = (c.items[i].el.getAttribute('data-labels') || '').toLowerCase();
                                         let allTags = new Set();
                                         if (cats) cats.split(',').forEach(function(t) { allTags.add(t.trim()); });
                                         if (labels) labels.split(',').forEach(function(t) { allTags.add(t.trim()); });
                                         tagMatch = evaluateTagExpression(tagExpr, allTags);
                                     }
                                     c.items[i].sr = !(textMatch && tagMatch);
                                     if (textMatch && tagMatch) {
                                         matchCount++;
                                         singleMatch = c.items[i].el;
                                     }
                                 }
                             
                                 applyVisibility(c);
                                 update_url_hash();
                             
                                 // Single match: expand scenario with diagrams
                                 if (matchCount === 1 && singleMatch) {
                                     singleMatch.setAttribute('open', '');
                                     let diagrams = singleMatch.querySelector('details.example-diagrams');
                                     if (diagrams) diagrams.setAttribute('open', '');
                                 }

                                 // For parameterized groups: highlight matching row(s) based on per-row search data
                                 if (searchTokens.length > 0) {
                                     document.querySelectorAll('details.scenario-parameterized').forEach(function(group) {
                                         if (group.style.display === 'none') return;
                                         var rows = group.querySelectorAll('tr[data-row-search]');
                                         var firstMatchRow = null;
                                         for (var ri = 0; ri < rows.length; ri++) {
                                             var tbl = rows[ri].closest('table');
                                             if (tbl && tbl.style.display === 'none') { rows[ri].classList.remove('row-search-match'); continue; }
                                             var rowText = rows[ri].getAttribute('data-row-search') || '';
                                             var allMatch = true;
                                             for (var j = 0; j < searchTokens.length; j++) {
                                                 if (!rowText.includes(searchTokens[j])) { allMatch = false; break; }
                                             }
                                             if (allMatch && !firstMatchRow) { firstMatchRow = rows[ri]; }
                                             rows[ri].classList.toggle('row-search-match', allMatch);
                                         }
                                         if (firstMatchRow && !firstMatchRow.classList.contains('row-active')) {
                                             firstMatchRow.click();
                                         }
                                     });
                                 } else {
                                     document.querySelectorAll('tr.row-search-match').forEach(function(r) { r.classList.remove('row-search-match'); });
                                 }
                             }
                             
                             function parseSearchTokensIncludingQuotes(str) {
                                 let quoteTokens = [];
                                 for (let match of str.matchAll(/"(.*?)"/g)) {
                                     let phrase = match[1].trim();
                                     if (phrase.length > 0) quoteTokens.push(phrase);
                                 }
                             
                                 let remaining = str.replace(/"(.*?)"/g, '').trim();
                             
                                 let simpleTokens = [];
                                 if (remaining.length > 0) {
                                     let rawWords = remaining.split(/\s+/);
                                     for (let i = 0; i < rawWords.length; i++) {
                                         let token = rawWords[i].trim();
                                         if (token.length > 0) simpleTokens.push(token);
                                     }
                                 }
                             
                                 return quoteTokens.concat(simpleTokens);
                             }

                             function evaluateTagExpression(expr, tags) {
                                 // Tokenize
                                 let tokens = [];
                                 let parts = expr.split(/\s+/);
                                 for (let i = 0; i < parts.length; i++) {
                                     let p = parts[i];
                                     if (p === 'and' || p === 'or' || p === 'not') tokens.push({type: p});
                                     else if (p === '(') tokens.push({type: 'lparen'});
                                     else if (p === ')') tokens.push({type: 'rparen'});
                                     else if (p.startsWith('@')) tokens.push({type: 'tag', value: p.substring(1).toLowerCase()});
                                     else tokens.push({type: 'tag', value: p.toLowerCase()});
                                 }
                                 let pos = {i: 0};
                                 function parseOr() {
                                     let left = parseAnd();
                                     while (pos.i < tokens.length && tokens[pos.i].type === 'or') {
                                         pos.i++;
                                         left = left || parseAnd();
                                     }
                                     return left;
                                 }
                                 function parseAnd() {
                                     let left = parseNot();
                                     while (pos.i < tokens.length && tokens[pos.i].type === 'and') {
                                         pos.i++;
                                         left = left && parseNot();
                                     }
                                     return left;
                                 }
                                 function parseNot() {
                                     if (pos.i < tokens.length && tokens[pos.i].type === 'not') {
                                         pos.i++;
                                         return !parsePrimary();
                                     }
                                     return parsePrimary();
                                 }
                                 function parsePrimary() {
                                     if (pos.i < tokens.length && tokens[pos.i].type === 'lparen') {
                                         pos.i++;
                                         let result = parseOr();
                                         if (pos.i < tokens.length && tokens[pos.i].type === 'rparen') pos.i++;
                                         return result;
                                     }
                                     if (pos.i < tokens.length && tokens[pos.i].type === 'tag') {
                                         let v = tokens[pos.i].value;
                                         pos.i++;
                                         return tags.has(v);
                                     }
                                     return false;
                                 }
                                 return parseOr();
                             }
                             """;

        var dependencyFilterFunction = """
                                       var _depMode = 'AND';
                                       function toggle_dep_mode(btn) {
                                           _depMode = _depMode === 'AND' ? 'OR' : 'AND';
                                           btn.textContent = _depMode;
                                           filter_dependencies();
                                       }
                                       function toggle_dependency(btn) {
                                           btn.classList.toggle('dependency-active');
                                           filter_dependencies();
                                       }
                                       
                                       function filter_dependencies() {
                                           var c = fc();
                                           var activeSet = new Set();
                                           document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) {
                                               activeSet.add(b.getAttribute('data-dependency'));
                                           });
                                       
                                           if (activeSet.size === 0) {
                                               for (var i = 0; i < c.items.length; i++) c.items[i].dep = false;
                                               applyVisibility(c);
                                               update_url_hash();
                                               return;
                                           }
                                       
                                           var activeArr = Array.from(activeSet);
                                           for (var i = 0; i < c.items.length; i++) {
                                               var d = c.items[i];
                                               var match;
                                               if (d.deps.size === 0) {
                                                   match = false;
                                               } else if (_depMode === 'AND') {
                                                   match = true;
                                                   for (var j = 0; j < activeArr.length; j++) {
                                                       if (!d.deps.has(activeArr[j])) { match = false; break; }
                                                   }
                                               } else {
                                                   match = false;
                                                   for (var j = 0; j < activeArr.length; j++) {
                                                       if (d.deps.has(activeArr[j])) { match = true; break; }
                                                   }
                                               }
                                               d.dep = !match;
                                           }
                                           applyVisibility(c);
                                           update_url_hash();
                                       }
                                       """;

        var categoryFilterFunction = """
                                     var _catMode = 'OR';
                                     function toggle_cat_mode(btn) {
                                         _catMode = _catMode === 'OR' ? 'AND' : 'OR';
                                         btn.textContent = _catMode;
                                         filter_categories();
                                     }
                                     function toggle_category(btn) {
                                         var cat = btn.getAttribute('data-category');
                                         if (cat === '') {
                                             // "All" button: deactivate all specific categories
                                             document.querySelectorAll('.category-toggle').forEach(function(b) { b.classList.remove('category-active'); });
                                             btn.classList.add('category-active');
                                         } else {
                                             // Deactivate "All" button, toggle this one
                                             var allBtn = document.querySelector('.category-toggle[data-category=""]');
                                             if (allBtn) allBtn.classList.remove('category-active');
                                             btn.classList.toggle('category-active');
                                             // If nothing is active, re-activate "All"
                                             if (document.querySelectorAll('.category-toggle.category-active').length === 0) {
                                                 if (allBtn) allBtn.classList.add('category-active');
                                             }
                                         }
                                         filter_categories();
                                     }
                                     
                                     function filter_categories() {
                                         var c = fc();
                                         var allActive = document.querySelector('.category-toggle.category-active[data-category=""]') !== null;
                                         if (allActive) {
                                             for (var i = 0; i < c.items.length; i++) c.items[i].cat = false;
                                             applyVisibility(c);
                                             update_url_hash();
                                             return;
                                         }
                                         var activeSet = new Set();
                                         document.querySelectorAll('.category-toggle.category-active').forEach(function(b) {
                                             activeSet.add(b.getAttribute('data-category'));
                                         });
                                         for (var i = 0; i < c.items.length; i++) {
                                             var raw = c.items[i].el.getAttribute('data-categories') || '';
                                             var cats = raw ? new Set(raw.split(',')) : new Set();
                                             if (activeSet.has('__uncategorized__') && cats.size === 0) {
                                                 c.items[i].cat = false;
                                             } else if (_catMode === 'AND') {
                                                 var allMatch = true;
                                                 activeSet.forEach(function(a) { if (a !== '__uncategorized__' && !cats.has(a)) allMatch = false; });
                                                 c.items[i].cat = !allMatch;
                                             } else {
                                                 var match = false;
                                                 activeSet.forEach(function(a) { if (a !== '__uncategorized__' && cats.has(a)) match = true; });
                                                 c.items[i].cat = !match;
                                             }
                                         }
                                         applyVisibility(c);
                                         update_url_hash();
                                     }
                                     """;

        var statusFilterFunction = """
                                   function toggle_status(btn) {
                                       btn.classList.toggle('status-active');
                                       filter_statuses();
                                   }
                                   
                                   function filter_statuses() {
                                       var c = fc();
                                       var activeSet = new Set();
                                       document.querySelectorAll('.status-toggle.status-active').forEach(function(b) {
                                           activeSet.add(b.getAttribute('data-status'));
                                       });
                                   
                                       if (activeSet.size === 0) {
                                           for (var i = 0; i < c.items.length; i++) c.items[i].st = false;
                                           applyVisibility(c);
                                           update_url_hash();
                                           return;
                                       }
                                   
                                       for (var i = 0; i < c.items.length; i++) {
                                           var s = c.items[i].status;
                                           if (s === 'SkippedAfterFailure') s = 'Failed';
                                           c.items[i].st = !activeSet.has(s);
                                       }
                                       applyVisibility(c);
                                       update_url_hash();
                                   }
                                   """;

        // Collapse/Expand All
        var collapseExpandAllFunction = """
                                        function toggle_expand_collapse(btn, selector, expandLabel, collapseLabel) {
                                            var expanding = btn.textContent === expandLabel;
                                            var els = document.querySelectorAll(selector);
                                            for (var i = 0; i < els.length; i++) { if (expanding) els[i].setAttribute('open', ''); else els[i].removeAttribute('open'); }
                                            btn.textContent = expanding ? collapseLabel : expandLabel;
                                        }
                                        """;

        var lightboxFunction = """
                               (function() {
                                   var overlay = document.createElement('div');
                                   overlay.className = 'lightbox-overlay';
                                   var img = document.createElement('img');
                                   overlay.appendChild(img);
                                   overlay.addEventListener('click', function() { overlay.classList.remove('active'); });
                                   window.openLightbox = function(e, anchor) {
                                       e.preventDefault();
                                       img.src = anchor.querySelector('img').src;
                                       img.alt = anchor.querySelector('img').alt;
                                       overlay.classList.add('active');
                                   };
                                   document.addEventListener('keydown', function(e) { if (e.key === 'Escape') overlay.classList.remove('active'); });
                                   document.addEventListener('DOMContentLoaded', function() { document.body.appendChild(overlay); });
                               })();
                               """;

        var toggleTableRefFunction = """
                                     function toggle_table_ref(btn) {
                                         var paramName = btn.getAttribute('data-param');
                                         var step = btn.closest('.step');
                                         var table = step ? step.querySelector('.step-param-table[data-param="' + paramName + '"]') : null;
                                         if (!table) {
                                             var scenario = btn.closest('.scenario');
                                             if (scenario) table = scenario.querySelector('.step-param-combined-table');
                                         }
                                         if (!table) {
                                             var val = btn.getAttribute('data-value');
                                             if (!val) return;
                                             var existing = btn.nextElementSibling;
                                             if (existing && existing.classList.contains('step-param-expand')) {
                                                 existing.remove();
                                                 btn.classList.remove('step-table-ref-active');
                                                 return;
                                             }
                                             var pre = document.createElement('pre');
                                             pre.className = 'step-param-expand';
                                             pre.textContent = val;
                                             btn.after(pre);
                                             btn.classList.add('step-table-ref-active');
                                             return;
                                         }
                                         table.scrollIntoView({behavior:'smooth',block:'nearest'});
                                         var cells = table.querySelectorAll('[data-param="' + paramName + '"]');
                                         if (cells.length > 0) {
                                             for (var i = 0; i < cells.length; i++) cells[i].classList.add('step-param-highlight');
                                             setTimeout(function() { for (var i = 0; i < cells.length; i++) cells[i].classList.remove('step-param-highlight'); }, 1500);
                                         } else {
                                             table.classList.add('step-param-highlight');
                                             setTimeout(function() { table.classList.remove('step-param-highlight'); }, 1500);
                                         }
                                     }
                                     """;

        var sortTableFunction = """
                                function sort_table(col) {
                                    var table = document.querySelector('.feature-summary-table');
                                    if (!table) return;
                                    var tbody = table.querySelector('tbody');
                                    var rows = Array.from(tbody.querySelectorAll('tr'));
                                    var asc = table.getAttribute('data-sort-col') === '' + col && table.getAttribute('data-sort-dir') !== 'asc';
                                    rows.sort(function(a, b) {
                                        var ac = a.cells[col].textContent.trim();
                                        var bc = b.cells[col].textContent.trim();
                                        var an = parseFloat(ac), bn = parseFloat(bc);
                                        if (!isNaN(an) && !isNaN(bn)) return asc ? an - bn : bn - an;
                                        return asc ? ac.localeCompare(bc) : bc.localeCompare(ac);
                                    });
                                    for (var i = 0; i < rows.length; i++) tbody.appendChild(rows[i]);
                                    table.setAttribute('data-sort-col', col);
                                    table.setAttribute('data-sort-dir', asc ? 'asc' : 'desc');
                                }
                                """;



        // Copy scenario name
        var copyScenarioNameFunction = """
                                       function copy_scenario_name(btn, evt) {
                                           evt.stopPropagation();
                                           evt.preventDefault();
                                           var name = btn.getAttribute('data-scenario-name');
                                           navigator.clipboard.writeText(name).then(function() {
                                               var orig = btn.textContent;
                                               btn.textContent = '\u2713';
                                               setTimeout(function() { btn.textContent = orig; }, 1500);
                                           });
                                       }
                                       """;

        // Toggle examples detail row
        var toggleExamplesDetailFunction = """
                                           function toggle_examples_detail(row) {
                                               var detail = row.nextElementSibling;
                                               if (detail && detail.classList.contains('examples-detail-row')) {
                                                   detail.style.display = detail.style.display === 'none' ? '' : 'none';
                                                   row.classList.toggle('examples-row-expanded');
                                               }
                                           }
                                           """;

        // Parameterized row selection
        var selectRowFunction = """
                                function selectRow(clickedRow, prefix) {
                                    var table = clickedRow.closest('table');
                                    if (!table) return;
                                    var rows = table.querySelectorAll('tbody tr');
                                    for (var i = 0; i < rows.length; i++) rows[i].classList.remove('row-active');
                                    clickedRow.classList.add('row-active');
                                    var idx = clickedRow.getAttribute('data-row-idx');
                                    // Scope queries to the scenario container instead of the full document
                                    var container = table.closest('.scenario') || document;
                                    var activePanel = document.getElementById(prefix + '-detail-' + idx);
                                    // Capture outgoing panel's <details> open states before hiding
                                    var panels = container.querySelectorAll('[id^="' + prefix + '-detail-"]');
                                    var outgoing = null;
                                    for (var i = 0; i < panels.length; i++) {
                                        if (panels[i].style.display !== 'none') { outgoing = panels[i]; break; }
                                    }
                                    var detailStates = [];
                                    if (outgoing && outgoing !== activePanel) {
                                        outgoing.querySelectorAll('details').forEach(function(d) { detailStates.push(d.open); });
                                    }
                                    // Switch detail panels
                                    for (var i = 0; i < panels.length; i++) panels[i].style.display = 'none';
                                    if (activePanel) {
                                        // Sync <details> open states from outgoing panel
                                        if (detailStates.length > 0) {
                                            var incoming = activePanel.querySelectorAll('details');
                                            for (var i = 0; i < incoming.length && i < detailStates.length; i++) {
                                                incoming[i].open = detailStates[i];
                                            }
                                        }
                                        activePanel.style.display = '';
                                    }
                                    // Switch diagram divs (sequence)
                                    var diagrams = container.querySelectorAll('[id^="' + prefix + '-diagram-"]');
                                    for (var i = 0; i < diagrams.length; i++) diagrams[i].style.display = 'none';
                                    var activeDiagram = document.getElementById(prefix + '-diagram-' + idx);
                                    if (activeDiagram) {
                                        activeDiagram.style.display = '';
                                        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(activeDiagram);
                                    }
                                    // Switch activity diagram divs
                                    var activities = container.querySelectorAll('[id^="' + prefix + '-activity-"]');
                                    for (var i = 0; i < activities.length; i++) activities[i].style.display = 'none';
                                    var activeActivity = document.getElementById(prefix + '-activity-' + idx);
                                    if (activeActivity) {
                                        activeActivity.style.display = '';
                                        if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(activeActivity);
                                    }
                                    // Switch flame chart divs
                                    var flames = container.querySelectorAll('[id^="' + prefix + '-flame-"]');
                                    for (var i = 0; i < flames.length; i++) flames[i].style.display = 'none';
                                    var activeFlame = document.getElementById(prefix + '-flame-' + idx);
                                    if (activeFlame) {
                                        activeFlame.style.display = '';
                                        if (window._renderFlameCharts) window._renderFlameCharts(activeFlame);
                                    }
                                    highlightColumns(table, prefix);
                                }
                                function highlightColumns(table, prefix) {
                                    var activeRow = table.querySelector('tbody tr.row-active');
                                    if (!activeRow) return;
                                    var idx = activeRow.getAttribute('data-row-idx');
                                    var activePanel = document.getElementById(prefix + '-detail-' + idx);
                                    if (!activePanel) return;
                                    var headerRow = table.querySelector('thead tr:last-child') || table.querySelector('thead tr');
                                    var paramCols = [];
                                    if (headerRow) {
                                        headerRow.querySelectorAll('th.sub-header').forEach(function(th) {
                                            paramCols.push(th.textContent.trim());
                                        });
                                    }
                                    activePanel.querySelectorAll('.step-param-combined-table').forEach(function(spt) {
                                        var tbl = spt.querySelector('table');
                                        if (!tbl) return;
                                        var ths = tbl.querySelectorAll('thead th');
                                        var trs = tbl.querySelectorAll('tbody tr');
                                        for (var ci = 0; ci < ths.length; ci++) {
                                            var colName = ths[ci].textContent.trim();
                                            var isMatch = paramCols.indexOf(colName) >= 0;
                                            ths[ci].classList.toggle('col-highlight', isMatch);
                                            trs.forEach(function(tr) {
                                                var td = tr.children[ci];
                                                if (td) td.classList.toggle('col-highlight', isMatch);
                                            });
                                        }
                                    });
                                }
                                """;

        // Toggle between grouped and flat parameter tables
        var toggleFlattenParamsJs = """
                                   function toggleFlattenParams(btn, prefix) {
                                       var wrapper = btn.closest('.param-table-wrapper');
                                       if (!wrapper) return;
                                       var grouped = wrapper.querySelector('.param-table-grouped');
                                       var flat = wrapper.querySelector('.param-table-flat');
                                       if (!grouped || !flat) return;
                                       var showFlat = grouped.style.display !== 'none';
                                       // Read active row index from the currently visible table
                                       var activeTable = showFlat ? grouped : flat;
                                       var activeRow = activeTable.querySelector('tbody tr.row-active');
                                       var activeIdx = activeRow ? activeRow.getAttribute('data-row-idx') : '0';
                                       // Toggle visibility
                                       grouped.style.display = showFlat ? 'none' : '';
                                       flat.style.display = showFlat ? '' : 'none';
                                       // Sync active row on the newly visible table
                                       var newTable = showFlat ? flat : grouped;
                                       var rows = newTable.querySelectorAll('tbody tr');
                                       for (var i = 0; i < rows.length; i++) rows[i].classList.remove('row-active');
                                       var target = newTable.querySelector('tbody tr[data-row-idx="' + activeIdx + '"]');
                                       if (target) target.classList.add('row-active');
                                   }
                                   """;

        // R4: param-expand toggle auto-selects row + cell-subtable click isolation
        var paramExpandJs = """
                            document.addEventListener('DOMContentLoaded', function() {
                                document.querySelectorAll('.param-expand').forEach(function(details) {
                                    details.addEventListener('toggle', function() {
                                        var row = details.closest('tr');
                                        if (row) {
                                            var table = row.closest('table');
                                            var prefix = table ? table.getAttribute('data-prefix') : '';
                                            selectRow(row, prefix);
                                        }
                                    });
                                    details.addEventListener('click', function(e) { e.stopPropagation(); });
                                });
                                document.querySelectorAll('.cell-subtable').forEach(function(el) {
                                    el.addEventListener('click', function(e) { e.stopPropagation(); });
                                });
                                // Trigger column highlighting for initially active rows
                                document.querySelectorAll('.param-test-table').forEach(function(table) {
                                    if (table.style.display === 'none') return;
                                    var activeRow = table.querySelector('tbody tr.row-active');
                                    if (activeRow) {
                                        var prefix = table.getAttribute('data-prefix') || '';
                                        highlightColumns(table, prefix);
                                    }
                                });
                            });
                            """;

        // Toggle timeline
        var deactivateComponentDiagramJs = !string.IsNullOrEmpty(componentDiagramPlantUml)
            ? """
                                             if (hidden) {
                                                 var cd = document.getElementById('component-diagram');
                                                 if (cd && cd.style.display !== 'none') {
                                                     cd.style.display = 'none';
                                                     var cdBtn = document.querySelector('button.timeline-toggle-active[onclick*="toggle_component_diagram"]');
                                                     if (cdBtn) cdBtn.classList.remove('timeline-toggle-active');
                                                 }
                                             }
              """
            : "";
        var toggleTimelineFunction = $$"""
                                     function toggle_timeline(btn) {
                                         var tl = document.getElementById('scenario-timeline');
                                         if (!tl) return;
                                         var hidden = tl.style.display === 'none';
                                         tl.style.display = hidden ? '' : 'none';
                                         btn.classList.toggle('timeline-toggle-active', hidden);{{deactivateComponentDiagramJs}}
                                     }
                                     """;

        // Toggle component diagram
        var toggleComponentDiagramFunction = !string.IsNullOrEmpty(componentDiagramPlantUml)
            ? """
              function toggle_component_diagram(btn) {
                  var cd = document.getElementById('component-diagram');
                  if (!cd) return;
                  var hidden = cd.style.display === 'none';
                  cd.style.display = hidden ? '' : 'none';
                  btn.classList.toggle('timeline-toggle-active', hidden);
                  if (hidden) {
                      if (window._renderDiagramsInContainer) window._renderDiagramsInContainer(cd);
                      var tl = document.getElementById('scenario-timeline');
                      if (tl && tl.style.display !== 'none') {
                          tl.style.display = 'none';
                          var tlBtn = document.querySelector('button.timeline-toggle-active[onclick*="toggle_timeline"]');
                          if (tlBtn) tlBtn.classList.remove('timeline-toggle-active');
                      }
                  }
              }
              """
            : "";

        // Jump to failure
        var hasFailures = features.SelectMany(f => f.Scenarios).Any(s => s.Result == ExecutionResult.Failed);
        var failureCount = features.SelectMany(f => f.Scenarios).Count(s => s.Result == ExecutionResult.Failed);
        var jumpToFailureFunction = """
                                    var _failureIndex = -1;
                                    function jump_to_next_failure() {
                                        var failures = document.querySelectorAll('details.scenario[data-status="Failed"]');
                                        if (failures.length === 0) return;
                                        _failureIndex = (_failureIndex + 1) % failures.length;
                                        var el = failures[_failureIndex];
                                        var feature = el.closest('details.feature');
                                        if (feature) feature.setAttribute('open', '');
                                        el.setAttribute('open', '');
                                        var target = el.querySelector(':scope > summary') || el;
                                        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                                        var counter = document.getElementById('failure-counter');
                                        if (counter) counter.textContent = '(' + (_failureIndex + 1) + '/' + failures.length + ')';
                                    }
                                    """;

        // Duration filter
        var hasDurations = features.SelectMany(f => f.Scenarios).Any(s => s.Duration.HasValue);
        var durationFilterFunction = """
                                     function filter_duration() {
                                         var c = fc();
                                         var input = document.getElementById('duration-threshold');
                                         if (!input) return;
                                         var threshold = parseFloat(input.value);
                                         if (isNaN(threshold) || threshold <= 0) {
                                             for (var i = 0; i < c.items.length; i++) c.items[i].dur = false;
                                             applyVisibility(c);
                                             update_url_hash();
                                             return;
                                         }
                                         var thresholdMs = threshold * 1000;
                                         for (var i = 0; i < c.items.length; i++) {
                                             if (!c.items[i].el.hasAttribute('data-duration-ms')) { c.items[i].dur = true; continue; }
                                             var ms = parseFloat(c.items[i].el.getAttribute('data-duration-ms'));
                                             c.items[i].dur = ms > 0 && ms < thresholdMs;
                                         }
                                         applyVisibility(c);
                                         update_url_hash();
                                     }
                                     function set_percentile(btn) {
                                         var wasActive = btn.classList.contains('percentile-active');
                                         document.querySelectorAll('.percentile-btn').forEach(function(b) { b.classList.remove('percentile-active'); });
                                         var input = document.getElementById('duration-threshold');
                                         var customWrap = document.getElementById('custom-duration-wrap');
                                         if (wasActive) {
                                             if (input) { input.value = ''; filter_duration(); }
                                             if (customWrap) customWrap.style.display = 'none';
                                             return;
                                         }
                                         var isCustom = btn.getAttribute('data-custom') === '1';
                                         if (isCustom) {
                                             btn.classList.add('percentile-active');
                                             if (customWrap) customWrap.style.display = 'inline-flex';
                                             if (input) { input.focus(); if (input.value) filter_duration(); }
                                         } else {
                                             if (customWrap) customWrap.style.display = 'none';
                                             btn.classList.add('percentile-active');
                                             var ms = parseFloat(btn.getAttribute('data-threshold-ms'));
                                             if (input) { input.value = (ms / 1000).toFixed(1); filter_duration(); }
                                         }
                                     }
                                     """;

        // Export filtered view
        var exportFunction = """
                             function clear_all_filters() {
                                 var c = fc();
                                 // Clear search
                                 var sb = document.getElementById('searchbar');
                                 if (sb) { sb.value = ''; }
                                 for (var i = 0; i < c.items.length; i++) c.items[i].sr = false;
                                 // Clear status
                                 document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { b.classList.remove('status-active'); });
                                 for (var i = 0; i < c.items.length; i++) c.items[i].st = false;
                                 // Clear happy paths
                                 var hp = document.querySelector('.happy-path-toggle.happy-path-active');
                                 if (hp) hp.classList.remove('happy-path-active');
                                 for (var i = 0; i < c.items.length; i++) c.items[i].hp = false;
                                 // Clear duration
                                 document.querySelectorAll('.percentile-btn.percentile-active').forEach(function(b) { b.classList.remove('percentile-active'); });
                                 var dur = document.getElementById('duration-threshold');
                                 if (dur) dur.value = '';
                                 var cw = document.getElementById('custom-duration-wrap');
                                 if (cw) cw.style.display = 'none';
                                 for (var i = 0; i < c.items.length; i++) c.items[i].dur = false;
                                 // Clear dependencies
                                 document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) { b.classList.remove('dependency-active'); });
                                 _depMode = 'AND';
                                 var depModeBtn = document.querySelector('.dep-mode-toggle');
                                 if (depModeBtn) depModeBtn.textContent = 'AND';
                                 for (var i = 0; i < c.items.length; i++) c.items[i].dep = false;
                                 // Clear categories
                                 document.querySelectorAll('.category-toggle.category-active').forEach(function(b) { b.classList.remove('category-active'); });
                                 var allCatBtn = document.querySelector('.category-toggle[data-category=""]');
                                 if (allCatBtn) allCatBtn.classList.add('category-active');
                                 if (typeof _catMode !== 'undefined') _catMode = 'OR';
                                 var catModeBtn = document.querySelector('.cat-mode-toggle');
                                 if (catModeBtn) catModeBtn.textContent = 'OR';
                                 for (var i = 0; i < c.items.length; i++) c.items[i].cat = false;
                                 // Apply and clear URL
                                 applyVisibility(c);
                                 history.replaceState(null, '', window.location.pathname + window.location.search);
                             }
                             function export_html() {
                                 var c = fc();
                                 var head = document.querySelector('head');
                                 var headHtml = head ? head.innerHTML : '';
                                 var html = '<html><head>' + headHtml + '</head><body>';
                                 html += '<h1>Filtered Report</h1>';
                                 for (var i = 0; i < c.features.length; i++) {
                                     if (c.features[i].style.display === 'none') continue;
                                     html += c.features[i].outerHTML;
                                 }
                                 html += '</body></html>';
                                 var blob = new Blob([html], { type: 'text/html' });
                                 var a = document.createElement('a');
                                 a.href = URL.createObjectURL(blob);
                                 a.download = 'filtered-report.html';
                                 a.click();
                                 URL.revokeObjectURL(a.href);
                             }
                             function export_csv() {
                                 var c = fc();
                                 var lines = ['Feature,Scenario,Status,Duration'];
                                 for (var i = 0; i < c.items.length; i++) {
                                     var d = c.items[i];
                                     if (d.el.style.display === 'none') continue;
                                     var f = d.f;
                                     var fname = (f.querySelector('summary.h2') || f.querySelector('summary')).textContent.trim();
                                     var sname = (d.el.querySelector('summary.h3') || d.el.querySelector('summary')).textContent.trim();
                                     var dur = d.el.getAttribute('data-duration-ms') || '';
                                     lines.push('"' + fname.replace(/"/g,'""') + '","' + sname.replace(/"/g,'""') + '","' + d.status + '","' + dur + '"');
                                 }
                                 var blob = new Blob([lines.join('\n')], { type: 'text/csv' });
                                 var a = document.createElement('a');
                                 a.href = URL.createObjectURL(blob);
                                 a.download = 'filtered-report.csv';
                                 a.click();
                                 URL.revokeObjectURL(a.href);
                             }
                             """;

        // Persistent filter state
        // No-op stubs (localStorage persistence removed)
        var persistentFilterFunction = """
                                       function save_filter_state() {}
                                       function restore_filter_state() {}
                                       """;

        // URL-anchored filters
        var urlHashFunction = """
                              function update_url_hash() {
                                  var parts = [];
                                  var search = document.getElementById('searchbar');
                                  if (search && search.value) parts.push('q=' + encodeURIComponent(search.value));
                                  var statuses = [];
                                  document.querySelectorAll('.status-toggle.status-active').forEach(function(b) { statuses.push(b.getAttribute('data-status')); });
                                  if (statuses.length > 0) parts.push('status=' + statuses.join(','));
                                  var deps = [];
                                  document.querySelectorAll('.dependency-toggle.dependency-active').forEach(function(b) { deps.push(b.getAttribute('data-dependency')); });
                                  if (deps.length > 0) parts.push('deps=' + encodeURIComponent(deps.join(',')));
                                  if (_depMode !== 'AND') parts.push('depmode=' + _depMode);
                                  if (typeof _catMode !== 'undefined' && _catMode !== 'OR') parts.push('catmode=' + _catMode);
                                  if (document.querySelector('.happy-path-toggle.happy-path-active')) parts.push('hp=1');
                                  var cats = [];
                                  if (!document.querySelector('.category-toggle.category-active[data-category=""]')) {
                                      document.querySelectorAll('.category-toggle.category-active').forEach(function(b) { cats.push(b.getAttribute('data-category')); });
                                  }
                                  if (cats.length > 0) parts.push('cats=' + encodeURIComponent(cats.join(',')));
                                  var dur = document.getElementById('duration-threshold');
                                  if (dur && dur.value) parts.push('dur=' + dur.value);
                                  var activeP = document.querySelector('.percentile-btn.percentile-active');
                                  if (activeP) parts.push('pctl=' + encodeURIComponent(activeP.textContent));
                                  var hash = parts.length > 0 ? '#' + parts.join('&') : '';
                                  history.replaceState(null, '', window.location.pathname + window.location.search + hash);
                              }
                              function parse_url_hash() {
                                  var hash = window.location.hash.substring(1);
                                  if (!hash) return;
                                  // Check if it's a scenario anchor (starts with 'scenario-')
                                  if (hash.indexOf('scenario-') === 0) {
                                      var el = document.getElementById(hash);
                                      if (el) {
                                          var feature = el.closest('details.feature');
                                          if (feature) feature.setAttribute('open', '');
                                          el.setAttribute('open', '');
                                          el.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                          return;
                                      }
                                      // Not a direct element — check if it's a row inside a parameterized group
                                      var row = document.querySelector('tr[data-scenario-id="' + hash + '"]');
                                      if (row) {
                                          var group = row.closest('details.scenario-parameterized');
                                          if (group) {
                                              var feature = group.closest('details.feature');
                                              if (feature) feature.setAttribute('open', '');
                                              group.setAttribute('open', '');
                                              row.click();
                                              group.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                          }
                                      }
                                      return;
                                  }
                                  var params = {};
                                  hash.split('&').forEach(function(p) {
                                      var kv = p.split('=');
                                      if (kv.length === 2) params[kv[0]] = decodeURIComponent(kv[1]);
                                  });
                                  if (params.q) {
                                      var sb = document.getElementById('searchbar');
                                      if (sb) { sb.value = params.q; run_search_scenarios(); }
                                  }
                                  if (params.status) {
                                      params.status.split(',').forEach(function(s) {
                                          var btn = document.querySelector('.status-toggle[data-status="' + s + '"]');
                                          if (btn) btn.classList.add('status-active');
                                      });
                                      filter_statuses();
                                  }
                                  if (params.depmode === 'OR') {
                                      _depMode = 'OR';
                                      var modeBtn = document.querySelector('.dep-mode-toggle');
                                      if (modeBtn) modeBtn.textContent = 'OR';
                                  }
                                  if (params.catmode === 'AND') {
                                      _catMode = 'AND';
                                      var catModeBtn = document.querySelector('.cat-mode-toggle');
                                      if (catModeBtn) catModeBtn.textContent = 'AND';
                                  }
                                  if (params.deps) {
                                      params.deps.split(',').forEach(function(d) {
                                          var btn = document.querySelector('.dependency-toggle[data-dependency="' + d + '"]');
                                          if (btn) btn.classList.add('dependency-active');
                                      });
                                      filter_dependencies();
                                  }
                                  if (params.hp === '1') {
                                      var hp = document.querySelector('.happy-path-toggle');
                                      if (hp) { hp.classList.add('happy-path-active'); filter_happy_paths(); }
                                  }
                                  if (params.pctl) {
                                      document.querySelectorAll('.percentile-btn').forEach(function(b) {
                                          if (b.textContent === params.pctl) {
                                              b.classList.add('percentile-active');
                                              if (b.getAttribute('data-custom') === '1') {
                                                  var cw = document.getElementById('custom-duration-wrap');
                                                  if (cw) cw.style.display = 'inline-flex';
                                              }
                                          }
                                      });
                                  }
                                  if (params.dur) {
                                      var dur = document.getElementById('duration-threshold');
                                      if (dur) { dur.value = params.dur; filter_duration(); }
                                  }
                                  if (params.cats) {
                                      var allBtn = document.querySelector('.category-toggle[data-category=""]');
                                      if (allBtn) allBtn.classList.remove('category-active');
                                      params.cats.split(',').forEach(function(c) {
                                          var btn = document.querySelector('.category-toggle[data-category="' + c + '"]');
                                          if (btn) btn.classList.add('category-active');
                                      });
                                      filter_categories();
                                  }
                              }
                              """;

        // Keyboard navigation
        var keyboardNavigationFunction = """
                                         document.addEventListener('keydown', function(e) {
                                             if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
                                             if (e.key === '/') {
                                                 e.preventDefault();
                                                 var sb = document.getElementById('searchbar');
                                                 if (sb) sb.focus();
                                                 return;
                                             }
                                             var scenarios = Array.from(document.querySelectorAll('details.scenario')).filter(function(s) { return s.style.display !== 'none'; });
                                             if (scenarios.length === 0) return;
                                             var focused = document.querySelector('.scenario-focused');
                                             var idx = focused ? scenarios.indexOf(focused) : -1;
                                             if (e.key === 'ArrowDown') {
                                                 e.preventDefault();
                                                 if (focused) focused.classList.remove('scenario-focused');
                                                 idx = (idx + 1) % scenarios.length;
                                                 scenarios[idx].classList.add('scenario-focused');
                                                 scenarios[idx].scrollIntoView({ behavior: 'smooth', block: 'center' });
                                                 var feature = scenarios[idx].closest('details.feature');
                                                 if (feature) feature.setAttribute('open', '');
                                             } else if (e.key === 'ArrowUp') {
                                                 e.preventDefault();
                                                 if (focused) focused.classList.remove('scenario-focused');
                                                 idx = idx <= 0 ? scenarios.length - 1 : idx - 1;
                                                 scenarios[idx].classList.add('scenario-focused');
                                                 scenarios[idx].scrollIntoView({ behavior: 'smooth', block: 'center' });
                                                 var feature = scenarios[idx].closest('details.feature');
                                                 if (feature) feature.setAttribute('open', '');
                                             } else if (e.key === 'Enter' && focused) {
                                                 e.preventDefault();
                                                 if (focused.hasAttribute('open')) focused.removeAttribute('open');
                                                 else focused.setAttribute('open', '');
                                             }
                                         });
                                         """;

        // Deep link + init script
        var initScript = """
                         document.addEventListener('DOMContentLoaded', function() {
                             // Restore filters from URL hash if present
                             if (window.location.hash && window.location.hash.length > 1) {
                                 parse_url_hash();
                             }

                             // #10 Back-to-top FAB — show after scrolling 2 viewports
                             var backToTop = document.getElementById('back-to-top');
                             if (backToTop) {
                                 window.addEventListener('scroll', function() {
                                     backToTop.style.display = window.scrollY > window.innerHeight * 2 ? 'block' : 'none';
                                 }, { passive: true });
                             }

                             // #2 Collapsible filter section on mobile
                             var isMobile = window.matchMedia('(max-width: 768px)').matches;
                             if (isMobile) {
                                 var filtersDiv = document.querySelector('.filters');
                                 var filterToggle = document.querySelector('.mobile-filter-toggle');
                                 if (filtersDiv && filterToggle) {
                                     filtersDiv.style.display = 'none';
                                     filterToggle.addEventListener('click', function() {
                                         var isOpen = filterToggle.classList.toggle('filter-open');
                                         filtersDiv.style.display = isOpen ? 'flex' : 'none';
                                     });
                                 }
                             }

                             // #4 Per-scenario diagram controls toggle on mobile
                             if (isMobile) {
                                 document.querySelectorAll('.diagram-toggle').forEach(function(controls) {
                                     controls.classList.add('scenario-diagram-controls');
                                     controls.style.display = 'none';
                                     var btn = document.createElement('button');
                                     btn.className = 'scenario-diagram-controls-toggle';
                                     btn.textContent = '\u2699 Diagram Settings';
                                     btn.style.display = 'block';
                                     controls.parentNode.insertBefore(btn, controls);
                                     btn.addEventListener('click', function() {
                                         var showing = controls.style.display === 'flex';
                                         controls.style.display = showing ? 'none' : 'flex';
                                         btn.textContent = showing ? '\u2699 Diagram Settings' : '\u2699 Hide Settings';
                                     });
                                 });
                             }
                         });
                         """;

        var combinedStylesheet = $"""
                                 {Stylesheets.HtmlReportStyleSheet}
                                 {stylesheet}
                                 """;

        var isPlantUmlBrowser = plantUmlRendering == PlantUmlRendering.BrowserJs;
        var isInlineSvg = !isPlantUmlBrowser && inlineSvgRendering;
        var hasInteractiveDiagrams = isPlantUmlBrowser || isInlineSvg;
        var hasAssertionNotes = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("<<assertionNote>>"))) ||
            diagrams.Any(d => d.CodeBehind.Contains("<<assertionNote>>")));
        var hasStepDelimiters = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && l.PlantUml.Contains("<<stepDelimiter>>"))) ||
            diagrams.Any(d => d.CodeBehind.Contains("<<stepDelimiter>>")));
        var hasDatabaseParticipants = isPlantUmlBrowser && (
            (trackedLogs is not null && trackedLogs.Any(l => l.PlantUml is not null && (l.PlantUml.Contains("\ndatabase \"") || l.PlantUml.Contains("\ncollections \"")))) ||
            diagrams.Any(d => d.CodeBehind.Contains("\ndatabase \"") || d.CodeBehind.Contains("\ncollections \"")));
        var plantUmlBrowserScript = isPlantUmlBrowser ? DiagramContextMenu.GetPlantUmlBrowserRenderScript() : "";
        var collapsibleNotesScript = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesScript() : "";
        var collapsibleNotesStyles = isPlantUmlBrowser ? DiagramContextMenu.GetCollapsibleNotesStyles() : "";
        var contextMenuScript = hasInteractiveDiagrams || internalFlowTracking ? DiagramContextMenu.GetContextMenuScript() : "";
        var contextMenuStyles = hasInteractiveDiagrams || internalFlowTracking ? DiagramContextMenu.GetStyles() : "";
        var inlineSvgStyles = (isInlineSvg || isPlantUmlBrowser) ? DiagramContextMenu.GetInlineSvgStyles() : "";
        var internalFlowPopupStyles = internalFlowTracking ? DiagramContextMenu.GetInternalFlowPopupStyles() : "";
        var internalFlowPopupScript = internalFlowTracking ? DiagramContextMenu.GetInternalFlowPopupScript() : "";
        var flameChartRenderScript = internalFlowTracking ? DiagramContextMenu.GetFlameChartRenderScript() : "";
        var toggleScript = internalFlowTracking ? DiagramContextMenu.GetToggleScript() : "";

        var customCssBlock = customCss is not null ? $"<style>{customCss}</style>" : "";
        var faviconLink = $"<link rel=\"icon\" href=\"{customFaviconBase64 ?? Constants.DefaultFavicon.DataUri}\">";

        var enrichSearchDataScript = "";

        var advancedSearchScript = AdvancedSearchJs.Value;

        var html = $$"""
                    <!DOCTYPE html>
                    <html>
                        <head>
                            <meta charset="utf-8" />
                            <meta name="viewport" content="width=device-width, initial-scale=1" />
                            <meta name="generator" content="Kronikol v{{KronikolVersion}}" />
                            <title>{{title}}</title>
                            <style>
                                {{combinedStylesheet}}
                                {{contextMenuStyles}}
                                {{inlineSvgStyles}}
                                {{collapsibleNotesStyles}}
                                {{internalFlowPopupStyles}}
                            </style>
                            {{customCssBlock}}
                            {{faviconLink}}
                            <script>
                                {{advancedSearchScript}}
                                {{scenarioFeatureMapHelper}}
                                {{toggleHappyPathsFunction}}
                                {{searchFunction}}
                                {{dependencyFilterFunction}}
                                {{categoryFilterFunction}}
                                {{statusFilterFunction}}
                                {{collapseExpandAllFunction}}
                                {{lightboxFunction}}
                                {{toggleTableRefFunction}}
                                {{sortTableFunction}}
                                {{copyScenarioNameFunction}}
                                {{toggleExamplesDetailFunction}}
                                {{selectRowFunction}}
                                {{toggleFlattenParamsJs}}
                                {{paramExpandJs}}
                                {{toggleTimelineFunction}}
                                {{toggleComponentDiagramFunction}}
                                {{jumpToFailureFunction}}
                                {{durationFilterFunction}}
                                {{exportFunction}}
                                {{persistentFilterFunction}}
                                {{urlHashFunction}}
                                {{keyboardNavigationFunction}}
                                {{initScript}}
                                {{enrichSearchDataScript}}
                            </script>
                            {{plantUmlBrowserScript}}
                            {{collapsibleNotesScript}}
                            {{contextMenuScript}}
                            {{flameChartRenderScript}}
                            {{internalFlowDataScript}}
                            {{internalFlowPopupScript}}
                            {{toggleScript}}
                        </head>
                        <body>
                    """;

        var body = new StringBuilder();
        if (customLogoHtml is not null)
            body.Append($"<div class=\"custom-logo\">{customLogoHtml}</div>");
        body.Append($"<h1>{title}</h1>");

        if (includeTestRunData)
        {
            var numberOfFeatures = features.Length;
            var scenarios = features.SelectMany(x => x.Scenarios).ToArray();
            var passedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Passed).ToArray();
            var skippedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Skipped).ToArray();
            var failedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Failed).ToArray();
            var overallStatus = failedScenarios.Any() ? "Failed" : "Passed";

            // Feature summary table (collapsible, above execution summary)
            var hasAnySteps = features.Any(f => f.Scenarios.Any(s => s.Steps is { Length: > 0 }));
            var hasAnyDurations = features.Any(f => f.Scenarios.Any(s => s.Duration.HasValue));
            var nextCol = 5;
            body.Append("<details class=\"features-summary-details\"><summary class=\"h2\">Features Summary</summary>");
            body.Append("<div class=\"features-summary-table-wrapper\">");
            body.Append("<table class=\"feature-summary-table\"><thead><tr>");
            body.Append("<th onclick=\"sort_table(0)\">Feature</th>");
            body.Append("<th onclick=\"sort_table(1)\">Scenarios</th>");
            body.Append("<th onclick=\"sort_table(2)\">Passed</th>");
            body.Append("<th onclick=\"sort_table(3)\">Failed</th>");
            body.Append("<th onclick=\"sort_table(4)\">Skipped</th>");
            if (hasAnySteps)
            {
                body.Append($"<th onclick=\"sort_table({nextCol++})\">Steps</th>");
                body.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Passed</th>");
                body.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Failed</th>");
                body.Append($"<th class=\"step-status-header\" onclick=\"sort_table({nextCol++})\">Skipped</th>");
            }
            if (hasAnyDurations)
            {
                body.Append($"<th onclick=\"sort_table({nextCol++})\">Duration</th>");
                body.Append($"<th onclick=\"sort_table({nextCol++})\">Avg</th>");
                body.Append($"<th onclick=\"sort_table({nextCol})\">Longest</th>");
            }
            body.Append("</tr></thead><tbody>");

            foreach (var feature in features)
            {
                var totalSc = feature.Scenarios.Length;
                var passedSc = feature.Scenarios.Count(s => s.Result == ExecutionResult.Passed);
                var failedSc = feature.Scenarios.Count(s => s.Result == ExecutionResult.Failed);
                var skippedSc = feature.Scenarios.Count(s => s.Result is ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure);
                var featureHasFail = failedSc > 0;

                body.Append($"<tr{(featureHasFail ? " class=\"failed\"" : "")}>");
                body.Append($"<td>{System.Net.WebUtility.HtmlEncode(feature.DisplayName)}</td>");
                body.Append($"<td>{totalSc}</td>");
                body.Append($"<td>{passedSc}</td>");
                body.Append($"<td>{failedSc}</td>");
                body.Append($"<td>{skippedSc}</td>");

                if (hasAnySteps)
                {
                    var allSteps = feature.Scenarios
                        .Where(s => s.Steps is not null)
                        .SelectMany(s => s.Steps!)
                        .ToArray();
                    var stepCount = CountStepsRecursive(allSteps);
                    var stepStatusCounts = CountStepsByStatusRecursive(allSteps);
                    body.Append($"<td>{stepCount}</td>");
                    body.Append($"<td>{stepStatusCounts.Passed}</td>");
                    body.Append($"<td>{stepStatusCounts.Failed}</td>");
                    body.Append($"<td>{stepStatusCounts.Skipped}</td>");
                }

                if (hasAnyDurations)
                {
                    var durations = feature.Scenarios.Where(s => s.Duration.HasValue).Select(s => s.Duration!.Value).ToArray();
                    var totalDuration = durations.Length > 0 ? durations.Aggregate(TimeSpan.Zero, (a, b) => a + b) : TimeSpan.Zero;
                    var avgDuration = durations.Length > 0 ? totalDuration / durations.Length : TimeSpan.Zero;
                    var maxDuration = durations.Length > 0 ? durations.Max() : TimeSpan.Zero;
                    body.Append($"<td>{FormatDuration(totalDuration)}</td>");
                    body.Append($"<td>{FormatDuration(avgDuration)}</td>");
                    body.Append($"<td>{FormatDuration(maxDuration)}</td>");
                }

                body.Append("</tr>");
            }

            body.Append("</tbody></table>");
            body.Append("</div>");
            body.Append("</details>");

            body.Append($"""
                    <div class="header-row">
                    <div class="test-execution-summary">
                        <h2>Test Execution Summary</h2>
                        <table>
                            <tr><td colspan="2" class="column-header">Execution</td><td colspan="2" class="column-header">Content</td></tr>
                            <tr><td>Overall status:</td><td>{overallStatus}</td><td>Features: </td><td>{numberOfFeatures}</td></tr>
                            <tr><td>Start Date:</td><td>{startRunTime:yyyy-MM-dd} (UTC)</td><td>Scenarios: </td><td>{scenarios.Length}</td></tr>
                            <tr><td>Start Time:</td><td>{startRunTime:HH:mm:ss} (UTC)</td><td>Passed Scenarios: </td><td>{passedScenarios.Length}</td></tr>
                            <tr><td>End Time:</td><td>{endRunTime:HH:mm:ss} (UTC)</td><td>Failed Scenarios: </td><td>{failedScenarios.Length}</td></tr>
                            <tr><td>Duration:</td><td>{FormatDuration(endRunTime - startRunTime)}</td><td>Skipped Scenarios: </td><td>{skippedScenarios.Length}</td></tr>
                            <tr style="display:none"><td>Kronikol Version:</td><td>{KronikolVersion}</td><td></td><td></td></tr>
                        </table>
                    </div>
                    """);

            if (ciMetadata is not null)
            {
                body.Append("<div class=\"ci-chart-group\">");
                body.Append("<div class=\"ci-metadata\"><table>");
                body.Append($"<tr><td colspan=\"2\" class=\"column-header\">CI ({ciMetadata.Provider})</td></tr>");
                if (ciMetadata.BuildNumber is not null)
                    body.Append($"<tr><td>Build #:</td><td>{System.Net.WebUtility.HtmlEncode(ciMetadata.BuildNumber)}</td></tr>");
                if (ciMetadata.Branch is not null)
                    body.Append($"<tr><td>Branch:</td><td>{System.Net.WebUtility.HtmlEncode(ciMetadata.Branch)}</td></tr>");
                if (ciMetadata.CommitSha is not null)
                {
                    var shortSha = ciMetadata.CommitSha.Length > 7 ? ciMetadata.CommitSha[..7] : ciMetadata.CommitSha;
                    body.Append($"<tr><td>Commit:</td><td><code title=\"{System.Net.WebUtility.HtmlEncode(ciMetadata.CommitSha)}\">{System.Net.WebUtility.HtmlEncode(shortSha)}</code></td></tr>");
                }
                if (ciMetadata.PipelineUrl is not null)
                    body.Append($"<tr><td>Pipeline:</td><td><a href=\"{System.Net.WebUtility.HtmlEncode(ciMetadata.PipelineUrl)}\" target=\"_blank\" rel=\"noopener noreferrer\">View Run</a></td></tr>");
                if (ciMetadata.Repository is not null)
                    body.Append($"<tr><td>Repository:</td><td>{System.Net.WebUtility.HtmlEncode(ciMetadata.Repository)}</td></tr>");
                body.Append("</table></div>");
            }

            var bypassedScenarios = scenarios.Where(x => x.Result == ExecutionResult.Bypassed).ToArray();
            body.Append(GeneratePieChartSvg(passedScenarios.Length, failedScenarios.Length, skippedScenarios.Length, bypassedScenarios.Length));

            if (ciMetadata is not null)
                body.Append("</div>"); // close ci-chart-group
        }

        var diagramsByTestId = diagrams.ToLookup(x => x.TestRuntimeId);

        // Extract dependencies and search terms per scenario from diagram source code
        var scenarioDependencies = new Dictionary<string, HashSet<string>>();
        var scenarioDiagramSearchTerms = new Dictionary<string, HashSet<string>>();
        var allDependencies = new HashSet<string>();
        foreach (var feature in features)
        foreach (var scenario in feature.Scenarios)
        {
            var deps = new HashSet<string>();
            var searchTerms = new HashSet<string>();
            foreach (var diagram in diagramsByTestId[scenario.Id])
            {
                foreach (var dep in ExtractDependencies(diagram.CodeBehind, diagramFormat))
                    deps.Add(dep);
                foreach (var term in ExtractDiagramSearchTerms(diagram.CodeBehind))
                    searchTerms.Add(term);
            }
            scenarioDependencies[scenario.Id] = deps;
            scenarioDiagramSearchTerms[scenario.Id] = searchTerms;
            foreach (var d in deps) allDependencies.Add(d);
        }

        body.Append($"""
                 <div class="filtering-box">
                    <div class="filtering-box-header"><h2>Filtering</h2><div class="filtering-box-export"><button class="export-btn" onclick="clear_all_filters()">Clear All</button><button class="export-btn" onclick="export_html()">Export Filtered HTML</button><button class="export-btn" onclick="export_csv()">Export Filtered CSV</button></div></div>
                    <div class="filter-search"><input id="searchbar" placeholder="Search... (@tag, $status, &&, ||, !!, parentheses)" onkeyup="search_scenarios()" /><button type="button" class="search-help-toggle" onclick="toggle_search_help()" title="Search syntax help">?</button></div>
                    <div class="mobile-filter-toggle">Filters</div>
                    <div class="filters">
                    <div class="search-help-panel" style="display:none">
                    <table class="search-help-table">
                    <tr><th>Syntax</th><th>Meaning</th><th>Example</th></tr>
                    <tr><td><code>word</code></td><td>Text search (feature name, scenario name, step text, tags, diagram source)</td><td><code>order</code></td></tr>
                    <tr><td><code>"phrase"</code></td><td>Exact phrase match</td><td><code>"create order"</code></td></tr>
                    <tr><td><code>&&</code></td><td>AND — both sides must match</td><td><code>order && create</code></td></tr>
                    <tr><td><code>||</code></td><td>OR — either side must match</td><td><code>payment || order</code></td></tr>
                    <tr><td><code>!!</code></td><td>NOT — excludes matches</td><td><code>order && !!delete</code></td></tr>
                    <tr><td><code>( )</code></td><td>Parentheses — group expressions</td><td><code>(a || b) && c</code></td></tr>
                    <tr><td><code>@tag</code></td><td>Filter by tag / category</td><td><code>@smoke && @api</code></td></tr>
                    <tr><td><code>$status</code></td><td>Filter by status</td><td><code>$failed</code>, <code>$passed</code>, <code>$skipped</code></td></tr>
                    </table>
                    <p class="search-help-note">Space-separated words use implicit AND. Press <kbd>/</kbd> to focus the search bar. Operators <code>&&</code> <code>||</code> <code>!!</code> activate advanced mode; without them, legacy tag expressions (<code>@a and @b or not @c</code>) are also supported.</p>
                    </div>
                    <div class="filter-row">
                 """);

        // Status filter toggles
        {
            body.Append("""<div class="status-filters"><span class="status-filters-label">Status:</span>""");
            foreach (var status in Enum.GetValues<ExecutionResult>().OrderBy(s => s))
            {
                if (status == ExecutionResult.SkippedAfterFailure) continue;
                var statusName = status.ToString();
                body.Append($"""<button class="status-toggle" data-status="{statusName}" onclick="toggle_status(this)">{statusName}</button>""");
            }
            body.Append("</div>");
        }

        body.Append("""
                    <div class="happy-path-filters"><span class="happy-path-filters-label">Happy Paths:</span><button class="happy-path-toggle" onclick="toggle_happy_paths(this)">Happy Paths Only</button></div>
                 """);

        body.Append("</div>"); // close filter-row

        // Duration filter (only shown when scenarios have duration data)
        if (hasDurations)
        {
            var durationsMs = features.SelectMany(f => f.Scenarios)
                .Where(s => s.Duration.HasValue)
                .Select(s => s.Duration!.Value.TotalMilliseconds)
                .OrderBy(d => d)
                .ToArray();
            var p50Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.50)] : 0;
            var p90Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.90)] : 0;
            var p95Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.95)] : 0;
            var p99Ms = durationsMs.Length > 0 ? durationsMs[(int)(durationsMs.Length * 0.99)] : 0;

            body.Append($"""<div class="duration-filters" data-p50="{p50Ms:F0}" data-p90="{p90Ms:F0}" data-p95="{p95Ms:F0}" data-p99="{p99Ms:F0}"><span class="duration-filters-label">Duration ≥:</span><button class="percentile-btn" data-threshold-ms="{p50Ms:F0}" onclick="set_percentile(this)">P50 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p50Ms))})</button><button class="percentile-btn" data-threshold-ms="{p90Ms:F0}" onclick="set_percentile(this)">P90 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p90Ms))})</button><button class="percentile-btn" data-threshold-ms="{p95Ms:F0}" onclick="set_percentile(this)">P95 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p95Ms))})</button><button class="percentile-btn" data-threshold-ms="{p99Ms:F0}" onclick="set_percentile(this)">P99 ({FormatDurationBadge(TimeSpan.FromMilliseconds(p99Ms))})</button><button class="percentile-btn" data-custom="1" onclick="set_percentile(this)">Custom</button><span id="custom-duration-wrap" style="display:none;align-items:center;gap:0.3em"><input id="duration-threshold" type="number" step="0.1" min="0" placeholder="seconds" onchange="filter_duration()" /><span class="duration-filters-unit">seconds</span></span></div>""");
        }

        if (allDependencies.Count > 0)
        {
            body.Append("""<div class="dependency-filters"><span class="dependency-filters-label">Dependencies:</span><button class="dep-mode-toggle" title="AND: show scenarios matching ALL selected dependencies. OR: show scenarios matching ANY selected dependency. Click to toggle." onclick="toggle_dep_mode(this)">AND</button>""");
            foreach (var dep in allDependencies.OrderBy(d => d))
            {
                body.Append($"""<button class="dependency-toggle" data-dependency="{System.Net.WebUtility.HtmlEncode(dep)}" onclick="toggle_dependency(this)">{System.Net.WebUtility.HtmlEncode(dep)}</button>""");
            }
            body.Append("</div>");
        }

        // Category filter (only shown when scenarios have category data)
        var allCategories = features.SelectMany(f => f.Scenarios)
            .Where(s => s.Categories is { Length: > 0 })
            .SelectMany(s => s.Categories!)
            .Distinct()
            .OrderBy(c => c)
            .ToArray();
        if (allCategories.Length > 0)
        {
            body.Append("""<div class="category-filters"><span class="category-filters-label">Categories:</span><button class="cat-mode-toggle" title="OR: show scenarios matching ANY selected category. AND: show scenarios matching ALL selected categories. Click to toggle." onclick="toggle_cat_mode(this)">OR</button>""");
            body.Append("""<button class="category-toggle category-active" data-category="" onclick="toggle_category(this)">All</button>""");
            foreach (var cat in allCategories)
            {
                body.Append($"""<button class="category-toggle" data-category="{System.Net.WebUtility.HtmlEncode(cat)}" onclick="toggle_category(this)">{System.Net.WebUtility.HtmlEncode(cat)}</button>""");
            }
            body.Append("""<button class="category-toggle" data-category="__uncategorized__" onclick="toggle_category(this)">Uncategorized</button>""");
            body.Append("</div>");
        }

        body.Append("</div>"); // close filters
        body.Append("</div>"); // close filtering-box
        if (includeTestRunData)
            body.Append("</div>"); // close header-row

        // Toolbar row: expand buttons left, Details/Headers right
        body.Append("""<div class="toolbar-row">""");
        body.Append("""<div class="toolbar-left"><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.feature', 'Expand All Features', 'Collapse All Features')">Expand All Features</button><button class="collapse-expand-all" onclick="toggle_expand_collapse(this, 'details.scenario', 'Expand All Scenarios', 'Collapse All Scenarios')">Expand All Scenarios</button>""");
        if (hasDurations)
            body.Append("""<button class="timeline-toggle" onclick="toggle_timeline(this)">Scenario Timeline</button>""");
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))
            body.Append("""<button class="timeline-toggle" onclick="toggle_component_diagram(this)">Component Diagram</button>""");
        body.Append("</div>");
        body.Append("""<div class="toolbar-right">""");
        if (isPlantUmlBrowser)
        {
            body.Append("""<span class="details-radio"><span class="details-radio-label">Details:</span><button class="details-radio-btn" data-state="expanded" onclick="window._setReportDetails('expanded')">Expand</button><button class="details-radio-btn" data-state="collapsed" onclick="window._setReportDetails('collapsed')">Collapse</button><button class="details-radio-btn details-active" data-state="truncated" onclick="window._setReportDetails('truncated')">Truncate</button><select class="truncate-lines-select" onchange="window._setTruncateLines(this)"><option value="3">3</option><option value="4">4</option><option value="5">5</option><option value="10">10</option><option value="15">15</option><option value="20">20</option><option value="25">25</option><option value="30">30</option><option value="35">35</option><option value="40" selected>40</option><option value="50">50</option><option value="60">60</option><option value="80">80</option><option value="100">100</option></select><span class="truncate-lines-label">lines</span></span>""");
            body.Append("""<button class="details-radio-btn toggle-btn details-active" data-toggle="headers" data-shown="true" onclick="window._toggleHeaders(this)">Headers Shown</button>""");
            if (hasAssertionNotes)
                body.Append("""<button class="details-radio-btn toggle-btn" data-toggle="assertions" data-shown="false" onclick="window._toggleAssertions(this)">Assertions Hidden</button>""");
            if (hasStepDelimiters)
                body.Append("""<button class="details-radio-btn toggle-btn details-active" data-toggle="steps" data-shown="true" onclick="window._toggleSteps(this)">Steps Shown</button>""");
            if (hasDatabaseParticipants)
                body.Append("""<button class="details-radio-btn toggle-btn details-active" data-toggle="databases" data-shown="true" onclick="window._toggleDatabases(this)">Databases Shown</button>""");
        }
        body.Append("</div>");
        body.Append("</div>");

        var plantUmlBrowserCounter = 0;
        var diagramDataMap = new Dictionary<string, string>();

        // Pre-compute median span count for outlier detection
        var medianSpanCount = 0;
        if (wholeTestSegments is not null && wholeTestSegments.Count > 0)
        {
            var spanCounts = wholeTestSegments.Values
                .Where(s => s.Spans.Length > 0)
                .Select(s => s.Spans.Length)
                .OrderBy(c => c)
                .ToArray();
            if (spanCounts.Length > 0)
                medianSpanCount = spanCounts[(spanCounts.Length - 1) / 2];
        }

        // Failure clusters
        var allScenarios = features.SelectMany(f => f.Scenarios).ToArray();

        // Pre-compute unique anchor IDs for all scenarios (handle duplicate display names)
        var scenarioAnchorIds = new Dictionary<string, string>();
        var anchorIdCounts = new Dictionary<string, int>();
        foreach (var scenario in allScenarios)
        {
            var baseAnchor = GenerateScenarioAnchorId(scenario.DisplayName);
            if (anchorIdCounts.TryGetValue(baseAnchor, out var count))
            {
                anchorIdCounts[baseAnchor] = count + 1;
                scenarioAnchorIds[scenario.Id] = $"{baseAnchor}-{count + 1}";
            }
            else
            {
                anchorIdCounts[baseAnchor] = 1;
                scenarioAnchorIds[scenario.Id] = baseAnchor;
            }
        }

        var clusters = FailureClusterer.Cluster(allScenarios);
        if (clusters.Length > 0)
        {
            // Build scenario-to-feature lookup for display
            var scenarioFeatureLookup = new Dictionary<string, string>();
            foreach (var feature in features)
            foreach (var scenario in feature.Scenarios)
                scenarioFeatureLookup[scenario.Id] = feature.DisplayName;

            body.Append("<details class=\"failure-clusters\" open>");
            body.Append($"<summary>Failure Clusters ({clusters.Length} root cause{(clusters.Length == 1 ? "" : "s")})</summary>");
            foreach (var cluster in clusters)
            {
                var anchorLinks = string.Join("", cluster.Scenarios.Select(s =>
                {
                    var anchorId = scenarioAnchorIds[s.Id];
                    var featureName = scenarioFeatureLookup.GetValueOrDefault(s.Id, "");
                    var prefix = featureName.Length > 0 ? $"<span style=\"color:rgb(100,100,100);font-size:0.85em\">{System.Net.WebUtility.HtmlEncode(featureName)} &rsaquo;</span> " : "";
                    return $"<li>{prefix}<a class=\"failure-cluster-scenario-link\" href=\"#{anchorId}\" onclick=\"event.preventDefault();var el=document.getElementById('{anchorId}');if(el){{var p=el;while(p){{if(p.tagName==='DETAILS')p.setAttribute('open','');p=p.parentElement;}}if(el.tagName==='TR')el.click();else el.setAttribute('open','');el.scrollIntoView({{behavior:'smooth',block:'start'}});history.replaceState(null,'',location.pathname+location.search+'#{anchorId}');}}\">{System.Net.WebUtility.HtmlEncode(s.DisplayName)}</a></li>";
                }));
                body.Append($"<details class=\"failure-cluster\"><summary>{System.Net.WebUtility.HtmlEncode(cluster.ClusterKey)}<span class=\"failure-cluster-count\">{cluster.Scenarios.Length} scenarios</span></summary>");
                body.Append($"<ul class=\"failure-cluster-scenarios\">{anchorLinks}</ul></details>");
            }
            body.Append("</details>");
        }

        // Scenario timeline / Gantt (hidden by default)
        if (hasDurations)
        {
            var timelineScenarios = features
                .SelectMany(f => f.Scenarios.Select(s => (Feature: f.DisplayName, Scenario: s)))
                .Where(x => x.Scenario.Duration.HasValue)
                .OrderByDescending(x => x.Scenario.Duration!.Value)
                .ToArray();

            if (timelineScenarios.Length > 0)
            {
                var maxDuration = timelineScenarios.Max(x => x.Scenario.Duration!.Value.TotalMilliseconds);
                body.Append("<div id=\"scenario-timeline\" class=\"scenario-timeline\" style=\"display:none\">");
                body.Append("<div class=\"timeline-header\">Scenario Timeline <span class=\"timeline-info\" title=\"The Scenario Timeline shows every test scenario ordered by duration (longest first). Each bar is proportional to the scenario's elapsed time, colour-coded by result: green = passed, red = failed, yellow = skipped. Use it to quickly spot slow tests, compare relative durations, and identify performance outliers across the entire test run.\">&#x1F6C8;</span></div>");
                foreach (var (featureName, scenario) in timelineScenarios)
                {
                    var durationMs = scenario.Duration!.Value.TotalMilliseconds;
                    var widthPercent = maxDuration > 0 ? (durationMs / maxDuration * 100) : 0;
                    var statusClass = scenario.Result switch
                    {
                        ExecutionResult.Failed => "timeline-bar-failed",
                        ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "timeline-bar-skipped",
                        ExecutionResult.Bypassed => "timeline-bar-bypassed",
                        _ => "timeline-bar-passed"
                    };
                    body.Append($"<div class=\"timeline-row\">");
                    body.Append($"<div class=\"timeline-label\" title=\"{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}\">{System.Net.WebUtility.HtmlEncode(scenario.DisplayName)}</div>");
                    body.Append($"<div class=\"timeline-track\"><div class=\"timeline-bar {statusClass}\" style=\"width:{widthPercent:F1}%\" title=\"{FormatDurationBadge(scenario.Duration.Value)}\"></div></div>");
                    body.Append($"<div class=\"timeline-duration\">{FormatDurationBadge(scenario.Duration.Value)}</div>");
                    body.Append("</div>");
                }
                body.Append("</div>");
            }
        }

        // Embedded component diagram
        if (!string.IsNullOrEmpty(componentDiagramPlantUml))
        {
            var compDiagramId = $"puml-{plantUmlBrowserCounter++}";
            var compDiagramCompressed = InternalFlowHtmlGenerator.CompressToBase64(componentDiagramPlantUml);
            diagramDataMap[compDiagramId] = compDiagramCompressed;
            body.Append($"""<div id="component-diagram" class="component-diagram-section" style="display:none"><div class="plantuml-browser" id="{compDiagramId}" data-diagram-type="plantuml"></div></div>""");
        }

        body.Append("<div id=\"report-content\">");
        var paramGroupCounter = 0;
        foreach (var feature in features)
        {
            var featureHasFailures = feature.Scenarios.Any(s => s.Result == ExecutionResult.Failed);
            var featureAllSkipped = !featureHasFailures && feature.Scenarios.All(s => s.Result == ExecutionResult.Skipped);
            body.Append($"""
                     <details class="feature">
                        <summary class="h2{(featureHasFailures ? " failed" : featureAllSkipped ? " skipped" : "")}">{feature.DisplayName}{(feature.Endpoint is null ? "" : $" <div class=\"endpoint\">{feature.Endpoint}</div>")}{(feature.Labels is { Length: > 0 } fl ? string.Concat(fl.Select(l => $" <span class=\"label\">{System.Net.WebUtility.HtmlEncode(l)}</span>")) : "")}</summary>
                     """);

            if (feature.Description is not null)
            {
                body.Append($"""<div class="feature-description">{System.Net.WebUtility.HtmlEncode(feature.Description)}</div>""");
            }

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName).ToArray();

            // Group parameterized scenarios using ParameterGrouper
            Func<Scenario[], bool> diagramComparer = groupScenarios =>
            {
                if (groupScenarios.Length < 2) return false;
                var firstDiags = diagramsByTestId[groupScenarios[0].Id].Select(d => d.CodeBehind).OrderBy(s => s).ToArray();
                if (firstDiags.Length == 0) return false;
                for (var gi = 1; gi < groupScenarios.Length; gi++)
                {
                    var thisDiags = diagramsByTestId[groupScenarios[gi].Id].Select(d => d.CodeBehind).OrderBy(s => s).ToArray();
                    if (!firstDiags.SequenceEqual(thisDiags)) return false;
                }
                return true;
            };
            var (paramGroups, _) = ParameterGrouper.Analyze(orderedScenarios, groupParameterizedTests, maxParameterColumns, diagramComparer);

            // Build lookup from scenario ID → group for first-encounter rendering
            var scenarioToGroup = new Dictionary<string, ParameterizedGroup>();
            var renderedGroupKeys = new HashSet<string>();
            foreach (var pg in paramGroups)
                foreach (var s in pg.Scenarios)
                    scenarioToGroup[s.Id] = pg;

            // Group by Rule for rendering
            string? currentRule = "__NOTSET__";
            var ruleOpen = false;
            foreach (var scenario in orderedScenarios)
            {
                // Is this scenario part of a parameterized group?
                ParameterizedGroup? group = null;
                string? groupKey = null;
                if (scenarioToGroup.TryGetValue(scenario.Id, out var g))
                {
                    groupKey = g.GroupDisplayName + "|" + string.Join(",", g.Scenarios.Select(s => s.Id));
                    if (renderedGroupKeys.Contains(groupKey))
                        continue;
                    group = g;
                }

                if (scenario.Rule != currentRule)
                {
                    if (ruleOpen)
                    {
                        body.Append("</details>"); // close previous rule
                    }
                    currentRule = scenario.Rule;
                    if (currentRule is not null)
                    {
                        body.Append($"<details class=\"rule\" open><summary class=\"h2-5\">{System.Net.WebUtility.HtmlEncode(currentRule)}</summary>");
                        ruleOpen = true;
                    }
                    else
                    {
                        ruleOpen = false;
                    }
                }

                // Render parameterized group
                if (group is not null)
                {
                    renderedGroupKeys.Add(groupKey!);
                    var groupPrefix = $"pgrp{paramGroupCounter++}";
                    RenderParameterizedGroup(body, group, groupPrefix, diagramsByTestId, scenarioDependencies,
                        scenarioDiagramSearchTerms,
                        showStepNumbers, isPlantUmlBrowser, isInlineSvg, lazyLoadImages,
                        ref plantUmlBrowserCounter, diagramDataMap, wholeTestSegments, trackedLogs, wholeTestVisualization, medianSpanCount,
                        titleizeParameterNames,
                        hasAssertionNotes: hasAssertionNotes,
                        hasStepDelimiters: hasStepDelimiters,
                        hasDatabaseParticipants: hasDatabaseParticipants,
                        scenarioAnchorIds: scenarioAnchorIds,
                        featureDisplayName: feature.DisplayName,
                        featureDescription: feature.Description,
                        featureLabels: feature.Labels);
                    continue;
                }

                var failed = scenario.Result == ExecutionResult.Failed;
                var skipped = scenario.Result == ExecutionResult.Skipped;
                var depsAttr = scenarioDependencies.TryGetValue(scenario.Id, out var deps) && deps.Count > 0
                    ? $" data-dependencies=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", deps.OrderBy(d => d)))}\""
                    : "";
                var statusAttr = $" data-status=\"{scenario.Result}\"";

                // Duration attributes and badge
                var durationAttr = "";
                var durationBadge = "";
                if (scenario.Duration.HasValue)
                {
                    var durationMs = scenario.Duration.Value.TotalMilliseconds;
                    durationAttr = $" data-duration-ms=\"{durationMs:F0}\"";
                    var durationClass = durationMs < 2000 ? "duration-fast" : durationMs < 5000 ? "duration-moderate" : "duration-slow";
                    durationBadge = $" <span class=\"duration-badge {durationClass}\">{FormatDurationBadge(scenario.Duration.Value)}</span>";
                }

                // Deep link anchor ID
                var anchorId = scenarioAnchorIds[scenario.Id];

                // Pre-build searchable text: feature context + scenario name + error info + step text + diagram sources + tags
                var searchParts = new List<string> { feature.DisplayName, scenario.DisplayName };
                if (feature.Description is not null) searchParts.Add(feature.Description);
                if (scenario.Rule is not null) searchParts.Add(scenario.Rule);
                if (feature.Labels is { Length: > 0 }) searchParts.AddRange(feature.Labels);
                if (scenario.Categories is { Length: > 0 }) searchParts.AddRange(scenario.Categories);
                if (scenario.Labels is { Length: > 0 }) searchParts.AddRange(scenario.Labels);
                if (failed && scenario.ErrorMessage is not null) searchParts.Add(scenario.ErrorMessage);
                CollectStepText(scenario.Steps, searchParts);
                if (scenarioDiagramSearchTerms.TryGetValue(scenario.Id, out var diagramTerms) && diagramTerms.Count > 0)
                    searchParts.AddRange(diagramTerms);
                var searchAttr = $" data-search=\"{System.Net.WebUtility.HtmlEncode(string.Join(" ", searchParts).ToLowerInvariant())}\"";

                var categoriesAttr = scenario.Categories is { Length: > 0 }
                    ? $" data-categories=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", scenario.Categories))}\""
                    : "";

                var labelsAttr = scenario.Labels is { Length: > 0 }
                    ? $" data-labels=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", scenario.Labels))}\""
                    : "";

                var encodedName = System.Net.WebUtility.HtmlEncode(scenario.DisplayName);
                var scenarioLabelsHtml = scenario.Labels is { Length: > 0 }
                    ? string.Concat(scenario.Labels
                        .Where(l => !scenario.IsHappyPath || !l.Equals("Happy Path", StringComparison.OrdinalIgnoreCase))
                        .Select(l => $" <span class=\"label\">{System.Net.WebUtility.HtmlEncode(l)}</span>"))
                    : "";

                var scenarioTooltip = scenario.Result switch
                {
                    ExecutionResult.Passed => "Passed — all assertions passed",
                    ExecutionResult.Failed => "Failed — an assertion or runtime failure occurred",
                    ExecutionResult.Skipped => "Skipped — either the entire test did not run (e.g. a skip attribute or filter excluded it), or a step was skipped at runtime which also prevented all subsequent steps from executing",
                    ExecutionResult.Bypassed => "Bypassed — some or all of the logic in a step was intentionally skipped over at runtime without preventing execution of subsequent steps",
                    ExecutionResult.SkippedAfterFailure => "Skipped after failure — this scenario was never reached because an earlier step failed",
                    _ => ""
                };

                body.Append($"""
                         <details class="scenario{(scenario.IsHappyPath ? " happy-path" : "")}"{depsAttr}{statusAttr}{searchAttr}{durationAttr}{categoriesAttr}{labelsAttr} id="{anchorId}" tabindex="0">
                            <summary class="h3{(failed ? " failed" : skipped ? " skipped" : "")}" title="{scenarioTooltip}">{scenario.DisplayName}{(scenario.IsHappyPath ? " <span class=\"label\">Happy Path</span>" : "")}{scenarioLabelsHtml}{durationBadge}<button class="copy-scenario-name" title="Copy scenario name" data-scenario-name="{encodedName}" onclick="copy_scenario_name(this, event)">&#128203;</button><a class="scenario-link" href="#{anchorId}" title="Link to this scenario" onclick="event.stopPropagation()">&#128279;</a></summary>
                         """);

                if (failed)
                {
                    var diffHtml = "";
                    var diffResult = ErrorDiffParser.TryParseExpectedActual(scenario.ErrorMessage);
                    if (diffResult is not null)
                        diffHtml = ErrorDiffParser.GenerateDiffHtml(diffResult.Expected, diffResult.Actual);

                    body.Append($"""
                              <details class="failure-result" open>
                                 <summary class="h4">Failure Result</summary>
                                 <pre>
                              Failure Cause: {scenario.ErrorMessage}
                              
                              {scenario.ErrorStackTrace}
                                 </pre>
                                 {diffHtml}
                              </details>
                              """);
                }

                if (scenario.BackgroundSteps is { Length: > 0 })
                {
                    body.Append("""<details class="scenario-background">""");
                    body.Append("""<summary class="h4">Background Steps</summary>""");
                    for (var bi = 0; bi < scenario.BackgroundSteps.Length; bi++)
                    {
                        var numberPrefix = showStepNumbers ? $"{bi + 1}." : null;
                        RenderStep(body, scenario.BackgroundSteps[bi], numberPrefix, skipTabularInline: false);
                    }
                    body.Append("</details>");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    body.Append("""<details class="scenario-steps" open>""");
                    body.Append("""<summary class="h4">Steps</summary>""");
                    var renderCombined = ShouldRenderCombinedTable(scenario.Steps);
                    var afterThen = false;
                    for (var si = 0; si < scenario.Steps.Length; si++)
                    {
                        var keyword = scenario.Steps[si].Keyword?.Trim();
                        if (keyword?.Equals("Then", StringComparison.OrdinalIgnoreCase) == true)
                            afterThen = true;
                        else if (keyword?.Equals("Given", StringComparison.OrdinalIgnoreCase) == true ||
                                 keyword?.Equals("When", StringComparison.OrdinalIgnoreCase) == true)
                            afterThen = false;

                        var numberPrefix = showStepNumbers ? $"{si + 1}." : null;
                        RenderStep(body, scenario.Steps[si], numberPrefix, skipTabularInline: renderCombined && afterThen);
                    }

                    if (renderCombined)
                        RenderCombinedTabularParameters(body, scenario.Steps);
                    body.Append("</details>");
                }

                if (scenario.Attachments is { Length: > 0 })
                {
                    body.Append("""<div class="scenario-attachments">""");
                    foreach (var attachment in scenario.Attachments)
                    {
                        var ext = Path.GetExtension(attachment.Name).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")
                        {
                            body.Append($"<a class=\"attachment-image-link\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" target=\"_blank\"><img class=\"attachment-image\" src=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
                        }
                        else
                        {
                            body.Append($"<a class=\"step-attachment\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
                        }
                    }
                    body.Append("</div>");
                }

                var diagramsForTest = diagramsByTestId[scenario.Id].ToArray();

                // Get whole-test-flow content (activity + flame) if available
                (string ActivityHtml, string FlameHtml, int SpanCount)? wholeTestContent = null;
                if (wholeTestSegments is not null && wholeTestVisualization != WholeTestFlowVisualization.None)
                {
                    var boundaryLogs = trackedLogs?
                        .Where(l => l.TestId == scenario.Id && l.Type == RequestResponseType.Request && l.Timestamp.HasValue)
                        .OrderBy(l => l.Timestamp!.Value)
                        .Select(l => ($"{l.Method.Value}: {l.Uri.PathAndQuery}", l.Timestamp!.Value))
                        .ToArray() ?? [];

                    wholeTestContent = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
                        wholeTestSegments, scenario.Id, boundaryLogs, wholeTestVisualization, diagramDataMap);
                }

                var hasSequenceDiagrams = diagramsForTest.Length > 0;
                var hasWholeTestFlow = wholeTestContent is not null;

                // Span count warning for outliers (>= 10x median AND > 100 spans)
                var spanWarning = "";
                if (hasWholeTestFlow && medianSpanCount > 0 && wholeTestContent!.Value.SpanCount >= medianSpanCount * 10 && wholeTestContent.Value.SpanCount > 100)
                {
                    var count = wholeTestContent.Value.SpanCount;
                    spanWarning = $"<span class=\"span-count-warning\">(Warning: {count:N0} spans. This might indicate a problem/recursive loop in your test.)</span>";
                }

                if (hasSequenceDiagrams || hasWholeTestFlow)
                {
                    body.Append("<details class=\"example-diagrams\" open>");

                    if (hasWholeTestFlow && hasSequenceDiagrams)
                    {
                        body.Append("<summary class=\"h4\">Diagrams</summary>");
                        body.Append("<div class=\"diagram-toggle\">");
                        body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"seq\">Sequence Diagrams</button>");
                        if (!string.IsNullOrEmpty(wholeTestContent!.Value.ActivityHtml))
                            body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"activity\">Activity Diagrams</button>");
                        if (!string.IsNullOrEmpty(wholeTestContent!.Value.FlameHtml))
                            body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                        body.Append(spanWarning);
                        if (isPlantUmlBrowser)
                            body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                        if (hasAssertionNotes)
                            body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                        if (hasStepDelimiters)
                            body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                        if (hasDatabaseParticipants)
                            body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                        body.Append("</div>");
                    }
                    else if (hasSequenceDiagrams)
                    {
                        body.Append("<summary class=\"h4\">Sequence Diagrams</summary>");
                        if (isPlantUmlBrowser)
                        {
                            body.Append("<div class=\"diagram-toggle\">");
                            body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                            if (hasAssertionNotes)
                                body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                            if (hasStepDelimiters)
                                body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                            if (hasDatabaseParticipants)
                                body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                            body.Append("</div>");
                        }
                    }
                    else
                    {
                        // Only whole-test-flow, no sequence diagrams
                        var hasActivity = !string.IsNullOrEmpty(wholeTestContent!.Value.ActivityHtml);
                        var hasFlame = !string.IsNullOrEmpty(wholeTestContent!.Value.FlameHtml);
                        if (hasActivity && hasFlame)
                        {
                            body.Append("<summary class=\"h4\">Diagrams</summary>");
                            body.Append("<div class=\"diagram-toggle\">");
                            body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"activity\">Activity Diagrams</button>");
                            body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                            body.Append(spanWarning);
                            if (isPlantUmlBrowser)
                                body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                            body.Append("</div>");
                        }
                        else if (hasActivity)
                        {
                            body.Append("<summary class=\"h4\">Activity Diagrams</summary>");
                        }
                        else
                        {
                            body.Append("<summary class=\"h4\">Flame Chart</summary>");
                        }
                    }

                    if (hasSequenceDiagrams)
                    {
                        var seqWrap = hasWholeTestFlow;
                        if (seqWrap) body.Append("<div class=\"diagram-view diagram-view-seq\">");

                        var lazyLoadAttr = lazyLoadImages ? " loading=\"lazy\"" : "";
                        var rawLabel = "Raw Plant UML";
                        foreach (var diagram in diagramsForTest)
                        {
                            if (isPlantUmlBrowser)
                            {
                                var diagramId = $"puml-{plantUmlBrowserCounter++}";
                                var compressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                                diagramDataMap[diagramId] = compressed;
                                body.Append($"""
                                         <div class="plantuml-browser" id="{diagramId}" data-diagram-type="plantuml"></div>
                                         """);
                            }
                            else if (isInlineSvg)
                            {
                                var svgDiagramId = $"puml-svg-{plantUmlBrowserCounter++}";
                                var sourceCompressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                                diagramDataMap[svgDiagramId] = sourceCompressed;
                                body.Append($"""
                                         <div class="plantuml-inline-svg" id="{svgDiagramId}" data-diagram-type="plantuml">{diagram.ImgSrc}</div>
                                         """);
                            }
                            else
                            {
                                body.Append($"""
                                         <details class="example">
                                            <summary class="example-image">
                                                <img{lazyLoadAttr} src="{diagram.ImgSrc}">
                                            </summary>
                                            <div class="raw-plantuml">
                                                <h4>{rawLabel}</h4>
                                                <pre>{diagram.CodeBehind}</pre>
                                             </div>
                                         </details>
                                         """);
                            }
                        }

                        if (seqWrap) body.Append("</div>");
                    }

                    if (hasWholeTestFlow)
                    {
                        var wtf = wholeTestContent!.Value;
                        var hideActivity = hasSequenceDiagrams; // hidden when seq is default
                        var hideFlame = hasSequenceDiagrams || (!string.IsNullOrEmpty(wtf.ActivityHtml) && !hasSequenceDiagrams);

                        if (!string.IsNullOrEmpty(wtf.ActivityHtml))
                            body.Append($"<div class=\"diagram-view diagram-view-activity\"{(hideActivity ? " style=\"display:none\"" : "")}>{wtf.ActivityHtml}</div>");
                        if (!string.IsNullOrEmpty(wtf.FlameHtml))
                            body.Append($"<div class=\"diagram-view diagram-view-flame\"{(hideFlame ? " style=\"display:none\"" : "")}>{wtf.FlameHtml}</div>");
                    }

                    body.Append("</details>");
                }

                body.Append("</details>");
            }
            if (ruleOpen)
            {
                body.Append("</details>"); // close last rule
            }
            body.Append("</details>");
        }
        body.Append("</div>");

        // Jump-to-failure button (only when there are failures)
        if (hasFailures)
        {
            body.Append($"""<button class="jump-to-failure" onclick="jump_to_next_failure()">Next Failure <span class="failure-counter" id="failure-counter">(0/{failureCount})</span></button>""");
        }

        // Back-to-top FAB (#10)
        body.Append("""<button class="back-to-top" id="back-to-top" onclick="window.scrollTo({top:0,behavior:'smooth'})">↑</button>""");

        html += body;
        if (diagramDataMap.Count > 0)
        {
            html += "<script id=\"puml-data\" type=\"application/json\">";
            html += System.Text.Json.JsonSerializer.Serialize(diagramDataMap);
            html += "</script>";
        }
        html += """
                    </body>
                </html>
                """
        ;

        return WriteFile(html, fileName);
    }

    public static string GenerateYamlSpecs(DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        Feature[] features,
        string fileName,
        string title,
        bool generateBlankOnFailedTests = false)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        var yml = new StringBuilder();
        yml.Append("Title: " + title + "\n");
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(x => x.DisplayName))
        {
            yml.Append("  - Feature: " + feature.DisplayName.SanitiseForYml() + "\n");

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                yml.Append("    Description: " + feature.Description.SanitiseForYml() + "\n");

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    yml.Append("      - " + label.SanitiseForYml() + "\n");
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);
            foreach (var scenario in orderedScenarios)
            {
                yml.Append("      - Scenario: " + scenario.DisplayName.SanitiseForYml() + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        yml.Append("          - " + label.SanitiseForYml() + "\n");
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        yml.Append("          - " + cat.SanitiseForYml() + "\n");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    yml.Append("        Steps:\n");
                    foreach (var step in scenario.Steps)
                        AppendYamlStep(yml, step, "          ");
                }

                yml.Append("\n");
            }
        }

        return WriteFile(yml.ToString(), fileName);
    }

    private static void AppendYamlStep(StringBuilder yml, ScenarioStep step, string indent)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;
        yml.Append(indent + "- " + text.SanitiseForYml() + "\n");

        if (step.SubSteps is { Length: > 0 })
        {
            foreach (var sub in step.SubSteps)
                AppendYamlStep(yml, sub, indent + "  ");
        }
    }

    private static int CountStepsRecursive(ScenarioStep[] steps)
    {
        var count = steps.Length;
        foreach (var step in steps)
        {
            if (step.SubSteps is { Length: > 0 })
                count += CountStepsRecursive(step.SubSteps);
        }
        return count;
    }

    private static (int Passed, int Failed, int Skipped) CountStepsByStatusRecursive(ScenarioStep[] steps)
    {
        var passed = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var step in steps)
        {
            switch (step.Status)
            {
                case ExecutionResult.Passed: passed++; break;
                case ExecutionResult.Failed: failed++; break;
                case ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure: skipped++; break;
                default: skipped++; break;
            }
            if (step.SubSteps is { Length: > 0 })
            {
                var sub = CountStepsByStatusRecursive(step.SubSteps);
                passed += sub.Passed;
                failed += sub.Failed;
                skipped += sub.Skipped;
            }
        }
        return (passed, failed, skipped);
    }

    internal static string GeneratePieChartSvg(int passed, int failed, int skipped, int bypassed)
    {
        var total = passed + failed + skipped + bypassed;
        if (total == 0) return "";

        var passRate = (int)Math.Round(100.0 * passed / total);
        var segments = new List<(double pct, string color, string label, int count)>();
        if (passed > 0) segments.Add((100.0 * passed / total, "#1daf26", "Passed", passed));
        if (failed > 0) segments.Add((100.0 * failed / total, "#cc0000", "Failed", failed));
        if (skipped > 0) segments.Add((100.0 * skipped / total, "#949494", "Skipped", skipped));
        if (bypassed > 0) segments.Add((100.0 * bypassed / total, "#2e7bff", "Bypassed", bypassed));

        const double radius = 40;
        const double circumference = 2 * Math.PI * radius;
        var sb = new StringBuilder();
        sb.Append("<div class=\"summary-chart\">");
        sb.Append("<svg viewBox=\"0 0 100 100\">");

        var offset = 0.0;
        foreach (var (pct, color, label, count) in segments)
        {
            var dash = circumference * pct / 100.0;
            var gap = circumference - dash;
            var dashOffset = -offset * circumference / 100.0;
            sb.Append($"<circle cx=\"50\" cy=\"50\" r=\"{radius:F1}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"12\" " +
                      $"stroke-dasharray=\"{dash:F2} {gap:F2}\" stroke-dashoffset=\"{dashOffset:F2}\" transform=\"rotate(-90 50 50)\">" +
                      $"<title>{label}: {count} ({pct:F0}%)</title></circle>");
            offset += pct;
        }

        sb.Append($"<text x=\"50\" y=\"50\" text-anchor=\"middle\" dominant-baseline=\"central\" font-size=\"16\" font-weight=\"bold\" fill=\"#333\">{passRate}%</text>");
        sb.Append("</svg></div>");
        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return $"{total.Milliseconds}ms";
        if (total.TotalMinutes < 1)
            return $"{total.Seconds}s";
        return $"{(int)total.TotalMinutes}m {total.Seconds}s";
    }

    private static bool HasAnyFailed(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Failed) return true;
            if (HasAnyFailed(sub)) return true;
        }
        return false;
    }

    private static bool HasAnyBypassed(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Bypassed) return true;
            if (HasAnyBypassed(sub)) return true;
        }
        return false;
    }

    private static bool HasAnySkipped(ScenarioStep step)
    {
        if (step.SubSteps is not { Length: > 0 }) return false;
        foreach (var sub in step.SubSteps)
        {
            if (sub.Status == ExecutionResult.Skipped) return true;
            if (HasAnySkipped(sub)) return true;
        }
        return false;
    }

    private static void RenderParameterizedGroup(
        StringBuilder body,
        ParameterizedGroup group,
        string prefix,
        ILookup<string, DefaultDiagramsFetcher.DiagramAsCode> diagramsByTestId,
        Dictionary<string, HashSet<string>> scenarioDependencies,
        Dictionary<string, HashSet<string>> scenarioDiagramSearchTerms,
        bool showStepNumbers,
        bool isPlantUmlBrowser,
        bool isInlineSvg,
        bool lazyLoadImages,
        ref int plantUmlBrowserCounter,
        Dictionary<string, string> diagramDataMap,
        Dictionary<string, InternalFlowSegment>? wholeTestSegments,
        RequestResponseLog[]? trackedLogs,
        WholeTestFlowVisualization wholeTestVisualization,
        int medianSpanCount,
        bool titleizeParameterNames = true,
        bool hasAssertionNotes = false,
        bool hasStepDelimiters = false,
        bool hasDatabaseParticipants = false,
        Dictionary<string, string>? scenarioAnchorIds = null,
        string? featureDisplayName = null,
        string? featureDescription = null,
        string[]? featureLabels = null)
    {
        var scenarios = group.Scenarios;

        // Aggregate status
        var hasFailure = scenarios.Any(s => s.Result == ExecutionResult.Failed);
        var hasSkipped = scenarios.Any(s => s.Result == ExecutionResult.Skipped);
        var overallStatus = hasFailure ? ExecutionResult.Failed
            : hasSkipped ? ExecutionResult.Skipped
            : scenarios.Any(s => s.Result == ExecutionResult.Bypassed) ? ExecutionResult.Bypassed
            : ExecutionResult.Passed;

        // Build search text
        var searchParts = new List<string> { group.GroupDisplayName };
        if (featureDisplayName is not null) searchParts.Add(featureDisplayName);
        if (featureDescription is not null) searchParts.Add(featureDescription);
        if (featureLabels is { Length: > 0 }) searchParts.AddRange(featureLabels);
        foreach (var s in scenarios)
        {
            searchParts.Add(s.DisplayName);
            if (s.Rule is not null) searchParts.Add(s.Rule);
            if (s.Categories is { Length: > 0 }) searchParts.AddRange(s.Categories);
            if (s.Labels is { Length: > 0 }) searchParts.AddRange(s.Labels);
            if (s.ErrorMessage is not null) searchParts.Add(s.ErrorMessage);
            CollectStepText(s.Steps, searchParts);
            if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var diagramTerms) && diagramTerms.Count > 0)
                searchParts.AddRange(diagramTerms);
        }
        var searchAttr = $" data-search=\"{System.Net.WebUtility.HtmlEncode(string.Join(" ", searchParts).ToLowerInvariant())}\"";

        // Aggregate categories, labels, dependencies
        var categories = scenarios.Where(s => s.Categories is { Length: > 0 }).SelectMany(s => s.Categories!).Distinct().ToArray();
        var categoriesAttr = categories.Length > 0 ? $" data-categories=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", categories))}\"" : "";
        var labels = scenarios.Where(s => s.Labels is { Length: > 0 }).SelectMany(s => s.Labels!).Distinct().ToArray();
        var labelsAttr = labels.Length > 0 ? $" data-labels=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", labels))}\"" : "";
        var allDeps = scenarios.Where(s => scenarioDependencies.ContainsKey(s.Id)).SelectMany(s => scenarioDependencies[s.Id]).Distinct().OrderBy(d => d).ToArray();
        var depsAttr = allDeps.Length > 0 ? $" data-dependencies=\"{System.Net.WebUtility.HtmlEncode(string.Join(",", allDeps))}\"" : "";

        // Total duration
        var totalDuration = scenarios.Where(s => s.Duration.HasValue).Select(s => s.Duration!.Value).Aggregate(TimeSpan.Zero, (acc, d) => acc + d);
        var durationAttr = totalDuration > TimeSpan.Zero ? $" data-duration-ms=\"{totalDuration.TotalMilliseconds:F0}\"" : "";
        var durationBadge = totalDuration > TimeSpan.Zero
            ? $" <span class=\"duration-badge {(totalDuration.TotalMilliseconds < 2000 ? "duration-fast" : totalDuration.TotalMilliseconds < 5000 ? "duration-moderate" : "duration-slow")}\">{FormatDurationBadge(totalDuration)}</span>"
            : "";

        // Pass/fail summary
        var passCount = scenarios.Count(s => s.Result == ExecutionResult.Passed);
        var failCount = scenarios.Count(s => s.Result == ExecutionResult.Failed);
        var skipCount = scenarios.Count(s => s.Result is ExecutionResult.Skipped or ExecutionResult.Bypassed or ExecutionResult.SkippedAfterFailure);
        var summaryParts = new List<string>();
        if (failCount > 0) summaryParts.Add($"{failCount} failed");
        if (skipCount > 0) summaryParts.Add($"{skipCount} skipped");
        summaryParts.Add($"{passCount}/{scenarios.Length} passed");
        var summaryText = $" <span class=\"label\">{string.Join(", ", summaryParts)}</span>";

        var anchorId = GenerateScenarioAnchorId(group.GroupDisplayName);
        var encodedGroupName = System.Net.WebUtility.HtmlEncode(group.GroupDisplayName);
        var isGroupHappyPath = scenarios.Any(s => s.IsHappyPath);
        var happyPathClass = isGroupHappyPath ? " happy-path" : "";
        var happyPathBadge = isGroupHappyPath ? " <span class=\"label\">Happy Path</span>" : "";

        body.Append($"<details class=\"scenario scenario-parameterized{happyPathClass}\" data-status=\"{overallStatus}\"{depsAttr}{searchAttr}{durationAttr}{categoriesAttr}{labelsAttr} id=\"{anchorId}\" tabindex=\"0\">");
        body.Append($"<summary class=\"h3{(hasFailure ? " failed" : hasSkipped ? " skipped" : "")}\">{encodedGroupName}{happyPathBadge}{summaryText}{durationBadge}<button class=\"copy-scenario-name\" title=\"Copy scenario name\" data-scenario-name=\"{encodedGroupName}\" onclick=\"copy_scenario_name(this, event)\">&#128203;</button><a class=\"scenario-link\" href=\"#{anchorId}\" title=\"Link to this scenario\" onclick=\"event.stopPropagation()\">&#128279;</a></summary>");

        // Parameter table
        var hasFlatView = group.FlatParameterNames is { Length: > 0 };
        if (hasFlatView) body.Append("<div class=\"param-table-wrapper\">");

        // Flat parameter table (visible by default) — shows original Gherkin Example columns as scalar values
        if (hasFlatView)
        {
            var flatNames = group.FlatParameterNames!;
            body.Append($"<table class=\"param-test-table param-table-flat\" data-prefix=\"{prefix}\"><thead>");
            body.Append($"<tr><th rowspan=\"2\" style=\"width:2.5em\">#</th>");
            body.Append($"<th colspan=\"{flatNames.Length}\" class=\"master-header\"><button class=\"flatten-toggle\" onclick=\"toggleFlattenParams(this,'{prefix}')\" title=\"Show grouped columns\">\u2212</button>Input Parameters</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5em\">Status</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5.5em\">Duration</th></tr>");
            body.Append("<tr>");
            foreach (var name in flatNames)
            {
                var displayName = titleizeParameterNames ? name.Titleize() : name;
                body.Append($"<th class=\"sub-header\">{System.Net.WebUtility.HtmlEncode(displayName)}</th>");
            }
            body.Append("</tr></thead><tbody>");

            for (var ri = 0; ri < scenarios.Length; ri++)
            {
                var s = scenarios[ri];
                var rowStatusClass = s.Result switch
                {
                    ExecutionResult.Passed => "row-passed",
                    ExecutionResult.Failed => "row-failed",
                    ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "row-skipped",
                    ExecutionResult.Bypassed => "row-bypassed",
                    _ => ""
                };
                var activeClass = ri == 0 ? " row-active" : "";
                var badgeClass = s.Result switch
                {
                    ExecutionResult.Passed => "badge-pass",
                    ExecutionResult.Failed => "badge-fail",
                    ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "badge-skip",
                    ExecutionResult.Bypassed => "badge-bypass",
                    _ => ""
                };
                var badgeText = s.Result switch
                {
                    ExecutionResult.Passed => "Passed",
                    ExecutionResult.Failed => "Failed",
                    ExecutionResult.Skipped => "Skipped",
                    ExecutionResult.Bypassed => "Bypassed",
                    ExecutionResult.SkippedAfterFailure => "Skipped",
                    _ => ""
                };

                var rowSearchParts = new List<string> { s.DisplayName };
                if (featureDisplayName is not null) rowSearchParts.Add(featureDisplayName);
                if (featureDescription is not null) rowSearchParts.Add(featureDescription);
                if (featureLabels is { Length: > 0 }) rowSearchParts.AddRange(featureLabels);
                if (s.Categories is { Length: > 0 }) rowSearchParts.AddRange(s.Categories);
                if (s.Labels is { Length: > 0 }) rowSearchParts.AddRange(s.Labels);
                if (s.ErrorMessage is not null) rowSearchParts.Add(s.ErrorMessage);
                CollectStepText(s.Steps, rowSearchParts);
                if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var rowDiagramTermsFlat) && rowDiagramTermsFlat.Count > 0)
                    rowSearchParts.AddRange(rowDiagramTermsFlat);
                var rowSearchAttr = $" data-row-search=\"{System.Net.WebUtility.HtmlEncode(string.Join(" ", rowSearchParts).ToLowerInvariant())}\"";

                body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
                body.Append($"<td>{ri + 1}</td>");

                foreach (var name in flatNames)
                {
                    var val = s.ExampleFlatValues?.GetValueOrDefault(name, "") ?? "";
                    body.Append($"<td class=\"mono\">{FormatDisplayValue(val)}</td>");
                }

                var rowDuration = s.Duration.HasValue ? FormatDurationBadge(s.Duration.Value) : "";
                body.Append($"<td><span class=\"status-badge {badgeClass}\">{badgeText}</span></td>");
                body.Append($"<td class=\"mono\">{rowDuration}</td>");
                body.Append("</tr>");
            }
            body.Append("</tbody></table>");
        }

        // Grouped parameter table (hidden when flat view exists)
        var groupedTableClass = hasFlatView ? " param-table-grouped" : "";
        var groupedStyle = hasFlatView ? " style=\"display:none\"" : "";
        body.Append($"<table class=\"param-test-table{groupedTableClass}\"{groupedStyle} data-prefix=\"{prefix}\"><thead>");
        if (group.Rule is ParameterDisplayRule.ScalarColumns or ParameterDisplayRule.FlattenedObject && group.ParameterNames.Length > 0)
        {
            // R1/R2: Two-row header with master "Input Parameters" header
            body.Append($"<tr><th rowspan=\"2\" style=\"width:2.5em\">#</th>");
            var toggleBtn = hasFlatView ? $"<button class=\"flatten-toggle\" onclick=\"toggleFlattenParams(this,'{prefix}')\" title=\"Show flattened columns\">+</button>" : "";
            body.Append($"<th colspan=\"{group.ParameterNames.Length}\" class=\"master-header\">{toggleBtn}Input Parameters</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5em\">Status</th>");
            body.Append("<th rowspan=\"2\" style=\"width:5.5em\">Duration</th></tr>");
            body.Append("<tr>");
            foreach (var name in group.ParameterNames)
            {
                var displayName = titleizeParameterNames ? name.Titleize() : name;
                body.Append($"<th class=\"sub-header\">{System.Net.WebUtility.HtmlEncode(displayName)}</th>");
            }
            body.Append("</tr>");
        }
        else
        {
            // R0: Fallback single-row header
            body.Append("<tr><th style=\"width:2.5em\">#</th><th>Test Case</th><th style=\"width:5em\">Status</th><th style=\"width:5.5em\">Duration</th></tr>");
        }
        body.Append("</thead><tbody>");

        for (var ri = 0; ri < scenarios.Length; ri++)
        {
            var s = scenarios[ri];
            var rowStatusClass = s.Result switch
            {
                ExecutionResult.Passed => "row-passed",
                ExecutionResult.Failed => "row-failed",
                ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "row-skipped",
                ExecutionResult.Bypassed => "row-bypassed",
                _ => ""
            };
            var activeClass = ri == 0 ? " row-active" : "";
            var badgeClass = s.Result switch
            {
                ExecutionResult.Passed => "badge-pass",
                ExecutionResult.Failed => "badge-fail",
                ExecutionResult.Skipped or ExecutionResult.SkippedAfterFailure => "badge-skip",
                ExecutionResult.Bypassed => "badge-bypass",
                _ => ""
            };
            var badgeText = s.Result switch
            {
                ExecutionResult.Passed => "Passed",
                ExecutionResult.Failed => "Failed",
                ExecutionResult.Skipped => "Skipped",
                ExecutionResult.Bypassed => "Bypassed",
                ExecutionResult.SkippedAfterFailure => "Skipped",
                _ => ""
            };

            var rowSearchParts = new List<string> { s.DisplayName };
            if (featureDisplayName is not null) rowSearchParts.Add(featureDisplayName);
            if (featureDescription is not null) rowSearchParts.Add(featureDescription);
            if (featureLabels is { Length: > 0 }) rowSearchParts.AddRange(featureLabels);
            if (s.Categories is { Length: > 0 }) rowSearchParts.AddRange(s.Categories);
            if (s.Labels is { Length: > 0 }) rowSearchParts.AddRange(s.Labels);
            if (s.ErrorMessage is not null) rowSearchParts.Add(s.ErrorMessage);
            CollectStepText(s.Steps, rowSearchParts);
            if (scenarioDiagramSearchTerms.TryGetValue(s.Id, out var rowDiagramTerms) && rowDiagramTerms.Count > 0)
                rowSearchParts.AddRange(rowDiagramTerms);
            var rowSearchAttr = $" data-row-search=\"{System.Net.WebUtility.HtmlEncode(string.Join(" ", rowSearchParts).ToLowerInvariant())}\"";

            var rowAnchorId = scenarioAnchorIds?.GetValueOrDefault(s.Id) ?? GenerateScenarioAnchorId(s.DisplayName);
            body.Append($"<tr class=\"{rowStatusClass}{activeClass}\" data-row-idx=\"{ri}\" id=\"{rowAnchorId}\" data-scenario-id=\"{rowAnchorId}\"{rowSearchAttr} onclick=\"selectRow(this,'{prefix}')\">");
            body.Append($"<td>{ri + 1}</td>");

            if (group.Rule is ParameterDisplayRule.ScalarColumns or ParameterDisplayRule.FlattenedObject && group.ParameterNames.Length > 0)
            {
                // R1/R2: Individual parameter columns with cell-level R3/R4 rendering
                foreach (var name in group.ParameterNames)
                {
                    var rawValue = s.ExampleRawValues?.GetValueOrDefault(name);
                    if (rawValue is not null && ParameterValueRenderer.IsSmallComplexObject(rawValue))
                    {
                        // R3: Sub-table for small complex objects
                        body.Append("<td>");
                        ParameterValueRenderer.RenderSubTable(body, rawValue);
                        body.Append("</td>");
                    }
                    else if (rawValue is not null && ParameterValueRenderer.IsComplexValue(rawValue))
                    {
                        // R4: Expandable details for deeply complex objects
                        body.Append("<td>");
                        ParameterValueRenderer.RenderExpandable(body, rawValue);
                        body.Append("</td>");
                    }
                    else
                    {
                        // Try string-based R3/R4 when raw values aren't available
                        var val = s.ExampleValues?.GetValueOrDefault(name, "") ?? "";
                        var tdBody = new StringBuilder();
                        if (ParameterValueRenderer.TryRenderFromParsedString(tdBody, val))
                        {
                            body.Append("<td>");
                            body.Append(tdBody);
                            body.Append("</td>");
                        }
                        else
                        {
                            // Scalar: plain text
                            body.Append($"<td class=\"mono\">{FormatDisplayValue(val)}</td>");
                        }
                    }
                }
            }
            else
            {
                // R0: Full display name as "Test Case"
                var displayText = s.ExampleDisplayName ?? s.DisplayName;
                body.Append($"<td class=\"mono\">{System.Net.WebUtility.HtmlEncode(displayText)}</td>");
            }

            var rowDuration = s.Duration.HasValue ? FormatDurationBadge(s.Duration.Value) : "";
            body.Append($"<td><span class=\"status-badge {badgeClass}\">{badgeText}</span></td>");
            body.Append($"<td class=\"mono\">{rowDuration}</td>");
            body.Append("</tr>");
        }
        body.Append("</tbody></table>");
        if (hasFlatView) body.Append("</div>"); // close param-table-wrapper

        // Detail panels (steps, failure) — rendered below the parameter table
        var hasAnyDetail = scenarios.Any(s => s.Steps is { Length: > 0 } || s.Result == ExecutionResult.Failed);
        if (hasAnyDetail)
        {
            body.Append($"<div class=\"param-detail-panels\">");
            for (var ri = 0; ri < scenarios.Length; ri++)
            {
                var s = scenarios[ri];
                var display = ri == 0 ? "" : " style=\"display:none\"";
                body.Append($"<div class=\"param-detail-panel\" id=\"{prefix}-detail-{ri}\"{display}>");

                if (s.BackgroundSteps is { Length: > 0 })
                {
                    body.Append("""<details class="scenario-background">""");
                    body.Append("""<summary class="h4">Background Steps</summary>""");
                    for (var bi = 0; bi < s.BackgroundSteps.Length; bi++)
                    {
                        var numberPrefix = showStepNumbers ? $"{bi + 1}." : null;
                        RenderStep(body, s.BackgroundSteps[bi], numberPrefix, skipTabularInline: false);
                    }
                    body.Append("</details>");
                }

                if (s.Steps is { Length: > 0 })
                {
                    body.Append("""<details class="scenario-steps" open>""");
                    body.Append("""<summary class="h4">Steps</summary>""");
                    var renderCombined = ShouldRenderCombinedTable(s.Steps);
                    var afterThen = false;
                    for (var si = 0; si < s.Steps.Length; si++)
                    {
                        var keyword = s.Steps[si].Keyword?.Trim();
                        if (keyword?.Equals("Then", StringComparison.OrdinalIgnoreCase) == true)
                            afterThen = true;
                        else if (keyword?.Equals("Given", StringComparison.OrdinalIgnoreCase) == true ||
                                 keyword?.Equals("When", StringComparison.OrdinalIgnoreCase) == true)
                            afterThen = false;

                        var numberPrefix = showStepNumbers ? $"{si + 1}." : null;
                        RenderStep(body, s.Steps[si], numberPrefix, skipTabularInline: renderCombined && afterThen);
                    }
                    if (renderCombined)
                        RenderCombinedTabularParameters(body, s.Steps);
                    body.Append("</details>");
                }

                if (s.Attachments is { Length: > 0 })
                {
                    body.Append("""<div class="scenario-attachments">""");
                    foreach (var attachment in s.Attachments)
                    {
                        var ext = Path.GetExtension(attachment.Name).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")
                        {
                            body.Append($"<a class=\"attachment-image-link\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" target=\"_blank\"><img class=\"attachment-image\" src=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
                        }
                        else
                        {
                            body.Append($"<a class=\"step-attachment\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
                        }
                    }
                    body.Append("</div>");
                }

                if (s.Result == ExecutionResult.Failed)
                {
                    var diffHtml = "";
                    var diffResult = ErrorDiffParser.TryParseExpectedActual(s.ErrorMessage);
                    if (diffResult is not null)
                        diffHtml = ErrorDiffParser.GenerateDiffHtml(diffResult.Expected, diffResult.Actual);
                    body.Append("<details class=\"failure-result\" open><summary class=\"h4\">Failure Result</summary><pre>");
                    if (s.ErrorMessage is not null)
                        body.Append($"Failure Cause: {s.ErrorMessage}\n\n");
                    if (s.ErrorStackTrace is not null)
                        body.Append(s.ErrorStackTrace);
                    body.Append($"</pre>{diffHtml}</details>");
                }

                body.Append("</div>");
            }
            body.Append("</div>");
        }

        // Compute whole-test-flow content per scenario
        var wholeTestContents = new (string ActivityHtml, string FlameHtml, int SpanCount)?[scenarios.Length];
        if (wholeTestSegments is not null && wholeTestVisualization != WholeTestFlowVisualization.None)
        {
            for (var ri = 0; ri < scenarios.Length; ri++)
            {
                var s = scenarios[ri];
                var boundaryLogs = trackedLogs?
                    .Where(l => l.TestId == s.Id && l.Type == RequestResponseType.Request && l.Timestamp.HasValue)
                    .OrderBy(l => l.Timestamp!.Value)
                    .Select(l => ($"{l.Method.Value}: {l.Uri.PathAndQuery}", l.Timestamp!.Value))
                    .ToArray() ?? [];
                wholeTestContents[ri] = InternalFlowHtmlGenerator.GetWholeTestFlowContent(
                    wholeTestSegments, s.Id, boundaryLogs, wholeTestVisualization, diagramDataMap);
            }
        }

        var hasAnyWholeTestFlow = wholeTestContents.Any(w => w is not null);
        var allWtfIdentical = false;
        if (hasAnyWholeTestFlow && group.AllDiagramsIdentical)
        {
            // Check if all whole-test-flow content is identical too
            var firstActivity = wholeTestContents[0]?.ActivityHtml ?? "";
            var firstFlame = wholeTestContents[0]?.FlameHtml ?? "";
            allWtfIdentical = wholeTestContents.All(w =>
                (w?.ActivityHtml ?? "") == firstActivity && (w?.FlameHtml ?? "") == firstFlame);
        }

        // Diagrams
        var hasAnySeqDiagrams = scenarios.Any(s => diagramsByTestId[s.Id].Any());
        var hasDiagramContent = hasAnySeqDiagrams || hasAnyWholeTestFlow;

        if (hasDiagramContent)
        {
            body.Append("<details class=\"example-diagrams\" open>");

            // Determine toggle buttons needed
            var showSeqToggle = hasAnySeqDiagrams;
            var showActivityToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.ActivityHtml));
            var showFlameToggle = hasAnyWholeTestFlow && wholeTestContents.Any(w => !string.IsNullOrEmpty(w?.FlameHtml));
            var multipleTypes = (showSeqToggle ? 1 : 0) + (showActivityToggle ? 1 : 0) + (showFlameToggle ? 1 : 0) > 1;

            if (multipleTypes)
            {
                body.Append("<summary class=\"h4\">Diagrams</summary>");
                body.Append("<div class=\"diagram-toggle\">");
                if (showSeqToggle)
                    body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"seq\">Sequence Diagrams</button>");
                if (showActivityToggle)
                    body.Append($"<button class=\"diagram-toggle-btn{(!showSeqToggle ? " diagram-toggle-active" : "")}\" data-dtype=\"activity\">Activity Diagrams</button>");
                if (showFlameToggle)
                    body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                if (isPlantUmlBrowser && showSeqToggle)
                    body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                if (hasAssertionNotes)
                    body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                if (hasStepDelimiters)
                    body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                if (hasDatabaseParticipants)
                    body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                body.Append("</div>");
            }
            else if (showSeqToggle)
            {
                body.Append("<summary class=\"h4\">Sequence Diagrams</summary>");
                if (isPlantUmlBrowser)
                {
                    body.Append("<div class=\"diagram-toggle\">");
                    body.Append("<span class=\"diagram-toggle-spacer\"></span><span class=\"details-radio\"><span class=\"details-radio-label\">Details:</span><button class=\"details-radio-btn\" data-state=\"expanded\" onclick=\"window._setAllNotes(this,'expanded')\">Expand</button><button class=\"details-radio-btn\" data-state=\"collapsed\" onclick=\"window._setAllNotes(this,'collapsed')\">Collapse</button><button class=\"details-radio-btn details-active\" data-state=\"truncated\" onclick=\"window._setAllNotes(this,'truncated')\">Truncate</button><select class=\"truncate-lines-select\" onchange=\"window._setScenarioTruncateLines(this)\"><option value=\"3\">3</option><option value=\"4\">4</option><option value=\"5\">5</option><option value=\"10\">10</option><option value=\"15\">15</option><option value=\"20\">20</option><option value=\"25\">25</option><option value=\"30\">30</option><option value=\"35\">35</option><option value=\"40\" selected>40</option><option value=\"50\">50</option><option value=\"60\">60</option><option value=\"80\">80</option><option value=\"100\">100</option></select><span class=\"truncate-lines-label\">lines</span></span><button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"headers\" data-shown=\"true\" onclick=\"window._toggleScenarioHeaders(this)\">Headers Shown</button>");
                    if (hasAssertionNotes)
                        body.Append("<button class=\"details-radio-btn toggle-btn\" data-toggle=\"assertions\" data-shown=\"false\" onclick=\"window._toggleScenarioAssertions(this)\">Assertions Hidden</button>");
                    if (hasStepDelimiters)
                        body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"steps\" data-shown=\"true\" onclick=\"window._toggleScenarioSteps(this)\">Steps Shown</button>");
                    if (hasDatabaseParticipants)
                        body.Append("<button class=\"details-radio-btn toggle-btn details-active\" data-toggle=\"databases\" data-shown=\"true\" onclick=\"window._toggleScenarioDatabases(this)\">Databases Shown</button>");
                    body.Append("</div>");
                }
            }
            else if (showActivityToggle && showFlameToggle)
            {
                body.Append("<summary class=\"h4\">Diagrams</summary>");
                body.Append("<div class=\"diagram-toggle\">");
                body.Append("<button class=\"diagram-toggle-btn diagram-toggle-active\" data-dtype=\"activity\">Activity Diagrams</button>");
                body.Append("<button class=\"diagram-toggle-btn\" data-dtype=\"flame\">Flame Chart</button>");
                body.Append("</div>");
            }
            else if (showActivityToggle)
            {
                body.Append("<summary class=\"h4\">Activity Diagrams</summary>");
            }
            else
            {
                body.Append("<summary class=\"h4\">Flame Chart</summary>");
            }

            // Sequence diagrams
            if (hasAnySeqDiagrams)
            {
                var seqWrap = hasAnyWholeTestFlow && multipleTypes;
                if (seqWrap) body.Append("<div class=\"diagram-view diagram-view-seq\">");

                if (group.AllDiagramsIdentical)
                {
                    var firstDiagrams = diagramsByTestId[scenarios[0].Id].ToArray();
                    if (firstDiagrams.Length > 0)
                    {
                        body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                        RenderDiagramsForScenario(body, firstDiagrams, isPlantUmlBrowser, isInlineSvg, lazyLoadImages, ref plantUmlBrowserCounter, diagramDataMap);
                    }
                }
                else
                {
                    for (var ri = 0; ri < scenarios.Length; ri++)
                    {
                        var s = scenarios[ri];
                        var display = ri == 0 ? "" : " style=\"display:none\"";
                        body.Append($"<div id=\"{prefix}-diagram-{ri}\"{display}>");
                        var diagrams = diagramsByTestId[s.Id].ToArray();
                        if (diagrams.Length > 0)
                            RenderDiagramsForScenario(body, diagrams, isPlantUmlBrowser, isInlineSvg, lazyLoadImages, ref plantUmlBrowserCounter, diagramDataMap);
                        body.Append("</div>");
                    }
                }

                if (seqWrap) body.Append("</div>");
            }

            // Activity diagrams
            if (showActivityToggle)
            {
                var hideActivity = showSeqToggle; // hidden when seq is default
                if (hideActivity) body.Append("<div class=\"diagram-view diagram-view-activity\" style=\"display:none\">");
                else body.Append("<div class=\"diagram-view diagram-view-activity\">");

                if (allWtfIdentical && wholeTestContents[0] is not null)
                {
                    body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                    body.Append(wholeTestContents[0]!.Value.ActivityHtml);
                }
                else
                {
                    for (var ri = 0; ri < scenarios.Length; ri++)
                    {
                        var display = ri == 0 ? "" : " style=\"display:none\"";
                        body.Append($"<div id=\"{prefix}-activity-{ri}\"{display}>");
                        if (wholeTestContents[ri] is not null && !string.IsNullOrEmpty(wholeTestContents[ri]!.Value.ActivityHtml))
                            body.Append(wholeTestContents[ri]!.Value.ActivityHtml);
                        body.Append("</div>");
                    }
                }

                body.Append("</div>");
            }

            // Flame charts
            if (showFlameToggle)
            {
                var hideFlame = showSeqToggle || (showActivityToggle && !showSeqToggle);
                if (hideFlame) body.Append("<div class=\"diagram-view diagram-view-flame\" style=\"display:none\">");
                else body.Append("<div class=\"diagram-view diagram-view-flame\">");

                if (allWtfIdentical && wholeTestContents[0] is not null)
                {
                    body.Append("<span class=\"param-diagram-identical-badge\">All diagrams identical across test cases</span>");
                    body.Append(wholeTestContents[0]!.Value.FlameHtml);
                }
                else
                {
                    for (var ri = 0; ri < scenarios.Length; ri++)
                    {
                        var display = ri == 0 ? "" : " style=\"display:none\"";
                        body.Append($"<div id=\"{prefix}-flame-{ri}\"{display}>");
                        if (wholeTestContents[ri] is not null && !string.IsNullOrEmpty(wholeTestContents[ri]!.Value.FlameHtml))
                            body.Append(wholeTestContents[ri]!.Value.FlameHtml);
                        body.Append("</div>");
                    }
                }

                body.Append("</div>");
            }

            body.Append("</details>");
        }

        body.Append("</details>");
    }

    private static void RenderDiagramsForScenario(
        StringBuilder body,
        DefaultDiagramsFetcher.DiagramAsCode[] diagrams,
        bool isPlantUmlBrowser,
        bool isInlineSvg,
        bool lazyLoadImages,
        ref int plantUmlBrowserCounter,
        Dictionary<string, string> diagramDataMap)
    {
        var lazyLoadAttr = lazyLoadImages ? " loading=\"lazy\"" : "";
        foreach (var diagram in diagrams)
        {
            if (isPlantUmlBrowser)
            {
                var diagramId = $"puml-{plantUmlBrowserCounter++}";
                var compressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                diagramDataMap[diagramId] = compressed;
                body.Append($"<div class=\"plantuml-browser\" id=\"{diagramId}\" data-diagram-type=\"plantuml\"></div>");
            }
            else if (isInlineSvg)
            {
                var svgDiagramId = $"puml-svg-{plantUmlBrowserCounter++}";
                var sourceCompressed = InternalFlowHtmlGenerator.CompressToBase64(diagram.CodeBehind);
                diagramDataMap[svgDiagramId] = sourceCompressed;
                body.Append($"<div class=\"plantuml-inline-svg\" id=\"{svgDiagramId}\" data-diagram-type=\"plantuml\">{diagram.ImgSrc}</div>");
            }
            else
            {
                body.Append($"""
                         <details class="example">
                            <summary class="example-image">
                                <img{lazyLoadAttr} src="{diagram.ImgSrc}">
                            </summary>
                            <div class="raw-plantuml">
                                <h4>Raw Plant UML</h4>
                                <pre>{diagram.CodeBehind}</pre>
                             </div>
                         </details>
                         """);
            }
        }
    }

    private static void RenderStep(StringBuilder body, ScenarioStep step, string? numberPrefix = null, bool skipTabularInline = true)
    {
        var statusClass = step.Status switch
        {
            ExecutionResult.Passed => HasAnySkipped(step) ? "passed-skipped" : HasAnyBypassed(step) ? "passed-bypassed" : "passed",
            ExecutionResult.Failed => "failed",
            ExecutionResult.Skipped => "skipped",
            ExecutionResult.Bypassed => "bypassed",
            ExecutionResult.SkippedAfterFailure => "skipped-after-failure",
            _ => ""
        };

        var statusIcon = step.Status switch
        {
            ExecutionResult.Passed => "&#10003;",
            ExecutionResult.Failed => "&#10005;",
            ExecutionResult.Skipped => "&#216;",
            ExecutionResult.Bypassed => "&#8631;",
            ExecutionResult.SkippedAfterFailure => "!",
            _ => ""
        };

        var statusTooltip = step.Status switch
        {
            ExecutionResult.Passed => HasAnySkipped(step)
                ? "Passed (with skipped sub-steps) — all assertions passed, but one or more sub-steps were skipped. Skipped steps did not execute and also prevented execution of subsequent steps"
                : HasAnyBypassed(step)
                ? "Passed (with bypassed sub-steps) — all assertions passed, but one or more sub-steps were bypassed (intentionally skipped over without preventing execution of subsequent steps)"
                : "Passed — all assertions in this step passed",
            ExecutionResult.Failed => "Failed — this step threw an exception or an assertion failed",
            ExecutionResult.Skipped => "Skipped — this step did not execute because it was intentionally skipped, either at the scenario level, or at the step level. In the latter case the skip also prevented execution of subsequent steps",
            ExecutionResult.Bypassed => "Bypassed — some or all of the logic in this step was intentionally skipped over without preventing execution of subsequent steps",
            ExecutionResult.SkippedAfterFailure => "Skipped after failure — this step was never reached because an earlier step failed",
            _ => ""
        };

        var hasSubSteps = step.SubSteps is { Length: > 0 };

        if (hasSubSteps)
        {
            body.Append(HasAnyFailed(step)
                ? "<details class=\"step step-collapsible\" open>"
                : "<details class=\"step step-collapsible\">");
            body.Append("<summary>");
        }
        else
        {
            body.Append("<div class=\"step\">");
        }

        if (numberPrefix is not null)
        {
            body.Append($"<span class=\"step-number\">{numberPrefix}</span>");
        }

        if (step.Status.HasValue)
        {
            body.Append($"<span class=\"step-status {statusClass}\" title=\"{statusTooltip}\">{statusIcon}</span>");
        }

        if (step.Keyword is not null)
        {
            body.Append($"<span class=\"step-keyword\">{System.Net.WebUtility.HtmlEncode(step.Keyword)}</span> ");
        }

        // Render step text — either structured segments with inline params, or plain text
        if (step.TextSegments is { Length: > 0 })
        {
            body.Append("<span class=\"step-text\">");
            foreach (var seg in step.TextSegments)
            {
                if (seg.Parameter is not null)
                {
                    var paramStatusClass = seg.Parameter.Status switch
                    {
                        VerificationStatus.Success => "param-success",
                        VerificationStatus.Failure => "param-failure",
                        VerificationStatus.Exception => "param-exception",
                        VerificationStatus.NotProvided => "param-not-provided",
                        _ => "param-na"
                    };
                    var display = seg.Parameter.Expectation is not null
                        ? $"{FormatDisplayValue(seg.Parameter.Value)}/{FormatDisplayValue(seg.Parameter.Expectation)}"
                        : FormatDisplayValue(seg.Parameter.Value);
                    var titleAttr = seg.ParameterName is not null
                        ? $" title=\"{System.Net.WebUtility.HtmlEncode(seg.ParameterName)}\""
                        : "";
                    body.Append($"<span class=\"step-param-inline {paramStatusClass}\"{titleAttr}>{display}</span>");
                }
                else if (seg.TableReference is not null)
                {
                    // Check if this table reference has a backing table/tree parameter
                    var matchingParam = step.Parameters?.FirstOrDefault(p => p.Name == seg.TableReference);
                    if (matchingParam is { Kind: StepParameterKind.Inline, InlineValue: not null } &&
                        ParameterParser.IsComplexObjectString(matchingParam.InlineValue.Value))
                    {
                        // Complex inline value with no backing table — render based on size
                        if (ParameterParser.IsSmallComplexValue(matchingParam.InlineValue.Value))
                        {
                            // Small: render inline like a normal parameter
                            var inlineDisplay = ParameterParser.FormatComplexValueInline(matchingParam.InlineValue.Value)
                                                ?? matchingParam.InlineValue.Value;
                            body.Append($"<span class=\"step-param-inline param-na\" title=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(inlineDisplay)}</span>");
                        }
                        else
                        {
                            // Large: render as expandable button with data-value
                            var json = ParameterParser.FormatComplexValueAsJson(matchingParam.InlineValue.Value)
                                       ?? matchingParam.InlineValue.Value;
                            body.Append("</span>");
                            body.Append($"<button class=\"step-table-ref\" onclick=\"toggle_table_ref(this)\" data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\" data-value=\"{System.Net.WebUtility.HtmlEncode(json)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");
                            body.Append("<span class=\"step-text\">");
                        }
                    }
                    else
                    {
                        // Check for simple inline value (not complex), or no matching param at all
                        if (matchingParam is { Kind: StepParameterKind.Inline, InlineValue: not null })
                        {
                            // Simple inline value — render as inline span showing the value
                            var display = FormatDisplayValue(matchingParam.InlineValue.Value);
                            body.Append($"<span class=\"step-param-inline param-na\" title=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{display}</span>");
                        }
                        else if (matchingParam is { Kind: StepParameterKind.Tabular or StepParameterKind.Tree })
                        {
                            // Table/Tree parameter — render as button (scrolls to table)
                            body.Append("</span>");
                            body.Append($"<button class=\"step-table-ref\" onclick=\"toggle_table_ref(this)\" data-param=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReference)}</button>");
                            body.Append("<span class=\"step-text\">");
                        }
                        else
                        {
                            // No matching parameter — render formatted value if available, otherwise plain text
                            if (seg.TableReferenceFormattedValue is not null)
                            {
                                body.Append($"<span class=\"step-param-inline param-na\" title=\"{System.Net.WebUtility.HtmlEncode(seg.TableReference)}\">{System.Net.WebUtility.HtmlEncode(seg.TableReferenceFormattedValue)}</span>");
                            }
                            else
                            {
                                body.Append(System.Net.WebUtility.HtmlEncode(seg.TableReference));
                            }
                        }
                    }
                }
                else if (seg.Text is not null)
                {
                    body.Append(System.Net.WebUtility.HtmlEncode(seg.Text));
                }
            }
            body.Append("</span>");
        }
        else
        {
            var stepText = step.Text;

            // Strip tabular parameter reference suffixes like [paramName: "<$paramName>"] from step text
            if (step.Parameters?.Any(p => p.Kind == StepParameterKind.Tabular) == true)
                stepText = StripTabularParamSuffixRegex().Replace(stepText, "").TrimEnd();

            body.Append($"<span class=\"step-text\">{System.Net.WebUtility.HtmlEncode(stepText)}</span>");
        }

        if (step.Duration.HasValue)
        {
            body.Append($" <span class=\"step-duration\">({FormatDurationBadge(step.Duration.Value)})</span>");
        }

        if (step.Comments is { Length: > 0 })
        {
            foreach (var comment in step.Comments)
            {
                body.Append($"<div class=\"step-comment\">{System.Net.WebUtility.HtmlEncode(comment)}</div>");
            }
        }

        if (step.Attachments is { Length: > 0 })
        {
            foreach (var attachment in step.Attachments)
            {
                var ext = Path.GetExtension(attachment.Name).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp")
                {
                    body.Append($"<a class=\"attachment-image-link\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" onclick=\"openLightbox(event, this)\"><img class=\"attachment-image\" src=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\" alt=\"{System.Net.WebUtility.HtmlEncode(attachment.Name)}\" /></a>");
                    body.Append($"<span class=\"attachment-image-name\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</span>");
                }
                else
                {
                    body.Append($"<a class=\"step-attachment\" href=\"{System.Net.WebUtility.HtmlEncode(attachment.RelativePath)}\">{System.Net.WebUtility.HtmlEncode(attachment.Name)}</a>");
                }
            }
        }

        if (step.Parameters is { Length: > 0 })
        {
            foreach (var param in step.Parameters)
            {
                if (skipTabularInline && param.Kind == StepParameterKind.Tabular) continue; // Rendered as combined table at scenario level
                if (step.TextSegments is { Length: > 0 } && param.Kind == StepParameterKind.Inline) continue; // Already rendered inline in text segments
                RenderParameter(body, param);
            }
        }

        if (step.DocString is not null)
        {
            var codeClassAttr = step.DocStringMediaType is not null
                ? $" class=\"language-{System.Net.WebUtility.HtmlEncode(step.DocStringMediaType)}\""
                : "";
            body.Append($"<pre class=\"step-docstring\"><code{codeClassAttr}>{System.Net.WebUtility.HtmlEncode(step.DocString)}</code></pre>");
        }

        if (hasSubSteps)
        {
            body.Append("</summary>");
            body.Append("<div class=\"sub-steps\">");
            for (var ssi = 0; ssi < step.SubSteps!.Length; ssi++)
            {
                var subPrefix = numberPrefix is not null ? $"{numberPrefix}{ssi + 1}." : null;
                RenderStep(body, step.SubSteps[ssi], subPrefix);
            }
            body.Append("</div>");
            body.Append("</details>");
        }
        else
        {
            body.Append("</div>");
        }
    }

    private static void RenderParameter(StringBuilder body, StepParameter param)
    {
        switch (param.Kind)
        {
            case StepParameterKind.Inline when param.InlineValue is not null:
                var statusClass = param.InlineValue.Status switch
                {
                    VerificationStatus.Success => "param-success",
                    VerificationStatus.Failure => "param-failure",
                    VerificationStatus.Exception => "param-exception",
                    VerificationStatus.NotProvided => "param-not-provided",
                    _ => "param-na"
                };
                var display = param.InlineValue.Expectation is not null
                    ? $"{FormatDisplayValue(param.InlineValue.Value)}/{FormatDisplayValue(param.InlineValue.Expectation)}"
                    : FormatDisplayValue(param.InlineValue.Value);
                body.Append($"<span class=\"step-param-inline {statusClass}\" title=\"{System.Net.WebUtility.HtmlEncode(param.Name)}\">{display}</span>");
                break;

            case StepParameterKind.Tabular when param.TabularValue is not null:
                var colNames = string.Join(",", param.TabularValue.Columns.Select(c => c.Name));
                body.Append($"<div class=\"step-param-table\" data-param=\"{System.Net.WebUtility.HtmlEncode(param.Name)}\" data-columns=\"{System.Net.WebUtility.HtmlEncode(colNames)}\">");
                var showRowIndicator = param.TabularValue.Rows.Any(r => r.Type != TableRowType.Matching);
                body.Append(showRowIndicator ? "<table><thead><tr><th></th>" : "<table><thead><tr>");
                foreach (var col in param.TabularValue.Columns)
                {
                    body.Append($"<th{(col.IsKey ? " class=\"key\"" : "")}>{System.Net.WebUtility.HtmlEncode(col.Name)}</th>");
                }
                body.Append("</tr></thead><tbody>");
                foreach (var row in param.TabularValue.Rows)
                {
                    var rowIndicator = row.Type switch
                    {
                        TableRowType.Matching => "=",
                        TableRowType.Surplus => "+",
                        TableRowType.Missing => "-",
                        _ => ""
                    };
                    body.Append(showRowIndicator
                        ? $"<tr class=\"row-{row.Type.ToString().ToLowerInvariant()}\"><td>{rowIndicator}</td>"
                        : $"<tr class=\"row-{row.Type.ToString().ToLowerInvariant()}\">");
                    foreach (var cell in row.Values)
                    {
                        var cellClass = cell.Status switch
                        {
                            VerificationStatus.Success => "param-success",
                            VerificationStatus.Failure => "param-failure",
                            VerificationStatus.Exception => "param-exception",
                            VerificationStatus.NotProvided => "param-not-provided",
                            _ => ""
                        };
                        var cellDisplay = cell.Expectation is not null && cell.Status == VerificationStatus.Failure
                            ? $"{FormatDisplayValue(cell.Value)}/{FormatDisplayValue(cell.Expectation)}"
                            : FormatDisplayValue(cell.Value);
                        body.Append($"<td class=\"{cellClass}\">{cellDisplay}</td>");
                    }
                    body.Append("</tr>");
                }
                body.Append("</tbody></table></div>");
                break;

            case StepParameterKind.Tree when param.TreeValue is not null:
                body.Append("<div class=\"step-param-tree\">");
                RenderTreeNode(body, param.TreeValue.Root);
                body.Append("</div>");
                break;
        }
    }

    private static void RenderTreeNode(StringBuilder body, TreeNode node)
    {
        var statusClass = node.Status switch
        {
            VerificationStatus.Success => "param-success",
            VerificationStatus.Failure => "param-failure",
            VerificationStatus.Exception => "param-exception",
            VerificationStatus.NotProvided => "param-not-provided",
            _ => ""
        };
        var valueDisplay = node.Expectation is not null && node.Status == VerificationStatus.Failure
            ? $"{FormatDisplayValue(node.Value)}/{FormatDisplayValue(node.Expectation)}"
            : FormatDisplayValue(node.Value);
        body.Append($"<div class=\"tree-node {statusClass}\"><span class=\"tree-node-name\">{System.Net.WebUtility.HtmlEncode(node.Node)}</span>: {valueDisplay}");

        if (node.Children is { Length: > 0 })
        {
            body.Append("<div class=\"tree-children\">");
            foreach (var child in node.Children)
                RenderTreeNode(body, child);
            body.Append("</div>");
        }

        body.Append("</div>");
    }

    private static bool ShouldRenderCombinedTable(ScenarioStep[] steps)
    {
        var afterThen = false;
        TabularParameterValue? setupTable = null;
        TabularParameterValue? assertionTable = null;
        foreach (var step in steps)
        {
            var keyword = step.Keyword?.Trim();
            if (keyword?.Equals("Then", StringComparison.OrdinalIgnoreCase) == true)
                afterThen = true;
            else if (keyword?.Equals("Given", StringComparison.OrdinalIgnoreCase) == true ||
                     keyword?.Equals("When", StringComparison.OrdinalIgnoreCase) == true)
                afterThen = false;

            var tab = step.Parameters?.FirstOrDefault(
                p => p.Kind == StepParameterKind.Tabular && p.TabularValue is not null)?.TabularValue;
            if (tab is not null)
            {
                if (afterThen) assertionTable ??= tab;
                else setupTable ??= tab;
            }
        }

        if (setupTable is null || assertionTable is null) return false;

        if (assertionTable.IsLinkedOutput) return true;

        var outputKeyNames = assertionTable.Columns
            .Where(c => c.IsKey).Select(c => c.Name).ToHashSet();
        if (outputKeyNames.Count > 0 &&
            setupTable.Columns.Any(c => outputKeyNames.Contains(c.Name)))
            return true;

        if (setupTable.Rows.Length > 1 &&
            setupTable.Rows.Length == assertionTable.Rows.Length)
            return true;

        return false;
    }

    private static void RenderCombinedTabularParameters(StringBuilder body, ScenarioStep[] steps)
    {
        var namedParams = steps
            .Where(s => s.Parameters is { Length: > 0 })
            .SelectMany(s => s.Parameters!)
            .Where(p => p.Kind == StepParameterKind.Tabular && p.TabularValue is not null)
            .Select(p => (Name: p.Name, Table: p.TabularValue!))
            .ToArray();

        if (namedParams.Length == 0) return;

        var hasSeparator = namedParams.Length > 1;
        var inputParams = namedParams.Length > 1 ? namedParams[..^1] : namedParams;
        var outputParam = namedParams.Length > 1 ? namedParams[^1] : ((string Name, TabularParameterValue Table)?)null;

        // Determine alignment mode
        var useKeyAlignment = false;
        HashSet<string>? sharedKeyNames = null;
        if (outputParam is not null && !outputParam.Value.Table.IsLinkedOutput)
        {
            var outputKeyNames = outputParam.Value.Table.Columns
                .Where(c => c.IsKey).Select(c => c.Name).ToHashSet();
            if (outputKeyNames.Count > 0)
            {
                sharedKeyNames = new HashSet<string>(
                    inputParams.SelectMany(p => p.Table.Columns)
                        .Where(c => outputKeyNames.Contains(c.Name))
                        .Select(c => c.Name));
                useKeyAlignment = sharedKeyNames.Count > 0;
            }
        }

        // Build aligned row pairs when using key-based alignment
        int[]? inputRowOrder = null;
        int maxRows;
        if (useKeyAlignment && outputParam is not null && inputParams.Length > 0)
        {
            var primaryInput = inputParams[0];
            var keyColIndicesInput = sharedKeyNames!
                .Select(k => Array.FindIndex(primaryInput.Table.Columns, c => c.Name == k))
                .Where(i => i >= 0).ToArray();
            var keyColIndicesOutput = sharedKeyNames!
                .Select(k => Array.FindIndex(outputParam.Value.Table.Columns, c => c.Name == k))
                .Where(i => i >= 0).ToArray();

            var inputKeyLookup = new Dictionary<string, int>();
            for (var i = 0; i < primaryInput.Table.Rows.Length; i++)
            {
                var key = string.Join("\0", keyColIndicesInput.Select(ci =>
                    ci < primaryInput.Table.Rows[i].Values.Length ? primaryInput.Table.Rows[i].Values[ci].Value : ""));
                inputKeyLookup.TryAdd(key, i);
            }

            var alignedInput = new List<int>();
            var matchedInputRows = new HashSet<int>();
            foreach (var outputRow in outputParam.Value.Table.Rows)
            {
                var outputKey = string.Join("\0", keyColIndicesOutput.Select(ci =>
                    ci < outputRow.Values.Length ? outputRow.Values[ci].Value : ""));
                if (inputKeyLookup.TryGetValue(outputKey, out var inputIdx) && matchedInputRows.Add(inputIdx))
                    alignedInput.Add(inputIdx);
                else
                    alignedInput.Add(-1); // no matching input row
            }

            // Append orphaned input rows
            for (var i = 0; i < primaryInput.Table.Rows.Length; i++)
            {
                if (!matchedInputRows.Contains(i))
                    alignedInput.Add(i);
            }

            inputRowOrder = alignedInput.ToArray();
            maxRows = inputRowOrder.Length;
        }
        else
        {
            maxRows = namedParams.Max(t => t.Table.Rows.Length);
        }

        var showRowIndicator = namedParams.Any(t => t.Table.Rows.Any(r => r.Type != TableRowType.Matching));

        body.Append(showRowIndicator
            ? "<div class=\"step-param-combined-table\"><table><thead><tr><th></th>"
            : "<div class=\"step-param-combined-table\"><table><thead><tr>");

        foreach (var param in inputParams)
        {
            var encodedName = System.Net.WebUtility.HtmlEncode(param.Name);
            foreach (var col in param.Table.Columns)
                body.Append($"<th data-param=\"{encodedName}\"{(col.IsKey ? " class=\"key\"" : "")}>{System.Net.WebUtility.HtmlEncode(col.Name)}</th>");
        }

        if (hasSeparator)
        {
            body.Append("<th class=\"combined-separator\">=</th>");

            var encodedOutputName = System.Net.WebUtility.HtmlEncode(outputParam!.Value.Name);
            foreach (var col in outputParam!.Value.Table.Columns)
                body.Append($"<th data-param=\"{encodedOutputName}\"{(col.IsKey ? " class=\"key\"" : "")}>{System.Net.WebUtility.HtmlEncode(col.Name)}</th>");
        }

        body.Append("</tr></thead><tbody>");

        for (var ri = 0; ri < maxRows; ri++)
        {
            var inputRi = inputRowOrder is not null ? inputRowOrder[ri] : ri;
            var outputRi = inputRowOrder is not null
                ? (ri < (outputParam?.Table.Rows.Length ?? 0) ? ri : -1)
                : ri;

            var rowType = outputParam is not null && outputRi >= 0 && outputRi < outputParam.Value.Table.Rows.Length
                ? outputParam.Value.Table.Rows[outputRi].Type
                : inputRi >= 0 && inputParams[0].Table.Rows.Length > inputRi
                    ? inputParams[0].Table.Rows[inputRi].Type
                    : TableRowType.Matching;

            var rowIndicator = rowType switch
            {
                TableRowType.Matching => "=",
                TableRowType.Surplus => "+",
                TableRowType.Missing => "-",
                _ => ""
            };

            body.Append(showRowIndicator
                ? $"<tr class=\"row-{rowType.ToString().ToLowerInvariant()}\"><td>{rowIndicator}</td>"
                : $"<tr class=\"row-{rowType.ToString().ToLowerInvariant()}\">");

            foreach (var param in inputParams)
            {
                var encodedName = System.Net.WebUtility.HtmlEncode(param.Name);
                if (inputRi >= 0 && inputRi < param.Table.Rows.Length)
                {
                    foreach (var cell in param.Table.Rows[inputRi].Values)
                        RenderCell(body, cell, encodedName);
                }
                else
                {
                    for (var ci = 0; ci < param.Table.Columns.Length; ci++)
                        body.Append($"<td data-param=\"{encodedName}\"></td>");
                }
            }

            if (hasSeparator)
            {
                body.Append("<td class=\"combined-separator\"></td>");

                var encodedOutputName = System.Net.WebUtility.HtmlEncode(outputParam!.Value.Name);
                if (outputRi >= 0 && outputRi < outputParam!.Value.Table.Rows.Length)
                {
                    foreach (var cell in outputParam.Value.Table.Rows[outputRi].Values)
                        RenderCell(body, cell, encodedOutputName);
                }
                else
                {
                    for (var ci = 0; ci < outputParam.Value.Table.Columns.Length; ci++)
                        body.Append($"<td data-param=\"{encodedOutputName}\"></td>");
                }
            }

            body.Append("</tr>");
        }

        body.Append("</tbody></table></div>");
    }

    private static void RenderCell(StringBuilder body, TabularCell cell, string? dataParam = null)
    {
        var cellClass = cell.Status switch
        {
            VerificationStatus.Success => "param-success",
            VerificationStatus.Failure => "param-failure",
            VerificationStatus.Exception => "param-exception",
            VerificationStatus.NotProvided => "param-not-provided",
            _ => ""
        };
        var cellDisplay = cell.Expectation is not null && cell.Status == VerificationStatus.Failure
            ? $"{FormatDisplayValue(cell.Value)}/{FormatDisplayValue(cell.Expectation)}"
            : FormatDisplayValue(cell.Value);
        var dataParamAttr = dataParam is not null ? $" data-param=\"{dataParam}\"" : "";
        body.Append($"<td class=\"{cellClass}\"{dataParamAttr}>{cellDisplay}</td>");
    }

    private static string FormatDisplayValue(string? value)
    {
        if (value is null or "null") return "<pre>null</pre>";
        if (value.Length > 0 && value.Trim().Length == 0)
            return $"<pre>{System.Net.WebUtility.HtmlEncode(value)}</pre>";
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static readonly Regex StripTabularParamSuffixCompiledRegex = new(@"\s*\[[a-zA-Z_]\w*:\s*""<\$[a-zA-Z_]\w*>""\]", RegexOptions.Compiled);
    private static Regex StripTabularParamSuffixRegex() => StripTabularParamSuffixCompiledRegex;

    internal static string FormatDurationBadge(TimeSpan duration)
    {
        var total = duration.Duration();
        if (total.TotalSeconds < 1)
            return $"{(int)total.TotalMilliseconds}ms";
        if (total.TotalMinutes < 1)
            return $"{total.TotalSeconds:F1}s";
        return $"{(int)total.TotalMinutes}m {total.Seconds}s";
    }

    internal static string GenerateScenarioAnchorId(string displayName)
    {
        // Convert to lowercase, replace non-alphanumeric with hyphens, collapse multiple hyphens
        var slug = System.Text.RegularExpressions.Regex.Replace(displayName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return $"scenario-{slug}";
    }

    public static string GenerateTestRunReportData(Feature[] features, DateTime startTime, DateTime endTime, string fileName, DataFormat format, DefaultDiagramsFetcher.DiagramAsCode[]? diagrams = null, RequestResponseLog[]? trackedLogs = null)
    {
        var diagramLookup = diagrams?.ToLookup(d => d.TestRuntimeId, d => d.CodeBehind);
        var logLookup = trackedLogs?.ToLookup(l => l.TestId);

        return format switch
        {
            DataFormat.Json => WriteFile(GenerateTestRunReportJson(features, startTime, endTime, diagramLookup, logLookup), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXml(features, startTime, endTime, diagramLookup, logLookup), fileName),
            DataFormat.Yaml => WriteFile(GenerateTestRunReportYaml(features, startTime, endTime, diagramLookup, logLookup), fileName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateTestRunReportJson(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var data = new
        {
            KronikolVersion = KronikolVersion,
            StartTime = startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EndTime = endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Features = features.OrderBy(f => f.DisplayName).Select(f => new
            {
                Name = f.DisplayName,
                f.Endpoint,
                f.Description,
                Labels = f.Labels ?? [],
                Scenarios = f.Scenarios.Select(s =>
                {
                    var scenario = new Dictionary<string, object?>
                    {
                        ["id"] = s.Id,
                        ["stableId"] = ScenarioStableId.Compute(f.DisplayName, s.DisplayName, s.OutlineId),
                        ["name"] = s.DisplayName,
                        ["result"] = s.Result.ToString(),
                        ["durationSeconds"] = s.Duration?.TotalSeconds ?? 0.0,
                        ["isHappyPath"] = s.IsHappyPath,
                        ["errorMessage"] = s.ErrorMessage,
                        ["errorStackTrace"] = s.ErrorStackTrace,
                        ["labels"] = s.Labels ?? [],
                        ["categories"] = s.Categories ?? [],
                        ["rule"] = s.Rule,
                        ["outlineId"] = s.OutlineId,
                        ["exampleValues"] = s.ExampleValues,
                        ["attachments"] = (s.Attachments ?? []).Select(a => new { a.Name, a.RelativePath }).ToArray(),
                        ["backgroundSteps"] = (s.BackgroundSteps ?? []).Select(MapStepJson).ToArray(),
                        ["steps"] = (s.Steps ?? []).Select(MapStepJson).ToArray()
                    };

                    if (diagramLookup != null)
                        scenario["diagrams"] = diagramLookup[s.Id].ToArray();

                    if (logLookup != null)
                        scenario["httpInteractions"] = logLookup[s.Id].Select(MapLogJson).ToArray();

                    return scenario;
                }).ToArray()
            }).ToArray()
        };
        return JsonSerializer.Serialize(data, options);
    }

    private static object MapLogJson(RequestResponseLog log) => new
    {
        Type = log.Type.ToString(),
        Method = log.Method.Value?.ToString()?.ToUpperInvariant(),
        Uri = log.Uri.ToString(),
        log.ServiceName,
        log.CallerName,
        log.Content,
        Headers = log.Headers.Select(h => new { h.Key, h.Value }).ToArray(),
        StatusCode = log.StatusCode?.Value?.ToString(),
        TraceId = log.TraceId.ToString(),
        RequestResponseId = log.RequestResponseId.ToString(),
        Timestamp = log.Timestamp?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    };

    private static object MapStepJson(ScenarioStep step) => new
    {
        step.Keyword,
        step.Text,
        Status = step.Status?.ToString(),
        DurationSeconds = step.Duration?.TotalSeconds,
        SubSteps = (step.SubSteps ?? []).Select(MapStepJson).ToArray(),
        Attachments = (step.Attachments ?? []).Select(a => new { a.Name, a.RelativePath }).ToArray()
    };

    private static string GenerateTestRunReportXml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup)
    {
        var doc = new XDocument(
            new XElement("TestRunReport",
                new XElement("KronikolVersion", KronikolVersion),
                new XElement("StartTime", startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("EndTime", endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")),
                new XElement("Features",
                    features.OrderBy(f => f.DisplayName).Select(f =>
                        new XElement("Feature",
                            new XElement("Name", f.DisplayName),
                            f.Endpoint != null ? new XElement("Endpoint", f.Endpoint) : null,
                            f.Description != null ? new XElement("Description", f.Description) : null,
                            (f.Labels is { Length: > 0 }) ? new XElement("Labels", f.Labels.Select(l => new XElement("Label", l))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.Select(s =>
                                {
                                    var scenarioElements = new List<object?>
                                    {
                                        new XElement("Id", s.Id),
                                        new XElement("StableId", ScenarioStableId.Compute(f.DisplayName, s.DisplayName, s.OutlineId)),
                                        new XElement("Name", s.DisplayName),
                                        new XElement("Result", s.Result.ToString()),
                                        new XElement("DurationSeconds", (s.Duration?.TotalSeconds ?? 0.0).ToString("F3")),
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLower()),
                                        s.ErrorMessage != null ? new XElement("ErrorMessage", s.ErrorMessage) : null,
                                        s.ErrorStackTrace != null ? new XElement("ErrorStackTrace", s.ErrorStackTrace) : null,
                                        (s.Labels is { Length: > 0 }) ? new XElement("Labels", s.Labels.Select(l => new XElement("Label", l))) : null,
                                        (s.Categories is { Length: > 0 }) ? new XElement("Categories", s.Categories.Select(c => new XElement("Category", c))) : null,
                                        s.Rule != null ? new XElement("Rule", s.Rule) : null,
                                        (s.BackgroundSteps is { Length: > 0 }) ? new XElement("BackgroundSteps", s.BackgroundSteps.Select(MapStepXml)) : null,
                                        (s.Steps is { Length: > 0 }) ? new XElement("Steps", s.Steps.Select(MapStepXml)) : null,
                                        (s.Attachments is { Length: > 0 }) ? new XElement("Attachments", s.Attachments.Select(a => new XElement("Attachment", new XElement("Name", a.Name), new XElement("RelativePath", a.RelativePath)))) : null
                                    };

                                    if (diagramLookup != null)
                                    {
                                        var diags = diagramLookup[s.Id].ToArray();
                                        if (diags.Length > 0)
                                            scenarioElements.Add(new XElement("Diagrams", diags.Select(d => new XElement("Diagram", d))));
                                    }

                                    if (logLookup != null)
                                    {
                                        var logs = logLookup[s.Id].ToArray();
                                        if (logs.Length > 0)
                                            scenarioElements.Add(new XElement("HttpInteractions", logs.Select(MapLogXml)));
                                    }

                                    return new XElement("Scenario", scenarioElements.ToArray());
                                })
                            )
                        )
                    )
                )
            )
        );
        return doc.ToString();
    }

    private static XElement MapLogXml(RequestResponseLog log) =>
        new("HttpInteraction",
            new XElement("Type", log.Type.ToString()),
            new XElement("Method", log.Method.Value?.ToString()?.ToUpperInvariant()),
            new XElement("Uri", log.Uri.ToString()),
            new XElement("ServiceName", log.ServiceName),
            new XElement("CallerName", log.CallerName),
            log.Content != null ? new XElement("Content", log.Content) : null,
            log.Headers.Length > 0 ? new XElement("Headers", log.Headers.Select(h => new XElement("Header", new XElement("Key", h.Key), new XElement("Value", h.Value)))) : null,
            log.StatusCode != null ? new XElement("StatusCode", log.StatusCode.Value?.ToString()) : null,
            new XElement("TraceId", log.TraceId.ToString()),
            new XElement("RequestResponseId", log.RequestResponseId.ToString()),
            log.Timestamp != null ? new XElement("Timestamp", log.Timestamp.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")) : null
        );

    private static XElement MapStepXml(ScenarioStep step) =>
        new("Step",
            step.Keyword != null ? new XElement("Keyword", step.Keyword) : null,
            new XElement("Text", step.Text),
            step.Status != null ? new XElement("Status", step.Status.ToString()) : null,
            step.Duration != null ? new XElement("DurationSeconds", step.Duration.Value.TotalSeconds.ToString("F3")) : null,
            (step.SubSteps is { Length: > 0 }) ? new XElement("SubSteps", step.SubSteps.Select(MapStepXml)) : null,
            (step.Attachments is { Length: > 0 }) ? new XElement("Attachments", step.Attachments.Select(a => new XElement("Attachment", new XElement("Name", a.Name), new XElement("RelativePath", a.RelativePath)))) : null
        );

    private static string GenerateTestRunReportYaml(Feature[] features, DateTime startTime, DateTime endTime, ILookup<string, string>? diagramLookup, ILookup<string, RequestResponseLog>? logLookup)
    {
        var yml = new StringBuilder();
        yml.Append("KronikolVersion: " + KronikolVersion + "\n");
        yml.Append("StartTime: " + startTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
        yml.Append("EndTime: " + endTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(f => f.DisplayName))
        {
            yml.Append("  - Name: " + feature.DisplayName.SanitiseForYml() + "\n");

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                yml.Append("    Description: " + feature.Description.SanitiseForYml() + "\n");

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    yml.Append("      - " + label.SanitiseForYml() + "\n");
            }

            yml.Append("    Scenarios:\n");
            foreach (var scenario in feature.Scenarios)
            {
                yml.Append("      - Name: " + scenario.DisplayName.SanitiseForYml() + "\n");
                yml.Append("        StableId: " + ScenarioStableId.Compute(feature.DisplayName, scenario.DisplayName, scenario.OutlineId) + "\n");
                yml.Append("        Result: " + scenario.Result + "\n");
                yml.Append("        DurationSeconds: " + (scenario.Duration?.TotalSeconds ?? 0.0).ToString("F3") + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.ErrorMessage is not null)
                    yml.Append("        ErrorMessage: " + scenario.ErrorMessage.SanitiseForYml() + "\n");

                if (scenario.ErrorStackTrace is not null)
                    yml.Append("        ErrorStackTrace: " + scenario.ErrorStackTrace.SanitiseForYml() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        yml.Append("          - " + label.SanitiseForYml() + "\n");
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        yml.Append("          - " + cat.SanitiseForYml() + "\n");
                }

                if (scenario.Rule is not null)
                    yml.Append("        Rule: " + scenario.Rule.SanitiseForYml() + "\n");

                if (scenario.BackgroundSteps is { Length: > 0 })
                {
                    yml.Append("        BackgroundSteps:\n");
                    foreach (var step in scenario.BackgroundSteps)
                        AppendTestRunYamlStep(yml, step, "          ");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    yml.Append("        Steps:\n");
                    foreach (var step in scenario.Steps)
                        AppendTestRunYamlStep(yml, step, "          ");
                }

                if (scenario.Attachments is { Length: > 0 })
                {
                    yml.Append("        Attachments:\n");
                    foreach (var att in scenario.Attachments)
                    {
                        yml.Append("          - Name: " + att.Name.SanitiseForYml() + "\n");
                        yml.Append("            RelativePath: " + att.RelativePath.SanitiseForYml() + "\n");
                    }
                }

                if (diagramLookup != null)
                {
                    var diags = diagramLookup[scenario.Id].ToArray();
                    if (diags.Length > 0)
                    {
                        yml.Append("        Diagrams:\n");
                        foreach (var diag in diags)
                            yml.Append("          - |\n" + string.Join("\n", diag.Split('\n').Select(line => "            " + line)) + "\n");
                    }
                }

                if (logLookup != null)
                {
                    var logs = logLookup[scenario.Id].ToArray();
                    if (logs.Length > 0)
                    {
                        yml.Append("        HttpInteractions:\n");
                        foreach (var log in logs)
                            AppendTestRunYamlLog(yml, log, "          ");
                    }
                }
            }
        }

        return yml.ToString();
    }

    private static void AppendTestRunYamlStep(StringBuilder yml, ScenarioStep step, string indent)
    {
        yml.Append(indent + "- Keyword: " + (step.Keyword ?? "").SanitiseForYml() + "\n");
        yml.Append(indent + "  Text: " + step.Text.SanitiseForYml() + "\n");
        yml.Append(indent + "  Status: " + (step.Status?.ToString() ?? "") + "\n");
        if (step.Duration != null)
            yml.Append(indent + "  DurationSeconds: " + step.Duration.Value.TotalSeconds.ToString("F3") + "\n");

        if (step.SubSteps is { Length: > 0 })
        {
            yml.Append(indent + "  SubSteps:\n");
            foreach (var sub in step.SubSteps)
                AppendTestRunYamlStep(yml, sub, indent + "    ");
        }

        if (step.Attachments is { Length: > 0 })
        {
            yml.Append(indent + "  Attachments:\n");
            foreach (var att in step.Attachments)
            {
                yml.Append(indent + "    - Name: " + att.Name.SanitiseForYml() + "\n");
                yml.Append(indent + "      RelativePath: " + att.RelativePath.SanitiseForYml() + "\n");
            }
        }
    }

    private static void AppendTestRunYamlLog(StringBuilder yml, RequestResponseLog log, string indent)
    {
        yml.Append(indent + "- Type: " + log.Type + "\n");
        yml.Append(indent + "  Method: " + (log.Method.Value?.ToString()?.ToUpperInvariant() ?? "") + "\n");
        yml.Append(indent + "  Uri: " + log.Uri + "\n");
        yml.Append(indent + "  ServiceName: " + log.ServiceName.SanitiseForYml() + "\n");
        yml.Append(indent + "  CallerName: " + log.CallerName.SanitiseForYml() + "\n");
        if (log.Content is not null)
            yml.Append(indent + "  Content: " + log.Content.SanitiseForYml() + "\n");
        if (log.StatusCode is not null)
            yml.Append(indent + "  StatusCode: " + log.StatusCode.Value + "\n");
        yml.Append(indent + "  TraceId: " + log.TraceId + "\n");
        yml.Append(indent + "  RequestResponseId: " + log.RequestResponseId + "\n");
        if (log.Timestamp is not null)
            yml.Append(indent + "  Timestamp: " + log.Timestamp.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\n");
        if (log.Headers.Length > 0)
        {
            yml.Append(indent + "  Headers:\n");
            foreach (var h in log.Headers)
                yml.Append(indent + "    - Key: " + h.Key.SanitiseForYml() + "\n" + indent + "      Value: " + (h.Value ?? "").SanitiseForYml() + "\n");
        }
    }

    public static string GenerateSpecificationsData(Feature[] features, string fileName, string title, DataFormat format, bool generateBlankOnFailedTests = false)
    {
        if (generateBlankOnFailedTests && features.Any(x => x.Scenarios.Any(y => y.Result == ExecutionResult.Failed)))
            return WriteFile(string.Empty, fileName);

        return format switch
        {
            DataFormat.Yaml => WriteFile(GenerateSpecificationsYaml(features, title), fileName),
            DataFormat.Json => WriteFile(GenerateSpecificationsJson(features, title), fileName),
            DataFormat.Xml => WriteFile(GenerateSpecificationsXml(features, title), fileName),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateSpecificationsYaml(Feature[] features, string title)
    {
        var yml = new StringBuilder();
        yml.Append("Title: " + title + "\n");
        yml.Append("Features:\n");

        foreach (var feature in features.OrderBy(x => x.DisplayName))
        {
            yml.Append("  - Feature: " + feature.DisplayName.SanitiseForYml() + "\n");

            if (feature.Endpoint is not null)
                yml.Append("    Endpoint: " + feature.Endpoint + "\n");

            if (feature.Description is not null)
                yml.Append("    Description: " + feature.Description.SanitiseForYml() + "\n");

            if (feature.Labels is { Length: > 0 })
            {
                yml.Append("    Labels:\n");
                foreach (var label in feature.Labels)
                    yml.Append("      - " + label.SanitiseForYml() + "\n");
            }

            yml.Append("    Scenarios:\n");

            var orderedScenarios = feature.Scenarios.OrderByDescending(x => x.IsHappyPath).ThenBy(x => x.DisplayName);
            foreach (var scenario in orderedScenarios)
            {
                yml.Append("      - Scenario: " + scenario.DisplayName.SanitiseForYml() + "\n");
                yml.Append("        IsHappyPath: " + scenario.IsHappyPath.ToString().ToLower() + "\n");

                if (scenario.Labels is { Length: > 0 })
                {
                    yml.Append("        Labels:\n");
                    foreach (var label in scenario.Labels)
                        yml.Append("          - " + label.SanitiseForYml() + "\n");
                }

                if (scenario.Categories is { Length: > 0 })
                {
                    yml.Append("        Categories:\n");
                    foreach (var cat in scenario.Categories)
                        yml.Append("          - " + cat.SanitiseForYml() + "\n");
                }

                if (scenario.Steps is { Length: > 0 })
                {
                    yml.Append("        Steps:\n");
                    foreach (var step in scenario.Steps)
                        AppendYamlStep(yml, step, "          ");
                }

                yml.Append("\n");
            }
        }

        return yml.ToString();
    }

    private static string GenerateSpecificationsJson(Feature[] features, string title)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        var data = new
        {
            Title = title,
            Features = features.OrderBy(f => f.DisplayName).Select(f => new
            {
                Name = f.DisplayName,
                f.Endpoint,
                f.Description,
                Labels = f.Labels ?? [],
                Scenarios = f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(s => s.DisplayName).Select(s => new
                {
                    Name = s.DisplayName,
                    s.IsHappyPath,
                    Labels = s.Labels ?? [],
                    Categories = s.Categories ?? [],
                    Steps = (s.Steps ?? []).Select(MapSpecStepJson).ToArray()
                }).ToArray()
            }).ToArray()
        };
        return JsonSerializer.Serialize(data, options);
    }

    private static string MapSpecStepJson(ScenarioStep step)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;
        // Specifications steps are text-only (matching YAML format)
        // SubSteps are not included as separate entries per the YAML spec format
        return text;
    }

    private static string GenerateSpecificationsXml(Feature[] features, string title)
    {
        var doc = new XDocument(
            new XElement("Specifications",
                new XElement("Title", title),
                new XElement("Features",
                    features.OrderBy(f => f.DisplayName).Select(f =>
                        new XElement("Feature",
                            new XElement("Name", f.DisplayName),
                            f.Endpoint != null ? new XElement("Endpoint", f.Endpoint) : null,
                            f.Description != null ? new XElement("Description", f.Description) : null,
                            (f.Labels is { Length: > 0 }) ? new XElement("Labels", f.Labels.Select(l => new XElement("Label", l))) : null,
                            new XElement("Scenarios",
                                f.Scenarios.OrderByDescending(s => s.IsHappyPath).ThenBy(s => s.DisplayName).Select(s =>
                                    new XElement("Scenario",
                                        new XElement("Name", s.DisplayName),
                                        new XElement("IsHappyPath", s.IsHappyPath.ToString().ToLower()),
                                        (s.Labels is { Length: > 0 }) ? new XElement("Labels", s.Labels.Select(l => new XElement("Label", l))) : null,
                                        (s.Categories is { Length: > 0 }) ? new XElement("Categories", s.Categories.Select(c => new XElement("Category", c))) : null,
                                        (s.Steps is { Length: > 0 }) ? new XElement("Steps", s.Steps.Select(MapSpecStepXml)) : null
                                    )
                                )
                            )
                        )
                    )
                )
            )
        );
        return doc.ToString();
    }

    private static XElement MapSpecStepXml(ScenarioStep step)
    {
        var text = step.Keyword is not null ? $"{step.Keyword} {step.Text}" : step.Text;
        var element = new XElement("Step", text);
        if (step.SubSteps is { Length: > 0 })
        {
            foreach (var sub in step.SubSteps)
                element.Add(MapSpecStepXml(sub));
        }
        return element;
    }

    /// <summary>
    /// Copies attachment files referenced by steps into the reports directory and rewrites
    /// their <see cref="FileAttachment.RelativePath"/> to point to the local copy.
    /// Attachments whose source file does not exist or whose path is already relative
    /// to an <c>attachments/</c> subfolder are left unchanged.
    /// </summary>
    public static void CopyAttachmentsToReportsFolder(Feature[] features, string reportsDirectory)
    {
        var attachmentsDir = Path.Combine(reportsDirectory, "attachments");
        var copiedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in features)
        {
            if (feature.Scenarios is null) continue;
            foreach (var scenario in feature.Scenarios)
            {
                if (scenario.Attachments is { Length: > 0 })
                    scenario.Attachments = ProcessAttachments(scenario.Attachments, attachmentsDir, copiedFiles, usedNames);
                if (scenario.BackgroundSteps is { Length: > 0 })
                    ProcessSteps(scenario.BackgroundSteps, attachmentsDir, copiedFiles, usedNames);
                if (scenario.Steps is { Length: > 0 })
                    ProcessSteps(scenario.Steps, attachmentsDir, copiedFiles, usedNames);
            }
        }
    }

    private static void ProcessSteps(ScenarioStep[] steps, string attachmentsDir,
        Dictionary<string, string> copiedFiles, HashSet<string> usedNames)
    {
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            if (step.Attachments is { Length: > 0 })
                step.Attachments = ProcessAttachments(step.Attachments, attachmentsDir, copiedFiles, usedNames);
            if (step.SubSteps is { Length: > 0 })
                ProcessSteps(step.SubSteps, attachmentsDir, copiedFiles, usedNames);
        }
    }

    private static FileAttachment[] ProcessAttachments(FileAttachment[] attachments, string attachmentsDir,
        Dictionary<string, string> copiedFiles, HashSet<string> usedNames)
    {
        var result = new FileAttachment[attachments.Length];
        for (var i = 0; i < attachments.Length; i++)
        {
            var att = attachments[i];
            var sourcePath = att.RelativePath;

            // Skip paths already pointing to attachments/ subfolder
            if (sourcePath.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase) ||
                sourcePath.StartsWith("attachments\\", StringComparison.OrdinalIgnoreCase))
            {
                result[i] = att;
                continue;
            }

            // Resolve to absolute if relative
            var fullPath = Path.IsPathRooted(sourcePath)
                ? sourcePath
                : Path.GetFullPath(sourcePath, AppDomain.CurrentDomain.BaseDirectory);

            if (!File.Exists(fullPath))
            {
                result[i] = att;
                continue;
            }

            // Check if we've already copied this exact source file
            var normalizedSource = Path.GetFullPath(fullPath);
            if (copiedFiles.TryGetValue(normalizedSource, out var existingRelative))
            {
                result[i] = att with { RelativePath = existingRelative };
                continue;
            }

            // Deduplicate the destination file name
            var fileName = Path.GetFileName(fullPath);
            var destName = GetUniqueFileName(fileName, usedNames);
            usedNames.Add(destName);

            Directory.CreateDirectory(attachmentsDir);
            var destPath = Path.Combine(attachmentsDir, destName);
            File.Copy(fullPath, destPath, overwrite: true);

            var relativePath = $"attachments/{destName}";
            copiedFiles[normalizedSource] = relativePath;
            result[i] = att with { RelativePath = relativePath };
        }

        return result;
    }

    private static string GetUniqueFileName(string fileName, HashSet<string> usedNames)
    {
        if (!usedNames.Contains(fileName))
            return fileName;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{nameWithoutExt}_{counter}{ext}";
            counter++;
        } while (usedNames.Contains(candidate));

        return candidate;
    }

    private static string WriteFile(string text, string fileName)
    {
        var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);
        try
        {
            File.WriteAllText(filePath, text);
        }
        catch (IOException)
        {
            var fallback = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(fileName) + "2" + Path.GetExtension(fileName));
            File.WriteAllText(fallback, text);
            return fallback;
        }
        return filePath;
    }

    internal static HashSet<string> ExtractDependencies(string codeBehind, DiagramFormat format)
    {
        var deps = new HashSet<string>();
        if (string.IsNullOrEmpty(codeBehind)) return deps;

        foreach (var line in codeBehind.Split('\n'))
        {
            var trimmed = line.Trim();

            // Match all PlantUML participant types: actor, boundary, control, entity, database, collections, queue, participant
            var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                @"^(?:actor|boundary|control|entity|database|collections|queue|participant)\s+""([^""]+)""\s+as\s+");
            if (match.Success)
                deps.Add(match.Groups[1].Value);
        }

        return deps;
    }

    private static readonly System.Text.RegularExpressions.Regex ParticipantRegex = new(
        @"^(?:actor|boundary|control|entity|database|collections|queue|participant)\s+""([^""]+)""",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex UrlRegex = new(
        @":\s*(?:GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS|CONNECT|TRACE):\s*(\S+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static HashSet<string> ExtractDiagramSearchTerms(string codeBehind)
    {
        var terms = new HashSet<string>();
        if (string.IsNullOrEmpty(codeBehind)) return terms;

        foreach (var line in codeBehind.Split('\n'))
        {
            var trimmed = line.Trim();

            var participantMatch = ParticipantRegex.Match(trimmed);
            if (participantMatch.Success)
                terms.Add(participantMatch.Groups[1].Value);

            var urlMatch = UrlRegex.Match(trimmed);
            if (urlMatch.Success)
                terms.Add(urlMatch.Groups[1].Value);
        }

        return terms;
    }

    private static void CollectStepText(ScenarioStep[]? steps, List<string> parts)
    {
        if (steps is null) return;
        foreach (var step in steps)
        {
            parts.Add(step.Text);
            CollectStepText(step.SubSteps, parts);
        }
    }

    private static string GetDataFormatExtension(DataFormat format) => format switch
    {
        DataFormat.Json => "json",
        DataFormat.Xml => "xml",
        DataFormat.Yaml => "yml",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string GetSchemaExtension(DataFormat format) => format switch
    {
        DataFormat.Json => "json",
        DataFormat.Xml => "xsd",
        DataFormat.Yaml => "json", // YAML uses JSON Schema
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string GenerateTestRunReportSchema(string fileName, DataFormat format)
    {
        return format switch
        {
            DataFormat.Json => WriteFile(GenerateTestRunReportJsonSchema(), fileName),
            DataFormat.Xml => WriteFile(GenerateTestRunReportXmlSchema(), fileName),
            DataFormat.Yaml => WriteFile(GenerateTestRunReportJsonSchema(), fileName), // YAML uses JSON Schema
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static string GenerateTestRunReportJsonSchema()
    {
        var resultEnumValues = Enum.GetNames(typeof(ExecutionResult));
        var statusEnumValues = resultEnumValues;

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        var schema = new Dictionary<string, object?>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = "TestRunReport",
            ["description"] = "Schema for Kronikol test run report data",
            ["type"] = "object",
            ["required"] = new[] { "startTime", "endTime", "features" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["kronikolVersion"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Version of Kronikol that generated this report" },
                ["startTime"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = "UTC start time of the test run" },
                ["endTime"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["description"] = "UTC end time of the test run" },
                ["features"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["required"] = new[] { "name", "labels", "scenarios" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["endpoint"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                            ["description"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                            ["labels"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                            ["scenarios"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["items"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "object",
                                    ["required"] = new[] { "id", "stableId", "name", "result", "durationSeconds", "isHappyPath", "labels", "categories", "steps" },
                                    ["properties"] = new Dictionary<string, object?>
                                    {
                                        ["id"] = new Dictionary<string, object?> { ["type"] = "string" },
                                        ["stableId"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "Deterministic cross-run identifier derived from feature name + scenario display name (+ outline ID for parameterized scenarios). Use this for matching the same test across runs." },
                                        ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                                        ["result"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = resultEnumValues },
                                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = "number" },
                                        ["isHappyPath"] = new Dictionary<string, object?> { ["type"] = "boolean" },
                                        ["errorMessage"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                                        ["errorStackTrace"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                                        ["labels"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["categories"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["rule"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Gherkin Rule grouping this scenario belongs to" },
                                        ["outlineId"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true, ["description"] = "Original scenario outline name for parameterized scenarios" },
                                        ["exampleValues"] = new Dictionary<string, object?> { ["type"] = "object", ["nullable"] = true, ["description"] = "Example parameter values for parameterized scenarios", ["additionalProperties"] = new Dictionary<string, object?> { ["type"] = "string" } },
                                        ["backgroundSteps"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                                        },
                                        ["steps"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                                        },
                                        ["attachments"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["description"] = "Scenario-level file attachments (added when no step was active)",
                                            ["items"] = new Dictionary<string, object?>
                                            {
                                                ["type"] = "object",
                                                ["properties"] = new Dictionary<string, object?>
                                                {
                                                    ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                                                    ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string" }
                                                }
                                            }
                                        },
                                        ["diagrams"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                                        },
                                        ["httpInteractions"] = new Dictionary<string, object?>
                                        {
                                            ["type"] = "array",
                                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/httpInteraction" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            ["$defs"] = new Dictionary<string, object?>
            {
                ["step"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["keyword"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["text"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = statusEnumValues, ["nullable"] = true },
                        ["durationSeconds"] = new Dictionary<string, object?> { ["type"] = "number", ["nullable"] = true },
                        ["subSteps"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?> { ["$ref"] = "#/$defs/step" }
                        },
                        ["attachments"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                                    ["relativePath"] = new Dictionary<string, object?> { ["type"] = "string" }
                                }
                            }
                        }
                    }
                },
                ["httpInteraction"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "Request", "Response" } },
                        ["method"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["uri"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uri" },
                        ["serviceName"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["callerName"] = new Dictionary<string, object?> { ["type"] = "string" },
                        ["content"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["headers"] = new Dictionary<string, object?>
                        {
                            ["type"] = "array",
                            ["items"] = new Dictionary<string, object?>
                            {
                                ["type"] = "object",
                                ["properties"] = new Dictionary<string, object?>
                                {
                                    ["key"] = new Dictionary<string, object?> { ["type"] = "string" },
                                    ["value"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true }
                                }
                            }
                        },
                        ["statusCode"] = new Dictionary<string, object?> { ["type"] = "string", ["nullable"] = true },
                        ["traceId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
                        ["requestResponseId"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "uuid" },
                        ["timestamp"] = new Dictionary<string, object?> { ["type"] = "string", ["format"] = "date-time", ["nullable"] = true }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(schema, options);
    }

    private static string GenerateTestRunReportXmlSchema()
    {
        var xs = XNamespace.Get("http://www.w3.org/2001/XMLSchema");

        var resultEnumValues = Enum.GetNames(typeof(ExecutionResult));

        var executionResultType = new XElement(xs + "simpleType",
            new XAttribute("name", "ExecutionResult"),
            new XElement(xs + "restriction",
                new XAttribute("base", "xs:string"),
                resultEnumValues.Select(v => new XElement(xs + "enumeration", new XAttribute("value", v)))
            ));

        var headerType = new XElement(xs + "complexType",
            new XAttribute("name", "HeaderType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Key"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Value"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
            ));

        var stepType = new XElement(xs + "complexType",
            new XAttribute("name", "StepType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Keyword"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Text"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Status"), new XAttribute("type", "ExecutionResult"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "DurationSeconds"), new XAttribute("type", "xs:decimal"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "SubSteps"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Step"), new XAttribute("type", "StepType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Attachments"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Attachment"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                                        new XElement(xs + "element", new XAttribute("name", "RelativePath"), new XAttribute("type", "xs:string"))
                                    )
                                )
                            )
                        )
                    )
                )
            ));

        var httpInteractionType = new XElement(xs + "complexType",
            new XAttribute("name", "HttpInteractionType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Type"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Method"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Uri"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "ServiceName"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "CallerName"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Content"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Headers"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Header"), new XAttribute("type", "HeaderType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "StatusCode"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "TraceId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "RequestResponseId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Timestamp"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"))
            ));

        var scenarioType = new XElement(xs + "complexType",
            new XAttribute("name", "ScenarioType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Id"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "StableId"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Result"), new XAttribute("type", "ExecutionResult")),
                new XElement(xs + "element", new XAttribute("name", "DurationSeconds"), new XAttribute("type", "xs:decimal")),
                new XElement(xs + "element", new XAttribute("name", "IsHappyPath"), new XAttribute("type", "xs:boolean")),
                new XElement(xs + "element", new XAttribute("name", "ErrorMessage"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "ErrorStackTrace"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Labels"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Label"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Categories"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Category"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Rule"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "BackgroundSteps"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Step"), new XAttribute("type", "StepType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Steps"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Step"), new XAttribute("type", "StepType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Attachments"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Attachment"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                                        new XElement(xs + "element", new XAttribute("name", "RelativePath"), new XAttribute("type", "xs:string"))
                                    )
                                )
                            )
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Diagrams"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Diagram"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "HttpInteractions"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "HttpInteraction"), new XAttribute("type", "HttpInteractionType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                )
            ));

        var featureType = new XElement(xs + "complexType",
            new XAttribute("name", "FeatureType"),
            new XElement(xs + "sequence",
                new XElement(xs + "element", new XAttribute("name", "Name"), new XAttribute("type", "xs:string")),
                new XElement(xs + "element", new XAttribute("name", "Endpoint"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Description"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                new XElement(xs + "element", new XAttribute("name", "Labels"), new XAttribute("minOccurs", "0"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Label"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                ),
                new XElement(xs + "element", new XAttribute("name", "Scenarios"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "Scenario"), new XAttribute("type", "ScenarioType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                        )
                    )
                )
            ));

        var doc = new XDocument(
            new XElement(xs + "schema",
                new XAttribute(XNamespace.Xmlns + "xs", "http://www.w3.org/2001/XMLSchema"),
                executionResultType,
                headerType,
                stepType,
                httpInteractionType,
                scenarioType,
                featureType,
                new XElement(xs + "element",
                    new XAttribute("name", "TestRunReport"),
                    new XElement(xs + "complexType",
                        new XElement(xs + "sequence",
                            new XElement(xs + "element", new XAttribute("name", "KronikolVersion"), new XAttribute("type", "xs:string"), new XAttribute("minOccurs", "0")),
                            new XElement(xs + "element", new XAttribute("name", "StartTime"), new XAttribute("type", "xs:string")),
                            new XElement(xs + "element", new XAttribute("name", "EndTime"), new XAttribute("type", "xs:string")),
                            new XElement(xs + "element", new XAttribute("name", "Features"),
                                new XElement(xs + "complexType",
                                    new XElement(xs + "sequence",
                                        new XElement(xs + "element", new XAttribute("name", "Feature"), new XAttribute("type", "FeatureType"), new XAttribute("minOccurs", "0"), new XAttribute("maxOccurs", "unbounded"))
                                    )
                                )
                            )
                        )
                    )
                )
            ));

        return doc.Declaration != null ? doc.Declaration + "\n" + doc : doc.ToString();
    }
}