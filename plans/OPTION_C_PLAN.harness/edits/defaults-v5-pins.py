# The pins on 4.x's start states that 5.0.0's defaults move (assertions shown, payloads truncated at 10 lines), and the
# facts whose seeded value became the default, which seed another preset so they still tell a seed from the default.
# usage: python defaults-v5-pins.py
ROOT = 'C:/Code/Kronikol-v5/'


def edit(path, pairs):
    p = ROOT + path
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    for old, new in pairs:
        if crlf:
            old, new = old.replace('\n', '\r\n'), new.replace('\n', '\r\n')
        assert s.count(old) == 1, (path, old[:70], s.count(old))
        s = s.replace(old, new)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', path)


edit('tests/Kronikol.Tests/Reports/ReportToggleDefaultsResolverTests.cs', [
    ("""            // Untouched properties keep the built-ins
            Assert.Equal(TruncateLineCount.Lines40, resolved.TruncateLines);""",
     """            // Untouched properties keep the built-ins
            Assert.Equal(TruncateLineCount.Lines10, resolved.TruncateLines);"""),
    ("""            SpecificationsToggleDefaults = { ScenariosExpanded = true, TruncateLines = TruncateLineCount.Lines10 }
        };

        var testRun = ReportToggleDefaultsResolver.Resolve(options, specifications: false);
        Assert.False(testRun.ScenariosExpanded);
        Assert.Equal(TruncateLineCount.Lines40, testRun.TruncateLines);

        var specs = ReportToggleDefaultsResolver.Resolve(options, specifications: true);
        Assert.True(specs.ScenariosExpanded);
        Assert.Equal(TruncateLineCount.Lines10, specs.TruncateLines);""",
     """            // a preset other than the built-in 10 lines (5.0.0), so the two reports' values differ
            SpecificationsToggleDefaults = { ScenariosExpanded = true, TruncateLines = TruncateLineCount.Lines20 }
        };

        var testRun = ReportToggleDefaultsResolver.Resolve(options, specifications: false);
        Assert.False(testRun.ScenariosExpanded);
        Assert.Equal(TruncateLineCount.Lines10, testRun.TruncateLines);

        var specs = ReportToggleDefaultsResolver.Resolve(options, specifications: true);
        Assert.True(specs.ScenariosExpanded);
        Assert.Equal(TruncateLineCount.Lines20, specs.TruncateLines);"""),
])

edit('tests/Kronikol.Tests/Reports/ToggleDefaultsMarkupTests.cs', [
    ("""            Assert.Contains("window._detailsDefault = 'truncated'", script);
            Assert.Contains("window._truncateLinesDefault = 40", script);""",
     """            Assert.Contains("window._detailsDefault = 'truncated'", script);
            Assert.Contains("window._truncateLinesDefault = 10", script);"""),
    ("""        var content = Generate(t => t.TruncateLines = TruncateLineCount.Lines10);
        Assert.Contains("<option value=\\"10\\" selected>10 lines</option>", content);
        Assert.DoesNotContain("<option value=\\"40\\" selected>40 lines</option>", content);""",
     """        // a preset other than the built-in 10 lines (5.0.0)
        var content = Generate(t => t.TruncateLines = TruncateLineCount.Lines20);
        Assert.Contains("<option value=\\"20\\" selected>20 lines</option>", content);
        Assert.DoesNotContain("<option value=\\"10\\" selected>10 lines</option>", content);"""),
    ("""            "<option value=\\"10\\">10 lines</option><option value=\\"15\\">15 lines</option><option value=\\"20\\">20 lines</option>" +
            "<option value=\\"25\\">25 lines</option><option value=\\"30\\">30 lines</option><option value=\\"35\\">35 lines</option>" +
            "<option value=\\"40\\" selected>40 lines</option><option value=\\"50\\">50 lines</option><option value=\\"60\\">60 lines</option>" +""",
     """            "<option value=\\"10\\" selected>10 lines</option><option value=\\"15\\">15 lines</option><option value=\\"20\\">20 lines</option>" +
            "<option value=\\"25\\">25 lines</option><option value=\\"30\\">30 lines</option><option value=\\"35\\">35 lines</option>" +
            "<option value=\\"40\\">40 lines</option><option value=\\"50\\">50 lines</option><option value=\\"60\\">60 lines</option>" +"""),
])

edit('tests/Kronikol.Tests/Tracking/TrackThatIntegrationTests.cs', [
    ("""        Assert.Contains("data-toggle=\\"assertions\\" data-shown=\\"false\\" aria-pressed=\\"false\\"", content);""",
     """        // assertions start shown since 5.0.0
        Assert.Contains("data-toggle=\\"assertions\\" data-shown=\\"true\\" aria-pressed=\\"true\\"", content);"""),
])

edit('tests/Kronikol.Tests/Reports/ParameterizedGroupRenderTests.cs', [
    ("""aria-pressed=\\"false\\" onclick=\\"window._toggleScenarioAssertions(this)\\">""",
     """aria-pressed=\\"true\\" onclick=\\"window._toggleScenarioAssertions(this)\\">"""),
])
