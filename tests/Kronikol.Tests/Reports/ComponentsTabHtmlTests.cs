using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Kronikol.ComponentDiagram;
using Kronikol.InternalFlow;
using Kronikol.Reports;
using Kronikol.Reports.Merge;
using Kronikol.Tracking;

namespace Kronikol.Tests.Reports;

/// <summary>
/// The Components tab as the generator writes it (5.0.0, plans/OPTION_C_PLAN.md S11, D26, D28): headed by the diagram's
/// title, which its drawing leaves out (ComponentDiagram.html keeps it); where the page draws the diagram, the tab's
/// controls at the right of the heading's line (Show where the report knows which scenarios made each connection's calls,
/// Direction, Call summaries), the note Filtered fills, and the model report-components-script.js draws each form from:
/// the line of the direction, of each component and of each connection, each connection's two components and its line
/// without the label, and its scenarios by stable id, computed from the run's calls. Under NodeJs, Server and Local the
/// report's renderer draws the diagram and the tab has no controls; a caller that hands in a source alone gets the heading
/// and its source as given; a run whose calls give the diagram no connection has no tab. The tab's card is the timeline's.
/// </summary>
[Collection("DiagramsFetcher")]
public class ComponentsTabHtmlTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kronikol-components-tab").FullName;
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public ComponentsTabHtmlTests() => DefaultDiagramsFetcher.Reset();

    public void Dispose()
    {
        DefaultDiagramsFetcher.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // One word each, so a name is drawn on one line; tagged, since the process's call log holds every test's calls.
    private string Shop => "Shop" + _tag;
    private string Payments => "Payments" + _tag;
    private string Orders => "Orders" + _tag;
    private string Stock => "Stock" + _tag;

    /// <summary>
    /// A run through the whole pipeline: Pays (passed) calls Payments from the shop; Ships (failed) calls Orders, which
    /// calls Stock; Browses (skipped) makes no call. The diagram is held to the four by a ParticipantFilter. Returns the
    /// run report's HTML and ComponentDiagram.html's.
    /// </summary>
    private (string Report, string Standalone) Run(Action<ReportConfigurationOptions>? configure = null)
    {
        var (pays, ships, browses) = ("ct-pays-" + _tag, "ct-ships-" + _tag, "ct-browses-" + _tag);
        RequestResponseLogger.LogPair("Pays", pays, HttpMethod.Post, new Uri("http://payments/pay"), Payments, Shop, statusCode: HttpStatusCode.OK);
        RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Post, new Uri("http://orders/orders"), Orders, Shop, statusCode: HttpStatusCode.Created);
        RequestResponseLogger.LogPair("Ships", ships, HttpMethod.Get, new Uri("http://stock/stock"), Stock, Orders, statusCode: HttpStatusCode.OK);
        var names = new HashSet<string> { Shop, Payments, Orders, Stock };
        var options = new ReportConfigurationOptions
        {
            ReportsFolderPath = _dir,
            GenerateSpecificationsReport = false,
            GenerateSpecificationsData = false,
            ComponentDiagramOptions = new ComponentDiagramOptions { ParticipantFilter = names.Contains },
        };
        configure?.Invoke(options);
        ReportGenerator.CreateStandardReportsWithDiagrams(
        [
            new Feature
            {
                DisplayName = "Shop " + _tag,
                Scenarios =
                [
                    new Scenario { Id = pays, DisplayName = "Pays", Result = ExecutionResult.Passed },
                    new Scenario { Id = ships, DisplayName = "Ships", Result = ExecutionResult.Failed },
                    new Scenario { Id = browses, DisplayName = "Browses", Result = ExecutionResult.Skipped }
                ]
            }
        ], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow, options);
        return (ReportHtml.Read(Path.Combine(_dir, "TestRunReport.html")), ReportHtml.Read(Path.Combine(_dir, "ComponentDiagram.html")));
    }

    private static IDocument Parse(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(req => req.Content(html)).GetAwaiter().GetResult();

    /// <summary>The source the tab draws from: the diagram element's entry in puml-data.</summary>
    private static string EmbeddedSource(IDocument page)
    {
        var id = page.QuerySelector("#component-diagram .plantuml-browser")!.Id!;
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(page.GetElementById("puml-data")!.TextContent)!;
        return InternalFlowHtmlGenerator.DecompressFromBase64(map[id]);
    }

    /// <summary>ComponentDiagram.html's source, which the page carries gzipped (BrowserJs).</summary>
    private static string StandaloneSource(string html) =>
        InternalFlowHtmlGenerator.DecompressFromBase64(Parse(html).QuerySelector("#comp-diagram")!.GetAttribute("data-plantuml-z")!);

    /// <summary>The source without its title line and the blank line after it.</summary>
    private static string WithoutTitle(string source) => Regex.Replace(source, @"^title [^\r\n]*\r?\n\r?\n", "", RegexOptions.Multiline);

    private static JsonElement Model(IDocument page) =>
        JsonDocument.Parse(page.GetElementById("component-diagram-model")!.TextContent).RootElement;

    /// <summary>A group's buttons as "value pressed text", the value read from <paramref name="key"/>.</summary>
    private static string[] Buttons(IElement group, string key) =>
        group.QuerySelectorAll(":scope > button.tl-seg-btn")
            .Select(b => $"{b.GetAttribute("data-" + key)} {b.GetAttribute("aria-pressed")} {b.TextContent}").ToArray();

    /// <summary>The name a component's statement draws in bold.</summary>
    private static string Declared(string statement) => Regex.Match(statement, @"\*\*(\w+)\*\*").Groups[1].Value;

    /// <summary>The scenario elements' stable ids, by the name each one's heading carries.</summary>
    private static string StableIdOf(IDocument page, string name) =>
        page.QuerySelectorAll("details.scenario[data-stable-id]")
            .Single(s => s.QuerySelector(":scope > summary .copy-scenario-name")!.GetAttribute("data-scenario-name") == name)
            .GetAttribute("data-stable-id")!;

    private static RequestResponseLog Request(string testId, string service, string caller, RequestResponseType type = RequestResponseType.Request) =>
        new(testId, testId, HttpMethod.Post, null, new Uri("http://" + service.ToLowerInvariant() + "/"), [], service, caller, type,
            Guid.NewGuid(), Guid.NewGuid(), false);

    [Fact]
    public void The_tab_is_headed_by_the_diagrams_title_which_its_drawing_leaves_out()
    {
        var (report, standalone) = Run();
        var page = Parse(report);

        var header = page.QuerySelector("#component-diagram > .timeline-header.component-header")!;
        Assert.Equal("Component Diagram ", header.ChildNodes[0].TextContent);
        Assert.Equal("\U0001F6C8", header.QuerySelector(":scope > .timeline-info")!.TextContent);
        // the source the tab draws is ComponentDiagram.html's, but for its title
        var embedded = EmbeddedSource(page);
        Assert.DoesNotMatch(new Regex("^title ", RegexOptions.Multiline), embedded);
        var standaloneSource = StandaloneSource(standalone);
        Assert.Matches(new Regex("^title Component Diagram\r?$", RegexOptions.Multiline), standaloneSource);
        Assert.Equal(WithoutTitle(standaloneSource), embedded);
    }

    [Fact]
    public void A_configured_title_is_the_tabs_heading()
    {
        var (report, standalone) = Run(o => o.ComponentDiagramOptions!.Title = "Shop <architecture>");
        var page = Parse(report);

        Assert.Equal("Shop <architecture> ", page.QuerySelector("#component-diagram > .timeline-header")!.ChildNodes[0].TextContent);
        Assert.Contains("Shop &lt;architecture&gt; <span class=\"timeline-info\"", report);
        Assert.DoesNotMatch(new Regex("^title ", RegexOptions.Multiline), EmbeddedSource(page));
        Assert.Matches(new Regex("^title Shop <architecture>\r?$", RegexOptions.Multiline), StandaloneSource(standalone));
    }

    [Fact]
    public void Where_the_page_draws_the_diagram_the_tab_has_its_three_controls_and_the_note()
    {
        var (report, _) = Run();
        var page = Parse(report);
        var section = page.QuerySelector("#component-diagram")!;
        var header = section.QuerySelector(":scope > .timeline-header")!;

        var groups = header.QuerySelectorAll(":scope > .timeline-controls > .tl-seg").ToArray();
        Assert.Equal(["Show", "Direction", "Call summaries"], groups.Select(g => g.GetAttribute("aria-label")));
        Assert.Equal(["Show", "Direction", "Call summaries"], groups.Select(g => g.QuerySelector(":scope > .tl-seg-label")!.TextContent));
        Assert.Equal(["filtered true Filtered", "full false Full"], Buttons(groups[0], "view"));
        Assert.Equal(["lr true Left to right", "tb false Top to bottom"], Buttons(groups[1], "dir"));
        Assert.Equal(["shown true Shown", "hidden false Hidden"], Buttons(groups[2], "calls"));
        Assert.All(header.QuerySelectorAll(".tl-seg-btn"), b =>
        {
            Assert.Equal("button", b.GetAttribute("type"));
            Assert.False(string.IsNullOrWhiteSpace(b.GetAttribute("title")), $"{b.TextContent} has no title");
        });
        Assert.Contains("Filtered draws only what the scenarios the filters show used; Full draws the whole run.",
            header.QuerySelector(":scope > .timeline-info")!.GetAttribute("title"));

        // under the header line the note, empty and hidden until Filtered leaves something out; then the diagram
        var note = section.QuerySelector(":scope > .timeline-header + .filtered-note")!;
        Assert.True(note.HasAttribute("hidden"));
        FilteredNoteMarkup.AssertEmptyWithClearAll(note);
        Assert.NotNull(section.QuerySelector(":scope > .filtered-note + .plantuml-browser[data-diagram-type='plantuml']"));

        // the model beside the section, and the script that reads it, after the timeline's in the head
        Assert.NotNull(page.QuerySelector("#component-diagram + script#component-diagram-model[type='application/json']"));
        var at = report.IndexOf("<script id=\"kronikol-components\">", StringComparison.Ordinal);
        Assert.True(at > 0, "the page has no components script");
        // in the head as a parser reads it: the export script's text carries a "</head>" of the page it writes
        Assert.NotNull(page.Head!.QuerySelector("script#kronikol-components"));
        var script = report[at..report.IndexOf("</script>", at, StringComparison.Ordinal)];
        Assert.Contains("'component-diagram-model'", script);
        Assert.Contains("'kron-filter-change'", script);
    }

    [Fact]
    public void The_model_says_where_each_part_of_the_source_is_written()
    {
        var page = Parse(Run().Report);
        var lines = EmbeddedSource(page).Split('\n');
        var model = Model(page);

        Assert.Equal("left to right direction", lines[model.GetProperty("direction").GetInt32()].TrimEnd('\r'));
        var components = model.GetProperty("components").EnumerateArray().Select(e => lines[e.GetInt32()].TrimEnd('\r')).ToArray();
        var names = components.Select(Declared).ToArray();
        Assert.Equal(new[] { Shop, Payments, Orders, Stock }.Order(), names.Order());
        var aliases = components.Select(c => Regex.Match(c, @" as (\w+)").Groups[1].Value).ToArray();
        Assert.All(aliases, a => Assert.False(string.IsNullOrEmpty(a)));

        var pairs = new List<(string, string)>();
        foreach (var connection in model.GetProperty("connections").EnumerateArray())
        {
            var line = lines[connection.GetProperty("line").GetInt32()].TrimEnd('\r');
            var (from, to) = (connection.GetProperty("from").GetInt32(), connection.GetProperty("to").GetInt32());
            var bare = connection.GetProperty("bare").GetString()!;
            // the connection's line is its line without the label, then the label
            Assert.Matches($@"^{aliases[from]} -\[#[0-9A-Fa-f]{{6}}\]-> {aliases[to]}$", bare);
            Assert.StartsWith(bare + " : \"", line);
            Assert.EndsWith(" calls across 1 tests\"", line);
            pairs.Add((names[from], names[to]));
        }
        Assert.Equal(new[] { (Shop, Payments), (Shop, Orders), (Orders, Stock) }.Order(), pairs.Order());
    }

    [Fact]
    public void Each_connection_names_the_scenarios_whose_calls_it_draws()
    {
        var page = Parse(Run().Report);
        var lines = EmbeddedSource(page).Split('\n');
        var model = Model(page);
        var names = model.GetProperty("components").EnumerateArray().Select(e => Declared(lines[e.GetInt32()])).ToArray();

        // the scenarios that made a call the diagram draws, by the stable ids their elements carry, in the page's order
        var stableIds = model.GetProperty("scenarios").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal([StableIdOf(page, "Pays"), StableIdOf(page, "Ships")], stableIds);
        var made = model.GetProperty("connections").EnumerateArray().ToDictionary(
            c => (names[c.GetProperty("from").GetInt32()], names[c.GetProperty("to").GetInt32()]),
            c => c.GetProperty("scenarios").EnumerateArray().Select(i => stableIds[i.GetInt32()]).ToArray());
        Assert.Equal([StableIdOf(page, "Pays")], made[(Shop, Payments)]);
        Assert.Equal([StableIdOf(page, "Ships")], made[(Shop, Orders)]);
        Assert.Equal([StableIdOf(page, "Ships")], made[(Orders, Stock)]);
    }

    [Theory]
    [InlineData(PlantUmlRendering.Server)]
    [InlineData(PlantUmlRendering.Local)]
    [InlineData(PlantUmlRendering.NodeJs)]
    public void Under_a_renderer_that_draws_when_the_report_is_written_the_tab_has_no_controls(PlantUmlRendering rendering)
    {
        Assert.SkipWhen(rendering == PlantUmlRendering.NodeJs && !NodeProbe.IsAvailable, "Node.js not available on PATH");

        var (report, _) = Run(o =>
        {
            o.PlantUmlRendering = rendering;
            o.PlantUmlServerBaseUrl = "https://plantuml.example/plantuml";
            o.LocalDiagramRenderer = (_, _) => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
            // Local writes no image file when the SVG is inline (Base64Svg); Svg would need LocalDiagramImageDirectory.
            o.PlantUmlImageFormat = rendering == PlantUmlRendering.Local ? PlantUmlImageFormat.Base64Svg : PlantUmlImageFormat.Svg;
            // no inline SVG, so a Server run fetches nothing
            o.InternalFlowTracking = false;
        });
        var page = Parse(report);

        // the heading still names the diagram; a drawing the renderer made cannot be drawn again in the page
        var header = page.QuerySelector("#component-diagram > .timeline-header")!;
        Assert.Equal("Component Diagram ", header.ChildNodes[0].TextContent);
        Assert.DoesNotContain("Filtered", header.QuerySelector(".timeline-info")!.GetAttribute("title"));
        Assert.Null(header.QuerySelector(".timeline-controls"));
        Assert.Null(page.QuerySelector("#component-diagram .filtered-note"));
        Assert.Null(page.GetElementById("component-diagram-model"));
        Assert.DoesNotContain("<script id=\"kronikol-components\">", report);
    }

    [Fact]
    public void A_caller_that_hands_in_a_source_alone_gets_the_heading_and_its_source_as_given()
    {
        const string source = "@startuml\nleft to right direction\n\ntitle Given\n\nrectangle \"A\" as a\nrectangle \"B\" as b\na --> b : \"HTTP: GET - 1 calls across 1 tests\"\n@enduml";
        var path = ReportGenerator.GenerateHtmlReport([], [new Feature { DisplayName = "F", Scenarios = [new Scenario { Id = "s", DisplayName = "S", Result = ExecutionResult.Passed }] }],
            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(_dir, "SourceAlone.html"), "Test", true,
            plantUmlRendering: PlantUmlRendering.BrowserJs, componentDiagramPlantUml: source);
        var html = ReportHtml.Read(path);
        var page = Parse(html);

        Assert.Equal("Component Diagram ", page.QuerySelector("#component-diagram > .timeline-header")!.ChildNodes[0].TextContent);
        Assert.Null(page.QuerySelector("#component-diagram .timeline-controls"));
        Assert.Null(page.GetElementById("component-diagram-model"));
        Assert.DoesNotContain("<script id=\"kronikol-components\">", html);
        Assert.Equal(source, EmbeddedSource(page));
    }

    [Fact]
    public void A_run_whose_calls_give_the_diagram_no_connection_has_no_components_tab()
    {
        var (report, standalone) = Run(o => o.ComponentDiagramOptions!.ParticipantFilter = _ => false);

        Assert.DoesNotContain("id=\"tab-components\"", report);
        Assert.DoesNotContain("id=\"component-diagram\"", report);
        // ComponentDiagram.html is written as before, title and all
        Assert.Matches(new Regex("^title Component Diagram\r?$", RegexOptions.Multiline), StandaloneSource(standalone));
    }

    [Fact]
    public void A_merged_report_has_Show_only_where_its_shards_carried_their_calls()
    {
        var calls = new[] { Request("m-pays", Payments, Shop), Request("m-ships", Orders, Shop) };
        Feature[] features =
        [
            new()
            {
                DisplayName = "Merged",
                Scenarios =
                [
                    new Scenario { Id = "m-pays", DisplayName = "Pays", Result = ExecutionResult.Passed },
                    new Scenario { Id = "m-ships", DisplayName = "Ships", Result = ExecutionResult.Failed }
                ]
            }
        ];
        IDocument Render(RequestResponseLog[] interactions)
        {
            var written = MergeableReportRenderer.Render(new MergeableReport
            {
                Features = features,
                ComponentRelationships = ComponentDiagramGenerator.ExtractRelationships(calls),
                Interactions = interactions
            }, Path.Combine(_dir, $"Merged_{Guid.NewGuid():N}.html"));
            return Parse(ReportHtml.Read(written));
        }

        var withCalls = Render(calls);
        Assert.Equal(["Show", "Direction", "Call summaries"],
            withCalls.QuerySelectorAll("#component-diagram .timeline-controls > .tl-seg").Select(g => g.GetAttribute("aria-label")));
        Assert.NotNull(withCalls.QuerySelector("#component-diagram > .filtered-note"));
        Assert.Equal(2, Model(withCalls).GetProperty("scenarios").GetArrayLength());

        // a shard written without its calls says nothing of who made which: the tab draws the whole run, and says so by
        // offering no Show
        var without = Render([]);
        Assert.Equal(["Direction", "Call summaries"],
            without.QuerySelectorAll("#component-diagram .timeline-controls > .tl-seg").Select(g => g.GetAttribute("aria-label")));
        Assert.Null(without.QuerySelector("#component-diagram > .filtered-note"));
        Assert.False(Model(without).TryGetProperty("scenarios", out _));
        Assert.DoesNotContain("Filtered", without.QuerySelector("#component-diagram .timeline-info")!.GetAttribute("title"));
    }

    [Fact]
    public void The_tabs_card_is_the_timelines()
    {
        var css = Stylesheets.HtmlReportStyleSheet;

        foreach (var property in new[] { "margin", "border", "border-radius", "padding", "background", "box-shadow" })
            Assert.Equal(CssRules.Value(css, ".scenario-timeline", property), CssRules.Value(css, ".component-diagram-section", property));
        Assert.Equal("var(--kron-card)", CssRules.Value(css, ".component-diagram-section", "background"));
        Assert.Equal("1px solid var(--kron-hairline)", CssRules.Value(css, ".component-diagram-section", "border"));
    }

    [Fact]
    public void The_embedded_form_is_the_diagram_without_its_title_and_GeneratePlantUml_is_unchanged()
    {
        var calls = new[] { Request("g1", "Payments", "Shop"), Request("g2", "Orders", "Shop"), Request("g2", "Stock", "Orders") };
        var relationships = ComponentDiagramGenerator.ExtractRelationships(calls);

        foreach (var c4 in new[] { false, true })
        {
            var standalone = ComponentDiagramGenerator.GeneratePlantUml(relationships, useC4: c4);
            var embedded = ComponentDiagramGenerator.GenerateEmbedded(relationships, useC4: c4, logs: calls);

            Assert.Matches(new Regex("^title Component Diagram\r?$", RegexOptions.Multiline), standalone);
            Assert.Equal(WithoutTitle(standalone), embedded.PlantUml);
            Assert.Equal(EmbeddedComponentDiagram.DefaultHeading, embedded.Heading);
            var lines = embedded.PlantUml.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Assert.Equal("left to right direction", lines[embedded.DirectionLine]);
            Assert.Equal(3, embedded.Connections.Length);
            foreach (var connection in embedded.Connections)
            {
                var line = lines[connection.Line];
                if (c4)
                {
                    // C4's relation holds its label inside: without it, an empty one
                    Assert.Matches("^Rel\\(\\w+, \\w+, \"\"(, \\$tags=\"#[0-9A-Fa-f]{6}\")?\\)$", connection.Bare);
                    Assert.Equal(connection.Bare, Regex.Replace(line, "^(Rel\\(\\w+, \\w+, )\"[^\"]*\"", "$1\"\""));
                }
                else
                    Assert.StartsWith(connection.Bare + " : \"", line);
            }
        }
    }

    [Fact]
    public void TestsOf_reads_the_calls_ExtractRelationships_counts()
    {
        var marker = Request("t6", "Orders", "Shop");
        marker.IsOverrideStart = true;
        RequestResponseLog[] calls =
        [
            Request("t1", "Payments", "Shop"), Request("t2", "Payments", "Shop"), Request("t1", "Payments", "Shop"),
            Request("t3", "Orders", "Shop"),
            // a response is no call of its own, an ignored call is not drawn, a marker is no call at all
            Request("t4", "Orders", "Shop", RequestResponseType.Response),
            Request("t5", "Orders", "Shop") with { TrackingIgnore = true },
            marker,
            // left out by the filter
            Request("t7", "Hidden", "Shop")
        ];
        Func<string, bool> filter = name => name != "Hidden";

        var relationships = ComponentDiagramGenerator.ExtractRelationships(calls, filter);
        var tests = ComponentDiagramGenerator.TestsOf(calls, filter);

        Assert.Equal(relationships.Select(r => (r.Caller, r.Service, r.Protocol)).Order(), tests.Keys.Order());
        foreach (var relationship in relationships)
            Assert.Equal(relationship.TestCount, tests[(relationship.Caller, relationship.Service, relationship.Protocol)].Count);
        var payments = relationships.Single(r => r.Service == "Payments");
        Assert.Equal(["t1", "t2"], tests[(payments.Caller, payments.Service, payments.Protocol)].Order());
        var orders = relationships.Single(r => r.Service == "Orders");
        Assert.Equal(["t3"], tests[(orders.Caller, orders.Service, orders.Protocol)]);
    }
}
