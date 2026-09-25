# Do the plan's cited lines still say what the plan says they say?
#
#   PYTHONUTF8=1 python check-anchors.py            # the working tree
#   PYTHONUTF8=1 python check-anchors.py origin/main  # any git revision, read through git show
#
# Run from anywhere inside the repository. Each check is (file, line the plan cites at 3.27.2
# `2843018a`, a marker that line must hold). A moved line is printed with the nearest line that
# holds the marker, which is the number to use at that revision. At 3.29.6 (`ad289f55`) 19 of 38
# held: the render script had shifted by 21 lines above `bindIflowLinks`, whose body was unchanged,
# and `ReportGenerator.cs` by 12 to 39.
import subprocess, sys, os

REV = sys.argv[1] if len(sys.argv) > 1 else None
ROOT = subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True, text=True).stdout.strip()

CHECKS = [
    ('src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs', 41, '__iflowSegments'),
    ('src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs', 113, 'HideLink'),
    ('src/Kronikol/InternalFlow/InternalFlowHtmlGenerator.cs', 163, 'data-plantuml-z'),
    ('src/Kronikol/Reports/internal-flow-popup-script.js', 3, '__iflowSegments'),
    ('src/Kronikol/Reports/internal-flow-popup-script.js', 21, 'iflowData[segmentId]'),
    ('src/Kronikol/Reports/internal-flow-popup-script.js', 72, "getAttribute('data-plantuml')"),
    ('src/Kronikol/Reports/internal-flow-popup-script.js', 125, 'addEventListener'),
    ('src/Kronikol/Reports/plantuml-browser-render-script.js', 1031, 'function bindIflowLinks'),
    ('src/Kronikol/Reports/plantuml-browser-render-script.js', 1033, '__iflowSegments'),
    ('src/Kronikol/Reports/plantuml-browser-render-script.js', 1041, 'iflowData[segId]'),
    ('src/Kronikol/Reports/plantuml-browser-render-script.js', 1058, 'extractIflowMap'),
    ('src/Kronikol/Reports/plantuml-browser-render-script.js', 1085, 'iflowData[segId]'),
    ('src/Kronikol/Reports/plantuml-browser-render-script.js', 1120, "getAttribute('data-plantuml')"),
    ('src/Kronikol/Reports/ReportGenerator.cs', 298, 'trackedLogs = runLogs'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 308, 'BuildFlowScript'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 390, 'componentDiagramPlantUml ='),
    ('src/Kronikol/Reports/ReportGenerator.cs', 419, 'ShouldEmbedComponentDiagram'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 1169, 'internalFlowPopupScript'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 1232, 'internalFlowDataScript'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 2147, 'AddWholeTestFlowSearchPieces'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 2152, 'Regex.Matches(activityHtml, "data-plantuml-z'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 4053, 'logLookup'),
    ('src/Kronikol/Reports/ReportGenerator.cs', 4479, 'internalFlowSegments'),
    ('src/Kronikol/Reports/Merge/MergeableReportRenderer.cs', 31, 'internalFlowDataScript'),
    ('src/Kronikol/Reports/Merge/MergeableReportRenderer.cs', 38, 'WrapSegmentData'),
    ('src/Kronikol/Reports/Merge/MergeableReportRenderer.cs', 46, 'GeneratePlantUml'),
    ('src/Kronikol/Reports/Merge/MergeableReport.cs', 42, '__iflowSegments'),
    ('src/Kronikol/Reports/report-export-function.js', 83, 'head.innerHTML'),
    ('src/Kronikol/Reports/report-search-index.js', 523, 'JSON.parse'),
    ('src/Kronikol/InternalFlow/InternalFlowSegmentBuilder.cs', 25, 'logsByTest'),
    ('src/Kronikol/Reports/BackgroundAttribution.cs', 124, 'UnknownTestId'),
    ('src/Kronikol/Reports/ReportDiagnostics.cs', 46, 'UnknownTestId'),
    ('src/Kronikol/ComponentDiagram/ComponentDiagramGenerator.cs', 234, 'iflow-rel'),
    ('src/Kronikol/PlantUml/PlantUmlCreator.cs', 366, '[[#iflow-'),
    ('tests/Kronikol.Tests.EndToEnd/TestPageGenerator.cs', 238, '__iflowSegments'),
    ('tests/Kronikol.Tests/ComponentDiagram/ComponentDiagramReportTests.cs', 333, '__iflowSegments'),
    ('tests/Kronikol.Tests/Reports/Merge/MergeableReportTests.cs', 360, '__iflowSegments'),
    ('tests/Kronikol.Tests/InternalFlow/InternalFlowHtmlGeneratorTests.cs', 187, 'data-plantuml-z'),
]


def lines_of(path):
    if REV:
        out = subprocess.run(['git', 'show', f'{REV}:{path}'], capture_output=True, cwd=ROOT)
        if out.returncode != 0:
            return None
        return out.stdout.decode('utf-8', 'replace').split('\n')
    full = os.path.join(ROOT, path)
    return open(full, encoding='utf-8', errors='replace').read().split('\n') if os.path.exists(full) else None


cache, held, report = {}, 0, []
for path, n, marker in CHECKS:
    if path not in cache:
        cache[path] = lines_of(path)
    lines = cache[path]
    if lines is None:
        report.append(f'  GONE   {path}')
        continue
    if n <= len(lines) and marker in lines[n - 1]:
        held += 1
        continue
    hits = [i + 1 for i, l in enumerate(lines) if marker in l]
    near = min(hits, key=lambda h: abs(h - n)) if hits else None
    report.append(f'  MOVED  {path}:{n} -> {near if near else "marker not found"}   ({marker})')

print(f'{REV or "working tree"}: {held} of {len(CHECKS)} cited lines hold')
print('\n'.join(report))
