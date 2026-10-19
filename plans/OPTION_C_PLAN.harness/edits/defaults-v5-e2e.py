# Playwright facts that relied on 4.x's start (assertions hidden, payloads truncated at 40 lines): each reads the new
# default where it reads the default, and otherwise writes its report with the start it was written for.
# usage: python defaults-v5-e2e.py
ROOT = 'C:/Code/Kronikol-v5/tests/Kronikol.Tests.EndToEnd/'


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


edit('DiagramNoteBasicTests.cs', [
    ("""        var value = await select.InputValueAsync();
        Assert.Equal("40", value);""",
     """        var value = await select.InputValueAsync();
        Assert.Equal("10", value); // the built-in 10 lines since 5.0.0 (40 through 4.x)"""),
    ("""        var select = Page.Locator(".diagram-toggle .truncate-lines-select").First;
        Assert.Equal("40", await select.InputValueAsync());
        await select.SelectOptionAsync("3");""",
     """        var select = Page.Locator(".diagram-toggle .truncate-lines-select").First;
        Assert.Equal("10", await select.InputValueAsync());
        await select.SelectOptionAsync("3");"""),
])

edit('CapturedTextPageTests.cs', [
    ("""        // Assertions start hidden: the page takes each assertion note out of the source before drawing it, and the""",
     """        // Assertions hidden (written so, as they start shown since 5.0.0): the page takes each assertion note out of
        // the source before drawing it, and the"""),
    ("""            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Assertions", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs);""",
     """            DateTime.UtcNow, DateTime.UtcNow, null, Path.Combine(TempDir, fileName), "Assertions", true,
            diagramFormat: DiagramFormat.PlantUml, plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { AssertionsShown = false });"""),
])

edit('ReportTestHelper.cs', [
    ("""            [new DiagramAsCode("t1", "", source)], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { HeadersShown = headersShown, NotePayloadFormat = notePayloadFormat });""",
     """            [new DiagramAsCode("t1", "", source)], features,
            DateTime.UtcNow, DateTime.UtcNow,
            null, Path.Combine(tempDir, fileName), "Test Report", true,
            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            // the note is longer than 5.0.0's 10-line truncation, and its facts read it whole
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with
            {
                HeadersShown = headersShown, NotePayloadFormat = notePayloadFormat, TruncateLines = TruncateLineCount.Lines40
            });"""),
])

# the multi-line SQL note's fixture: the second GenerateHtmlReport call with this exact tail in the file
p = ROOT + 'ReportTestHelper.cs'
s = open(p, encoding='utf-8', newline='').read()
anchor = s.index('public static string GenerateReportWithMultiLineSqlNote')
tail = """            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs);"""
at = s.index(tail, anchor)
s = s[:at] + """            diagramFormat: DiagramFormat.PlantUml,
            plantUmlRendering: PlantUmlRendering.BrowserJs,
            // the note is longer than 5.0.0's 10-line truncation, and its facts read it whole
            toggleDefaults: ResolvedToggleDefaults.BuiltIn with { TruncateLines = TruncateLineCount.Lines40 });""" + s[at + len(tail):]
open(p, 'w', encoding='utf-8', newline='').write(s)
print('edited the SQL note fixture')
