"""Apply or revert a named source mutation in a checkout, for the plan's red proofs.

    python mutate.py apply <name> [<name> ...]
    python mutate.py revert            # puts back every file a mutation touched, from the copy taken before it
    python mutate.py list

Each mutation is an exact-string replacement that must match once. The file is copied aside before its first
mutation (to %TEMP%/kron-mutate, keyed by the checkout), and revert restores those copies, so uncommitted work in the
checkout survives a revert. KRONIKOL_ROOT names the checkout; by default it is the one this file sits in. Nothing
here is shipped.
"""
import hashlib, io, os, shutil, sys, tempfile

ROOT = os.path.abspath(os.environ.get('KRONIKOL_ROOT') or os.path.join(os.path.dirname(__file__), '..', '..', '..'))
BACKUP = os.path.join(tempfile.gettempdir(), 'kron-mutate', hashlib.sha1(ROOT.lower().encode()).hexdigest()[:12])

RG = 'src/Kronikol/Reports/ReportGenerator.cs'
MUTATIONS = {
    # R1, F1: each anchored assertion must fail when the piece it reads is missing.
    'f1-diag-summary': (RG,
        'var summary = $"Report diagnostics ({diagnostics.Count}',
        'var summary = $"Diagnostics ({diagnostics.Count}'),
    'f1-diag-kind': (RG,
        'var kindClass = $"report-diagnostic-kind report-diagnostic-kind-{',
        'var kindClass = $"report-diagnostic-kind report-diagnostic-kindx-{'),
    'f1-diag-none': (RG,
        '        if (diagnostics.Count == 0)\n            return string.Empty;\n\n        var byKind',
        '        if (diagnostics.Count >= 0)\n            return string.Empty;\n\n        var byKind'),
    'f1-sparkline': ('src/Kronikol/History/HistoryHtml.cs',
        'return $" <span class=\\"history-sparkline\\" role',
        'return $" <span class=\\"history-sparklinx\\" role'),
    'f1-clusters': (RG,
        'body.Append($"<details class=\\"failure-clusters\\"{',
        'body.Append($"<details class=\\"failure-clusterz\\"{'),

    # R2, section 7.4: one per behaviour.
    'r2-sparkline-without-opt-in': (RG,
        'var scenarioHistory = showScenarioHistory ? history : null;',
        'var scenarioHistory = history;'),
    'r2-history-css-always': (RG,
        '+ (history is not null && (showHistorySection || drawsVerdicts) ? Stylesheets.HistoryStyleSheet : "")',
        '+ Stylesheets.HistoryStyleSheet'),
    'r2-diagnostics-css-always': (RG,
        '+ (includeTestRunData && showReportDiagnostics && diagnostics is { Count: > 0 } ? Stylesheets.ReportDiagnosticsStyleSheet : "")',
        '+ Stylesheets.ReportDiagnosticsStyleSheet'),
    'r2-fences-kept-by-default': (RG,
        '_ => VerdictFences.Variant(LoadResource(name), verdicts));',
        '_ => VerdictFences.Variant(LoadResource(name), true));'),
    'r2-fences-stripped-when-asked': (RG,
        '_ => VerdictFences.Variant(LoadResource(name), verdicts));',
        '_ => VerdictFences.Variant(LoadResource(name), false));'),
    'r2-show-scenario-history-ignored': (RG,
        'showScenarioHistory: options.ShowScenarioHistory));',
        'showScenarioHistory: false));'),
    'r2-embed-ignored-by-page': (RG,
        'var labsHistory = options.EmbedHistoryInReport ? history?.Verdicts : null;',
        'var labsHistory = history?.Verdicts;'),
    'r2-page-not-planned': (RG,
        '        if (options.GenerateLabsReport)\n            planned.Add(LabsReportGenerator.FileName(options.HtmlTestRunReportFileName));',
        ''),
    'r2-link-to-literal-report': (RG,
        'options.GenerateTestRunReport ? $"{options.HtmlTestRunReportFileName}.html" : null, runId, runEndedAt,',
        'options.GenerateTestRunReport ? "TestRunReport.html" : null, runId, runEndedAt,'),
    'r2-generate-labs-ignored': (RG,
        'if (options.GenerateLabsReport && LabsReportGenerator.HasContent(labsHistory, reportDiagnostics))',
        'if (LabsReportGenerator.HasContent(labsHistory, reportDiagnostics))'),
    'r2-empty-page-written': ('src/Kronikol/Reports/LabsReportGenerator.cs',
        'history is not null || diagnostics.Count > 0;',
        'true;'),
    'r2-merged-page-drops-diagnostics': ('src/Kronikol/Reports/Merge/MergeableReportRenderer.cs',
        '                report.Diagnostics, Path.GetFileName(destination),',
        '                [], Path.GetFileName(destination),'),
    'r2-f3-merged-section-drops-diagnostics': ('src/Kronikol/Reports/Merge/MergeableReportRenderer.cs',
        '            diagnostics: report.Diagnostics,\n',
        ''),
    'r2-page-before-report-in-pointer': (RG,
        '            new[]\n            {\n                $"{options.HtmlTestRunReportFileName}.html",',
        '            new[]\n            {\n                LabsReportGenerator.FileName(options.HtmlTestRunReportFileName),\n                $"{options.HtmlTestRunReportFileName}.html",'),
    'r2-debug-section-names-the-page': ('src/Kronikol/Reports/RunSummaryConsoleWriter.cs',
        '                                          && !f.Name.EndsWith(LabsReportGenerator.Suffix, StringComparison.OrdinalIgnoreCase))',
        ')'),
    'r2-table-wrap-does-not-scroll': ('src/Kronikol/Reports/labs-styles.css',
        'overflow-x: auto;',
        'overflow-x: visible;'),
}

def _backup(path):
    target = os.path.join(BACKUP, path)
    if not os.path.exists(target):
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copyfile(os.path.join(ROOT, path), target)

def apply(name):
    path, a, b = MUTATIONS[name]
    p = os.path.join(ROOT, path)
    raw = io.open(p, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    s = raw.decode('utf-8-sig')
    n = s.count(a)
    if n != 1:
        sys.exit(f'{name}: expected one match in {path}, found {n}')
    _backup(path)
    s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(s)
    print(f'applied {name} to {path}')

def revert():
    restored = []
    for folder, _, files in os.walk(BACKUP):
        for f in files:
            source = os.path.join(folder, f)
            path = os.path.relpath(source, BACKUP)
            shutil.copyfile(source, os.path.join(ROOT, path))
            os.remove(source)
            restored.append(path.replace('\\', '/'))
    print('reverted', ', '.join(sorted(restored)) or 'nothing')

if __name__ == '__main__':
    if sys.argv[1] == 'apply':
        for n in sys.argv[2:]:
            apply(n)
    elif sys.argv[1] == 'revert':
        revert()
    elif sys.argv[1] == 'list':
        print('\n'.join(MUTATIONS))
