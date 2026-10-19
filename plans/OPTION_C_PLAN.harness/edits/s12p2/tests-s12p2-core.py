# S12 phone part 2's core facts (applied after D49 and the count contrast fix):
#   FilterBarTests: the phone's Filters toggle is a button on a line of its own between the search line and the rows
#   StylesheetRulesTests: one fold class at every width; the phone's toggle and sticky search line take tokens and read
#   at AA in every block of the token sheet (the count theory's token lookup becomes a shared helper); no sheet keeps an
#   empty @media block
# usage: python tests-s12p2-core.py <worktree root>
import sys

ROOT = sys.argv[1].rstrip('/\\') + '/'


def edit(path, pairs):
    raw = open(ROOT + path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    s = raw.decode('utf-8-sig')
    assert 'On_a_phone_the_rows_fold_under_a_Filters_button' not in s and 'No_sheet_has_an_empty_media_block' not in s, \
        'phone part 2 is in already'
    for old, new in pairs:
        assert s.count(old) == 1, (path, s.count(old), old[:70])
        s = s.replace(old, new)
    open(ROOT + path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)


edit('tests/Kronikol.Tests/Reports/FilterBarTests.cs', [(
    r'''    [Fact]
    public void The_rows_start_folded_under_More_filters_at_the_end_of_the_search_line()
''', r'''    [Fact]
    public void On_a_phone_the_rows_fold_under_a_Filters_button_on_its_own_line()
    {
        var html = Generate("FilterBar_Phone.html");

        // a button, which the keyboard reaches and which says whether the rows are open, on a line of its own between the
        // search line and the rows: on a phone the readout moves onto it (report-filter-bar-script.js)
        InOrder(html,
            "<div class=\"filter-search\">",
            "<div class=\"mobile-filter-line\"><button type=\"button\" class=\"mobile-filter-toggle\" aria-expanded=\"false\" aria-controls=\"filter-rows\"",
            ">Filters</button></div>",
            "<div class=\"filters\" id=\"filter-rows\">");
        Assert.DoesNotContain("<div class=\"mobile-filter-toggle\"", html);
    }

    [Fact]
    public void The_rows_start_folded_under_More_filters_at_the_end_of_the_search_line()
''')])

edit('tests/Kronikol.Tests/Reports/StylesheetRulesTests.cs', [
    # the count theory's token lookup, shared
    (r'''        var tokens = Stylesheets.ReportTokensStyleSheet;
        string Resolve(string? value)
        {
            var m = TokenReference().Match(value ?? "");
            Assert.True(m.Success, $"'{value}' is not one token");
            return CssRules.Value(tokens, block, m.Groups[1].Value, media) ?? CssRules.Value(tokens, ":root", m.Groups[1].Value)
                ?? throw new InvalidOperationException(m.Groups[1].Value + " is not in the token sheet");
        }
        var ink = Resolve(CssRules.Value(Base, ".more-filters-count", "color"));
        var ground = Resolve(CssRules.Value(Base, ".more-filters-count", "background"));
''', r'''        var ink = TokenValue(block, media, CssRules.Value(Base, ".more-filters-count", "color"));
        var ground = TokenValue(block, media, CssRules.Value(Base, ".more-filters-count", "background"));
'''),
    (r'''    [GeneratedRegex(@"^var\((--[\w-]+)\)$")]
    private static partial Regex TokenReference();
''', r'''    [GeneratedRegex(@"^var\((--[\w-]+)\)$")]
    private static partial Regex TokenReference();

    // what a declaration that is one token reference gives in a block of the token sheet, as the cascade resolves it
    private static string TokenValue(string block, string? media, string? value)
    {
        var tokens = Stylesheets.ReportTokensStyleSheet;
        var m = TokenReference().Match(value ?? "");
        Assert.True(m.Success, $"'{value}' is not one token");
        return CssRules.Value(tokens, block, m.Groups[1].Value, media) ?? CssRules.Value(tokens, ":root", m.Groups[1].Value)
            ?? throw new InvalidOperationException(m.Groups[1].Value + " is not in the token sheet");
    }

    /// <summary>The filter rows fold with one class at every width: More filters sets it on a desktop and the Filters
    /// button on a phone, so the rows stay as they were when the window crosses the phone breakpoint, and the search help
    /// opens while they are folded at either width (on a phone the rows were hidden by an inline style, the help with
    /// them).</summary>
    [Fact]
    public void The_filter_rows_fold_with_one_class_at_every_width()
    {
        Assert.Equal("none", CssRules.Value(Base, ".filtering-box.rows-folded:not(.help-open) > .filters", "display"));
        Assert.Equal("none", CssRules.Value(Base, ".filtering-box.rows-folded.help-open > .filters > :not(.search-help-panel)", "display"));
        Assert.Null(CssRules.Value(Base, ".filtering-box.rows-folded:not(.help-open) > .filters", "display", "(min-width: 768.02px)"));
    }

    /// <summary>The phone's Filters button and its sticky search line take their colours from the tokens (they were
    /// rgb(100, 100, 100) and rgb(224, 224, 224) in every theme): the search line the filter box's own ground, and the
    /// button the rows' label ink, which reads at AA on that ground in every block of the token sheet.</summary>
    [Theory]
    [InlineData(":root[data-theme=\"dark\"]", null)]
    [InlineData(":root:not([data-theme])", "(prefers-color-scheme: dark)")]
    [InlineData(":root", null)]
    public void The_phone_Filters_button_and_search_line_read_in_every_theme(string block, string? media)
    {
        var box = CssRules.Value(Base, ".filtering-box", "background");
        Assert.Equal(box, CssRules.Value(Base, ".filter-search", "background", "(max-width: 768px)"));

        var ink = TokenValue(block, media, CssRules.Value(Base, ".mobile-filter-toggle", "color"));
        var ground = TokenValue(block, media, box);
        var ratio = Contrast(ink, ground);
        Assert.True(ratio >= 4.5, $"{block}: the Filters button is {ink} on {ground}, {ratio.ToString("0.00", CultureInfo.InvariantCulture)}:1");
    }

    /// <summary>No sheet keeps an empty media block (stylesheets.css held one at min-width 768.02px).</summary>
    [Fact]
    public void No_sheet_has_an_empty_media_block()
    {
        foreach (var (name, css) in AllSheets.Append(("report-tokens.css", Stylesheets.ReportTokensStyleSheet)))
            Assert.False(EmptyMedia().IsMatch(CssComment().Replace(css, "")), $"{name} has an empty @media block");
    }

    [GeneratedRegex(@"@media[^{]*\{\s*\}")]
    private static partial Regex EmptyMedia();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CssComment();
'''),
])
print('ok: FilterBarTests.cs, StylesheetRulesTests.cs')
