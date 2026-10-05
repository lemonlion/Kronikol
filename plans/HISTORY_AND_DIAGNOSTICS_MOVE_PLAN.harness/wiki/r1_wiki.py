"""The 4.5.1 (R1) wiki edits, applied to a wiki checkout after 4.5.1 is published:

    python r1_wiki.py <wiki checkout>

Exact-string replacements, each matching once; the wiki is CRLF, so the new text takes the file's line endings.
"""
import io, os, sys

EDITS = [
('Component-Diagrams.md',
"""| `ShowRelationshipFlows` | `bool` | `true` | Adds a clickable relationship list below the component diagram. Each relationship opens a popup showing the aggregated internal flow (OTel spans) for that caller→service path. Requires `InternalFlowTracking = true`. |
| `RelationshipFlowStyle` | `InternalFlowDiagramStyle` | `ActivityDiagram` | Visual style for relationship flow popups. `ActivityDiagram` shows a PlantUML diagram. `CallTree` shows an HTML nested list. |
| `ShowSystemFlameChart` | `bool` | `true` | Adds a system-level flow section below the component diagram showing performance analytics (bar chart, status codes, payload sizes, outliers, etc.). |
| `LowCoverageThreshold` | `int` | `3` | Relationships with fewer than this many calls are rendered with dashed arrows and labelled as low-coverage. |
| `ArrowColorMode` | `ArrowColorMode` | `DependencyType` | Controls arrow coloring. `DependencyType` colors arrows by target service type (blue=HTTP, red=Database, etc.). `Performance` uses P95-based green/orange/red hotspot coloring. |""",
"""| `ShowRelationshipFlows` | `bool` | `true` | Has no effect: nothing reads it. The relationship list and its flow popups it switched left in 2.0.92-beta. Kept so that code which sets it still compiles; its removal waits for v5. |
| `RelationshipFlowStyle` | `InternalFlowDiagramStyle` | `ActivityDiagram` | Has no effect: nothing reads it. It chose the style of the relationship flow popups, which left in 2.0.92-beta. |
| `ShowSystemFlameChart` | `bool` | `true` | Has no effect: nothing reads it. The system-level flow section it switched left in 2.0.92-beta. |
| `LowCoverageThreshold` | `int` | `3` | Has no effect: nothing reads it. See [Low-Coverage Warnings](#low-coverage-warnings) for where the dashed arrows come from. |
| `ArrowColorMode` | `ArrowColorMode` | `DependencyType` | Controls arrow coloring. `DependencyType` colors arrows by target service type (blue=HTTP, red=Database, etc.). `Performance` colours by P95 latency (green/orange/red) in a diagram drawn with relationship stats; a generated report passes none, so it draws its arrows uncoloured in this mode (see [Performance-Based Arrow Colouring](#performance-based-arrow-colouring-opt-in)). |"""),
('Component-Diagrams.md',
"""| `MaxFlameChartTests` | `int` | `50` | Legacy option retained for backward compatibility. |""",
"""| `MaxFlameChartTests` | `int` | `50` | Has no effect: nothing reads it. Kept so that code which sets it still compiles. |"""),
('Component-Diagrams.md',
"""This provides an instant visual indicator of which service dependencies are performance bottlenecks.

### Low-Coverage Warnings

Relationships with fewer calls than `LowCoverageThreshold` (default: 3) are rendered with **dashed arrows** (`..>`) instead of solid arrows. This highlights service paths that may not have sufficient test coverage to produce reliable statistics.""",
"""This provides an instant visual indicator of which service dependencies are performance bottlenecks.

The colours come from relationship stats (`ComponentFlowSegmentBuilder.ComputeRelationshipStats`, handed to
`ComponentDiagramGenerator.GeneratePlantUml`), as the [Stats-Driven Labels](#stats-driven-labels) do. A generated
report passes none, and none has since 2.0.92-beta, so in `TestRunReport.html` and `ComponentDiagram.html` this mode
draws the arrows uncoloured. `ArrowColorMode.Performance`'s doc says so since 4.5.1.

### Low-Coverage Warnings

In a diagram drawn with relationship stats, a relationship with fewer calls than the threshold
`ComputeRelationshipStats` is given (its `lowCoverageThreshold` argument, default 3) is rendered with **dashed arrows**
(`..>`) instead of solid arrows. This highlights service paths that may not have sufficient test coverage to produce
reliable statistics. A generated report passes no stats, so it draws no dashed arrows, and
`ComponentDiagramOptions.LowCoverageThreshold` has no effect: nothing reads it."""),
('Component-Diagrams.md',
"""    public bool ShowRelationshipFlows { get; set; } = true;
    public InternalFlowDiagramStyle RelationshipFlowStyle { get; set; } = InternalFlowDiagramStyle.ActivityDiagram;
    public bool ShowSystemFlameChart { get; set; } = true;
    public int LowCoverageThreshold { get; set; } = 3;
    public int MaxFlameChartTests { get; set; } = 50;""",
"""    public bool ShowRelationshipFlows { get; set; } = true;   // no effect: nothing reads it
    public InternalFlowDiagramStyle RelationshipFlowStyle { get; set; } = InternalFlowDiagramStyle.ActivityDiagram;   // no effect
    public bool ShowSystemFlameChart { get; set; } = true;   // no effect
    public int LowCoverageThreshold { get; set; } = 3;   // no effect
    public int MaxFlameChartTests { get; set; } = 50;   // no effect"""),
('Diagnostics-and-Debugging.md',
"""> **Tip:** Enable `DiagnosticMode` temporarily during development""",
"""The page is written into the run's reports directory, beside its other files. Its assertion value resolution
section fills only when `Track.DiagnosticMode` is on as well; `DiagnosticMode` does not turn it on. A pass that finds no
scenarios while calls were logged (xUnit v3's discovery pass, or a run whose tests never enqueued their contexts)
writes the page too, unless the previous run's `Run.json` lists its own copy, which it keeps (4.5.1).

> **Tip:** Enable `DiagnosticMode` temporarily during development"""),
]

def main(wiki):
    by_file = {}
    for name, old, new in EDITS:
        by_file.setdefault(name, []).append((old, new))
    for name, reps in by_file.items():
        path = os.path.join(wiki, name)
        raw = io.open(path, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        crlf = '\r\n' in text
        for old, new in reps:
            if crlf:
                old = old.replace('\n', '\r\n')
                new = new.replace('\n', '\r\n')
            count = text.count(old)
            if count != 1:
                sys.exit(f'{name}: expected one match, found {count}: {old[:80]!r}')
            text = text.replace(old, new)
        io.open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(text)
        print(f'edited {name} ({len(reps)} change(s))')

if __name__ == '__main__':
    main(sys.argv[1])
