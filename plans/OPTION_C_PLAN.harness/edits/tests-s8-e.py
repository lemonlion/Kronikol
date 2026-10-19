# S8: ToolbarStylingTests on the default theme's payload controls and view tabs (5.0.0, the C look): a payload button
# takes the hover tint on its transparent edge, the picked one the selected tint and edge; a view tab has no fill, the
# picked one the accent's ink (its underline is a shadow); the phone's settings gear takes the hover tint on the select
# edge. The violet theme's rows keep its 4.x colours until S13 makes it token overrides.
p = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/ToolbarStylingTests.cs'
s = open(p, encoding='utf-8', newline='').read()

def sub(old, new):
    global s
    assert s.count(old) == 1, (s.count(old), old[:90])
    s = s.replace(old, new)

sub('''    private const string ControlBorder = "rgb(174, 183, 198)"; // --kron-control-border: a filter control's edge (S6)
''', '''    private const string ControlBorder = "rgb(174, 183, 198)"; // --kron-control-border: a filter control's edge (S6)
    private const string SelectBorder = "rgb(122, 132, 150)";  // --kron-select-border: a dropdown's and a gear's edge (S8)
    private const string Accent = "rgb(37, 89, 199)";          // --kron-accent: the picked view tab's ink (S8)
    private const string None = "rgba(0, 0, 0, 0)";
''')
sub('''        { false, BlueTint, BlueTintBorder, ".toolbar-right .details-radio-btn:not(.details-active)" },
''', '''        // 5.0.0: a payload button takes the hover tint on its transparent edge (plans/OPTION_C_PLAN.md S8)
        { false, Hover, None, ".toolbar-right .details-radio-btn:not(.details-active)" },
''')
sub('''    [Theory]
    [InlineData(true, Violet)]
    [InlineData(false, Blue)]
    public async Task The_active_details_radio_takes_the_themes_colour_at_rest_and_hovered(bool violet, string active)
    {
        await Open($"DetailsRadio{violet}.html", violet);

        var radio = Page.Locator(".toolbar-right .details-radio-btn.details-active").First;
        await Paints(radio, active, active);
        await radio.HoverAsync();
        await Paints(radio, active, active);
    }''', '''    [Theory]
    [InlineData(true, Violet, Violet)]
    [InlineData(false, Selected, SelectedEdge)] // 5.0.0: the selected tint and edge (S8)
    public async Task The_active_details_radio_takes_the_themes_colour_at_rest_and_hovered(bool violet, string active, string edge)
    {
        await Open($"DetailsRadio{violet}.html", violet);

        var radio = Page.Locator(".toolbar-right .details-radio-btn.details-active").First;
        await Paints(radio, active, edge);
        await radio.HoverAsync();
        await Paints(radio, active, edge);
    }''')
sub('''    public static TheoryData<bool, bool, string, string, string, string> TabColours => new()
    {
        // violet, flow tracking, idle, idle hovered, active, active hovered
        { true, true, Grey, Tint, Violet, VioletDark },
        { true, false, Grey, Tint, Violet, VioletDark },
        { false, true, Grey, "rgb(232, 240, 254)", Blue, "rgb(51, 103, 214)" },
        { false, false, Grey, "rgb(232, 240, 254)", Blue, "rgb(51, 103, 214)" },
    };''', '''    public static TheoryData<bool, bool, string, string, string, string, string?> TabColours => new()
    {
        // violet, flow tracking, idle, idle hovered, active, active hovered, active edge
        { true, true, Grey, Tint, Violet, VioletDark, Violet },
        { true, false, Grey, Tint, Violet, VioletDark, Violet },
        // 5.0.0: a view tab has no fill; the picked one is marked by the accent's ink and underline (S8)
        { false, true, None, Hover, None, Hover, null },
        { false, false, None, Hover, None, Hover, null },
    };''')
sub('''        bool violet, bool internalFlowTracking, string idle, string idleHovered, string active, string activeHovered)
    {''', '''        bool violet, bool internalFlowTracking, string idle, string idleHovered, string active, string activeHovered, string? activeEdge)
    {''')
sub('''        await Paints(idleTab, idle);
        await Paints(activeTab, active, active);
        await idleTab.HoverAsync();
        await Paints(idleTab, idleHovered);
        await activeTab.HoverAsync();
        await Paints(activeTab, activeHovered, active);''', '''        await Paints(idleTab, idle);
        await Paints(activeTab, active, activeEdge);
        if (!violet) await Expect(activeTab).ToHaveCSSAsync("color", Accent);
        await idleTab.HoverAsync();
        await Paints(idleTab, idleHovered);
        await activeTab.HoverAsync();
        await Paints(activeTab, activeHovered, activeEdge);''')
sub('''    [InlineData(false, BlueTint, BlueTintBorder)]
    public async Task The_phone_diagram_settings_button_takes_the_themes_tint_when_hovered(bool violet, string tint, string border)''',
    '''    [InlineData(false, Hover, SelectBorder)] // 5.0.0: the hover tint on the gear's edge (S8)
    public async Task The_phone_diagram_settings_button_takes_the_themes_tint_when_hovered(bool violet, string tint, string border)''')
sub('''        var toolbar = Page.Locator("details.example-diagrams .diagram-toggle").First;
        await Expect(toolbar).ToBeVisibleAsync();''', '''        // 5.0.0: the scenario's settings, behind the gear of its diagrams (S8)
        await OpenScenarioSettingsAsync();
        var toolbar = Page.Locator("details.example-diagrams .diagram-toggle").First;
        await Expect(toolbar).ToBeVisibleAsync();''')
open(p, 'w', encoding='utf-8', newline='').write(s)

for name, old, new in [
        ('NoteAppearanceTests.cs', 'Assert.Equal("Note width", await select.Locator("optgroup").First.GetAttributeAsync("label"));',
         'Assert.Equal("Payload width", await select.Locator("optgroup").First.GetAttributeAsync("label"));'),
        ('NoteFormatDropdownTests.cs', 'Assert.Equal("Note payload format", await ReportSelect.GetAttributeAsync("aria-label"));',
         'Assert.Equal("Payload format", await ReportSelect.GetAttributeAsync("aria-label"));')]:
    q = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/' + name
    t = open(q, encoding='utf-8', newline='').read()
    assert t.count(old) == 1, (name, old)
    open(q, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
print('ok')
