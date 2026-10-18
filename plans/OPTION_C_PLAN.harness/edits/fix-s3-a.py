# S3 fixes: the helpers' doc comments, a tabs script that names no section, and the in-panel margins in the sheets
# of the sections they style (a default report names no history or diagnostics anywhere, 4.6.0).
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:90])
    open(path, 'wb').write(s.replace(old, new).encode())

G = 'src/Kronikol/Reports/ReportGenerator.cs'
s = open(G, 'rb').read().decode()
band_doc = '''    /// <summary>
    /// The run band (5.0.0, plans/OPTION_C_PLAN.md S2, D3): the ring, then the test execution summary (status, when,
    /// and the counts), then the CI zone when the run had one, as one row the page's CSS stacks when it does not fit.
    /// </summary>
'''
a = s.index(band_doc)
b = s.index('    private static void AppendRunBand(StringBuilder body,', a)
helpers = s[a + len(band_doc):b]
assert helpers.startswith('    /// <summary>A tab of the report bar'), helpers[:60]
s = s[:a] + helpers + band_doc + s[b:]
open(G, 'wb').write(s.encode())

J = 'src/Kronikol/Reports/report-tabs-script.js'
sub(J, '''        if (t.closest('.jump-to-failure, .history-link, .report-diagnostics a[href^="#"], .failure-cluster-scenario-link')) toFeatures();
        // the summary of a section in its tab is its heading: the tab is its disclosure, so it stays open
        var summary = t.closest('#panel-history > details > summary, #panel-diagnostics > details > summary');
        if (summary) e.preventDefault();''', '''        // a link to a scenario names it by its address (a failure cluster's, a history entry's, a diagnostic's), and
        // Next Failure goes to one: named by kind, never by section, so a page without a section names none of it
        if (t.closest('.jump-to-failure, a[href^="#scenario-"], a[href^="#sid-"]')) toFeatures();
        // the summary of a section in a tab of its own is its heading: the tab is its disclosure, so it stays open
        if (t.closest('.report-panel:not(#panel-features) > details > summary')) e.preventDefault();''')

C = 'src/Kronikol/Reports/stylesheets.css'
sub(C, '''.report-panel > .scenario-timeline, .report-panel > .component-diagram-section, .report-panel > .history-section,
.report-panel > .report-diagnostics { margin: 0 0 14px; }''', '''.report-panel > .scenario-timeline, .report-panel > .component-diagram-section { margin: 0 0 14px; }''')
print('ok')
