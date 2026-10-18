# S3: the component diagram tests that pinned the 4.x toggle, rewritten for the Components tab (each says why).
BS = chr(92); Q = BS + '"'
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

C = 'tests/Kronikol.Tests/ComponentDiagram/ComponentDiagramReportTests.cs'
sub(C, '''        Assert.Contains("class=''' + Q + '''component-diagram-section''' + Q + '''", content);
        Assert.Contains("Component Diagram", content);''', '''        Assert.Contains("class=''' + Q + '''component-diagram-section''' + Q + '''", content);
        Assert.Contains(">Components</button>", content); // 5.0.0: its tab (plans/OPTION_C_PLAN.md S3)''')

sub(C, '''        Assert.Contains("""id="component-diagram""", content);
        Assert.Contains("""style="display:none""", content);
        // Slice from the section's MARKUP (not the CSS selector the shared script carries,
        // which sits far earlier in the file and would span the whole scenario list).
        var sectionIdx = content.IndexOf("class=''' + Q + '''component-diagram-section''' + Q + '''", StringComparison.Ordinal);
        Assert.True(sectionIdx > 0, "component diagram section markup should be present");
        var reportContentIdx = content.IndexOf("id=''' + Q + '''report-content''' + Q + '''", sectionIdx, StringComparison.Ordinal);
        Assert.True(reportContentIdx > sectionIdx, "report-content div should follow the component diagram section");
        Assert.DoesNotContain("<details", content[sectionIdx..reportContentIdx]);''', '''        // 5.0.0 (plans/OPTION_C_PLAN.md S3): hidden as its tab's panel until the tab is chosen
        Assert.Contains("""data-panel="components" hidden><div id="component-diagram" class="component-diagram-section">""", content);
        // Slice from the section's MARKUP (not the CSS selector the shared script carries,
        // which sits far earlier in the file and would span the whole scenario list).
        var sectionIdx = content.IndexOf("class=''' + Q + '''component-diagram-section''' + Q + '''", StringComparison.Ordinal);
        Assert.True(sectionIdx > 0, "component diagram section markup should be present");
        var end = content.IndexOf("class=''' + Q + '''back-to-top''' + Q + '''", sectionIdx, StringComparison.Ordinal);
        Assert.True(end > sectionIdx, "the page's last button follows the component diagram's panel");
        Assert.DoesNotContain("<details", content[sectionIdx..end]);''')

sub(C, '    public void EmbeddedComponentDiagram_Has_Toggle_Button_In_Toolbar()', '    public void EmbeddedComponentDiagram_Has_A_Tab_In_The_Bar()')
sub(C, '''        Assert.Contains("toggle_component_diagram(this)", content);
        Assert.Contains(">Component Diagram</button>", content);''', '''        // 5.0.0 (plans/OPTION_C_PLAN.md S3): a tab of the report bar in place of the toolbar's toggle
        Assert.Contains("<button class=''' + Q + '''report-tab''' + Q + ''' id=''' + Q + '''tab-components''' + Q + ''' role=''' + Q + '''tab''' + Q + ''' aria-controls=''' + Q + '''panel-components''' + Q + '''", content);
        Assert.Contains(">Components</button>", content);''')

sub(C, '    public void EmbeddedComponentDiagram_No_Toggle_Button_When_Null()', '    public void EmbeddedComponentDiagram_No_Tab_When_Null()')
sub(C, '''        Assert.DoesNotContain("toggle_component_diagram", content);''', '''        Assert.DoesNotContain("toggle_component_diagram", content);
        Assert.DoesNotContain("id=''' + Q + '''tab-components''' + Q + '''", content);''')

sub(C, '    public void EmbeddedComponentDiagram_Toggle_Function_Exists_In_Script()', '    public void EmbeddedComponentDiagram_Tab_Draws_Its_Diagram_When_It_Shows()')
sub(C, '''        Assert.Contains("function toggle_component_diagram(btn)", content);
        Assert.Contains("_renderDiagramsInContainer", content);''', '''        // 5.0.0 (S3): the tabs script draws a panel's diagrams when the panel shows, as the toggle did
        Assert.Contains("<script id=''' + Q + '''kronikol-report-tabs''' + Q + '''>", content);
        Assert.Contains("window._renderDiagramsInContainer(target)", content);''')

sub(C, '    public void EmbeddedComponentDiagram_Section_Appears_Before_ReportContent()', '    public void EmbeddedComponentDiagram_Section_Is_Its_Tab_Panel_After_The_Features()')
sub(C, '''        // The section's MARKUP, not the script's CSS selector for it — the selector sits in the
        // <head> script and would make this ordering check pass without any section emitted.
        var componentIdx = content.IndexOf("class=''' + Q + '''component-diagram-section''' + Q + '''", StringComparison.Ordinal);
        var reportContentIdx = content.IndexOf("id=''' + Q + '''report-content''' + Q + '''", StringComparison.Ordinal);
        Assert.True(componentIdx > 0 && componentIdx < reportContentIdx,
            "Component diagram section must appear before report-content div");''', '''        // The section's MARKUP, not the script's CSS selector for it, which sits in the <head> script.
        // Since 5.0.0 the section is the Components tab's panel, after the features (plans/OPTION_C_PLAN.md S3).
        var componentIdx = content.IndexOf("class=''' + Q + '''component-diagram-section''' + Q + '''", StringComparison.Ordinal);
        var reportContentIdx = content.IndexOf("id=''' + Q + '''report-content''' + Q + '''", StringComparison.Ordinal);
        var panelIdx = content.IndexOf("id=''' + Q + '''panel-components''' + Q + '''", StringComparison.Ordinal);
        Assert.True(reportContentIdx > 0 && panelIdx > reportContentIdx && componentIdx > panelIdx,
            "the component diagram section is in its panel, after the features");''')

# the two toggle functions that hid each other's panel: the tabs show one panel at a time by themselves
s = open(C, 'rb').read().decode()
a = s.index('    [Fact]\n    public void Toggle_Component_Diagram_Function_Deactivates_Timeline()')
b = s.index('    [Fact]\n    public void ComponentDiagram_html_holds_a_long_edge_to_the_label_cap()', a)
s = s[:a] + '''    /// <summary>The timeline and the component diagram were panels that each toggle had to hide when the other
    /// showed; since 5.0.0 they are tabs, one panel at a time, and the page opens with only the Features panel
    /// shown (plans/OPTION_C_PLAN.md S3; ReportTabsTests covers the configured start).</summary>
    [Fact]
    public void Only_the_features_panel_shows_when_the_page_opens()
    {
        var plantUml = "@startuml\\nleft to right direction\\nrectangle A\\n@enduml";
        var features = MakeFeatures();
        foreach (var scenario in features.SelectMany(f => f.Scenarios)) scenario.Duration = TimeSpan.FromMilliseconds(50);

        var html = ReportGenerator.GenerateHtmlReport(
            MakeDiagrams(), features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, "OnePanelAtATime.html", "Test", true,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            componentDiagramPlantUml: plantUml);

        var content = File.ReadAllText(html);
        Assert.Contains("data-panel=''' + Q + '''timeline''' + Q + ''' hidden>", content);
        Assert.Contains("data-panel=''' + Q + '''components''' + Q + ''' hidden>", content);
        Assert.Contains("data-panel=''' + Q + '''features''' + Q + '''>", content);
    }

''' + s[b:]
open(C, 'wb').write(s.encode())
print('ok')
