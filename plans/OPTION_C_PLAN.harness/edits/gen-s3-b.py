# S3: the History and Diagnostics tabs are on by default (the owner, 2026-10-18); the scenarios' own history stays opt-in.
P = 'src/Kronikol/ReportConfigurationOptions.cs'
s = open(P, 'rb').read().decode()
def sub(old, new):
    global s
    assert s.count(old) == 1, (s.count(old), old[:90])
    s = s.replace(old, new)

sub('''    /// <summary>
    /// Whether <c>TestRunReport.html</c> also carries the "Report diagnostics" section: the collapsed list of
    /// what the run recorded about itself, such as a tap whose decoder gave up, a skipped capture line or a render
    /// that failed. Default: <c>false</c>, because on a healthy run it is a line of noise above the features.
    /// The list is on the labs page beside the report whenever the run recorded an entry
    /// (<see cref="GenerateLabsReport"/>), and every diagnostic still reaches
    /// <see cref="Kronikol.Ingestion.IngestResult.Diagnostics"/>, the <c>diagnostics</c> array of
    /// <c>TestRunReport.json</c>, and the console. <see cref="ReportToggleDefaults.DiagnosticsOpen"/> decides whether
    /// the report's section starts open, and is inert while this is <c>false</c>; the labs page's list is always open.
    /// </summary>
    public bool ShowReportDiagnosticsSection { get; set; }''', '''    /// <summary>
    /// Whether <c>TestRunReport.html</c> has a Diagnostics tab when the run recorded something about itself, such as
    /// a tap whose decoder gave up, a skipped capture line or a render that failed: the list, open in its tab, with
    /// the count of its entries on the tab. Default: <c>true</c> since 5.0.0, where the section is a tab beside the
    /// features rather than a line above them (4.6.0 to 4.x: <c>false</c>). A healthy run has nothing to list and no
    /// tab. The list is also on the labs page beside the report (<see cref="GenerateLabsReport"/>), and every
    /// diagnostic still reaches <see cref="Kronikol.Ingestion.IngestResult.Diagnostics"/>, the <c>diagnostics</c>
    /// array of <c>TestRunReport.json</c>, and the console. <see cref="ReportToggleDefaults.DiagnosticsOpen"/> has
    /// nothing to open in a tab; the labs page's list is always open.
    /// </summary>
    public bool ShowReportDiagnosticsSection { get; set; } = true;''')

sub('''    /// <summary>
    /// Whether <c>TestRunReport.html</c> also carries the History section beside the timeline: the run's
    /// summary line, the trend of the last runs, and the lists of what changed. Default: <c>false</c>,
    /// because a run where nothing changed still has the section to say so, and above the features that reads as
    /// noise. The section is on the labs page beside the report whenever the run read history
    /// (<see cref="GenerateLabsReport"/>), and the history a run read is still in <c>Failures.md</c>, the CTRF
    /// document and the ledger, and still what <c>kronikol query history</c> answers from. It needs
    /// <see cref="EmbedHistoryInReport"/>.
    /// </summary>
    public bool ShowHistorySection { get; set; }''', '''    /// <summary>
    /// Whether <c>TestRunReport.html</c> has a History tab when the run read history: the run's summary line, the
    /// trend of the last runs, and the lists of what changed. Default: <c>true</c> since 5.0.0, where the section is a
    /// tab beside the features rather than a section above them (4.6.0 to 4.x: <c>false</c>); nothing of it is in the
    /// features unless <see cref="ShowScenarioHistory"/> asks. The section is also on the labs page beside the report
    /// (<see cref="GenerateLabsReport"/>), and the history a run read is still in <c>Failures.md</c>, the CTRF
    /// document and the ledger, and still what <c>kronikol query history</c> answers from. It needs
    /// <see cref="EmbedHistoryInReport"/>.
    /// </summary>
    public bool ShowHistorySection { get; set; } = true;''')

sub('''    /// the search code they need. Default: <c>false</c>: since 4.6.0 a default report carries no history, and the
    /// same sparklines and verdicts are on the labs page beside it (<see cref="GenerateLabsReport"/>). It needs
    /// <see cref="EmbedHistoryInReport"/>, and a ledger to read.''', '''    /// the search code they need. Default: <c>false</c>: a run's history is the report's History tab
    /// (<see cref="ShowHistorySection"/>), not part of its features, and the same sparklines and verdicts are on the
    /// labs page beside it (<see cref="GenerateLabsReport"/>). It needs <see cref="EmbedHistoryInReport"/>, and a
    /// ledger to read.''')
open(P, 'wb').write(s.encode())
print('ok')
