# S2: assertions anchored on emitted markup (the stylesheet names the band's classes in every report), and the
# tests that pinned the 4.x summary table's version row and the pie's literal colours.
def sub(path, old, new, count=1):
    s = open(path, 'rb').read().decode()
    assert s.count(old) == count, (path, s.count(old), old[:80])
    open(path, 'wb').write(s.replace(old, new).encode())

C = 'tests/Kronikol.Tests/Reports/'
R = C + 'RunBandTests.cs'
sub(R, '<div class=\\"run-stat\\"><dt>Duration</dt><dd>5.0s</dd></div></dl></div>"', '<div class=\\"run-stat\\"><dt>Duration</dt><dd>5s</dd></div></dl></div>"')
sub(R, '''        Assert.DoesNotContain("header-row", html);
        Assert.DoesNotContain("test-execution-summary", html);
        Assert.DoesNotContain("ci-metadata", html);''', '''        Assert.DoesNotContain("class=\\"header-row\\"", html);
        Assert.DoesNotContain("class=\\"test-execution-summary\\"", html);
        Assert.DoesNotContain("class=\\"ci-metadata\\"", html);''')
sub(R, '        Assert.DoesNotContain("run-stat-fail", html);', '        Assert.DoesNotContain("class=\\"run-stat run-stat-fail\\"", html);')
sub(R, '        Assert.DoesNotContain("run-ci", html);', '        Assert.DoesNotContain("<div class=\\"run-zone run-ci\\">", html);')

sub(C + 'CiMetadataReportTests.cs', '        Assert.DoesNotContain("run-ci", content);', '        Assert.DoesNotContain("<div class=\\"run-zone run-ci\\">", content);')

sub(C + 'FeatureSummaryTableReportTests.cs', '''        Assert.DoesNotContain(">Duration<", content);
        Assert.DoesNotContain(">Avg<", content);
        Assert.DoesNotContain(">Longest<", content);''', '''        // the table's own headers: since 5.0.0 the run band names the run's duration too (plans/OPTION_C_PLAN.md S2)
        Assert.DoesNotContain(")\\">Duration</th>", content);
        Assert.DoesNotContain(")\\">Avg</th>", content);
        Assert.DoesNotContain(")\\">Longest</th>", content);''')

sub(C + 'PieChartReportTests.cs', '''        Assert.Contains("#1daf26", content); // green
        Assert.Contains("#cc0000", content); // red
        Assert.Contains("#949494", content); // gray
        Assert.Contains("#2e7bff", content); // blue''', '''        // 5.0.0 (plans/OPTION_C_PLAN.md S2): an arc per status, coloured through its class by the theme's tokens
        Assert.Contains("<circle class=\\"ring-pass\\"", content);
        Assert.Contains("<circle class=\\"ring-fail\\"", content);
        Assert.Contains("<circle class=\\"ring-skip\\"", content);
        Assert.Contains("<circle class=\\"ring-bypass\\"", content);''')

V = C + 'VersionEmbeddingTests.cs'
sub(V, '''    public void Html_report_contains_kronikol_version_in_summary_table()''', '''    public void Html_report_contains_kronikol_version_in_the_generator_meta()''')
sub(V, '''        Assert.Contains("Kronikol Version:", content);
        Assert.Contains(ReportGenerator.KronikolVersion, content);
    }''', '''        // 5.0.0 (plans/OPTION_C_PLAN.md S2): the run band has no hidden version row; the meta says it
        Assert.Contains($"<meta name=\\"generator\\" content=\\"Kronikol v{ReportGenerator.KronikolVersion}", content);
    }''')
sub(V, '''    public void Html_report_kronikol_version_row_is_hidden()''', '''    public void Html_report_draws_no_kronikol_version_row()''')
sub(V, '''        // The Kronikol Version row should be hidden from the user
        Assert.Contains("style=\\"display:none\\"", content.Substring(content.IndexOf("Kronikol Version") - 50, 100));''', '''        // 4.x drew a hidden "Kronikol Version:" row in the summary table; the run band draws none (5.0.0)
        Assert.DoesNotContain("Kronikol Version", content);''')
print('ok')
