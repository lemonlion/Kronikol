# S11b (D44, R17.10): the flame chart in the diagrams' manner. One hue per source from a palette of eight (blue, violet,
# teal, rose, green, amber, purple, coral), written as --fh by the page's script and by InternalFlowRenderer alike, in
# place of a hue from 0 to 359 at 60% saturation lightened with depth; the stylesheet draws a pale fill, a 3px edge in the
# hue's stronger shade, ink text, 20px rows 2px apart and 4px corners (mock/build.js 238-255, mock/c-round17.css 98-110).
# The fill, edge and hover shades are tokens with a dark value in both dark blocks of the token sheet; the bar's rules
# fall back to the light shades where a page has no token sheet. depth stays in the flame data (its format), unused here.
# usage: python scripts-s11b.py <worktree root>
import sys
R = sys.argv[1].rstrip('/') + '/src/Kronikol/'

def edit(path, old, new, count=1):
    s = open(R + path, encoding='utf-8', newline='').read()
    assert s.count(old) == count, (path, s.count(old), old[:60])
    open(R + path, 'w', encoding='utf-8', newline='').write(s.replace(old, new))

FLAME = 'Reports/flame-chart-render-script.js'
edit(FLAME, '''    function buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs) {
        var hue = Math.abs(hashCode(source)) % 360;
        var lightness = 70 + Math.min(depth * 5, 20);
''', '''    // One hue per source from eight that sit with the diagrams (blue, violet, teal, rose, green, amber, purple, coral);
    // the stylesheet draws the bar from it (5.0.0, plans/OPTION_C_PLAN.md S11, D44). InternalFlowRenderer picks the same.
    var FLAME_HUES = [212, 262, 188, 338, 152, 32, 290, 8];
    function buildBar(source, name, leftPct, widthPct, durMs, totalDurMs) {
        var hue = FLAME_HUES[Math.abs(hashCode(source)) % FLAME_HUES.length];
''')
edit(FLAME, """            + '%;width:' + widthPct.toFixed(2) + '%;background:hsl(' + hue + ', 60%, ' + lightness
            + '%)" data-left=""", """            + '%;width:' + widthPct.toFixed(2) + '%;--fh:' + hue
            + '" data-left=""")
edit(FLAME, 'var srcIdx = sp[0], name = sp[1], leftPct = sp[2], widthPct = sp[3], depth = sp[4], durMs = sp[5];',
     'var srcIdx = sp[0], name = sp[1], leftPct = sp[2], widthPct = sp[3], durMs = sp[5];', count=2)
edit(FLAME, 'html.push(buildBar(source, name, leftPct, widthPct, depth, durMs, totalDurMs));',
     'html.push(buildBar(source, name, leftPct, widthPct, durMs, totalDurMs));', count=2)

CSS = 'Reports/internal-flow-popup-styles.css'
edit(CSS, '''.iflow-flame { padding: 4px 0; }
.iflow-flame-bar {
    position: relative;
    height: 22px;
    min-width: 4px;
    border-radius: 3px;
    margin: 1px 0;
    overflow: hidden;
    font: 11px/22px -apple-system, 'Segoe UI', sans-serif;
    color: #333;
    cursor: pointer;
    border: 1px solid rgba(0,0,0,0.08);
}
.iflow-flame-bar:hover { filter: brightness(0.92); }
''', '''.iflow-flame { padding: 6px 0; }
/* A bar in its source's hue (--fh, one of eight): a pale fill, a 3px edge in a stronger shade, ink text, 20px rows 2px
   apart (5.0.0, plans/OPTION_C_PLAN.md S11, D44); the shades are the theme's, the light ones where a page has none. */
.iflow-flame-bar {
    position: relative;
    height: 20px;
    min-width: 4px;
    border-radius: 4px;
    margin: 2px 0;
    overflow: hidden;
    font: 11px/20px -apple-system, 'Segoe UI', sans-serif;
    color: var(--kron-ink, #333);
    cursor: pointer;
    box-sizing: border-box;
    border: 0;
    border-left: 3px solid hsl(var(--fh, 215) var(--dg-flame-edge, 42% 54%));
    background: hsl(var(--fh, 215) var(--dg-flame-fill, 58% 93%));
}
.iflow-flame-bar:hover { background: hsl(var(--fh, 215) var(--dg-flame-hover, 62% 87%)); }
''')

TOKENS = 'Reports/report-tokens.css'
edit(TOKENS, '\n    --dg-step-table: #ffffff;\n',
     '\n    --dg-step-table: #ffffff;\n    --dg-flame-fill: 58% 93%;\n    --dg-flame-edge: 42% 54%;\n    --dg-flame-hover: 62% 87%;\n')
edit(TOKENS, '\n    --dg-step-table: #131a24;\n',
     '\n    --dg-step-table: #131a24;\n    --dg-flame-fill: 26% 21%;\n    --dg-flame-edge: 48% 60%;\n    --dg-flame-hover: 30% 27%;\n')
edit(TOKENS, '        --dg-step-table: #131a24;\n',
     '        --dg-step-table: #131a24;\n        --dg-flame-fill: 26% 21%;\n        --dg-flame-edge: 48% 60%;\n        --dg-flame-hover: 30% 27%;\n')

RENDERER = 'InternalFlow/InternalFlowRenderer.cs'
edit(RENDERER, '''        // The hue the browser's flame chart gives the source (flame-chart-render-script.js hashCode): string.GetHashCode
        // is randomised per process, so the same source changed colour from one run to the next until 4.7.3.
        var hue = Math.Abs((long)SourceHash(rawSource)) % 360;
        var color = $"hsl({hue}, 60%, {70 + Math.Min(depth * 5, 20)}%)";
''', '''        // The hue the browser's flame chart gives the source (flame-chart-render-script.js hashCode and FLAME_HUES):
        // string.GetHashCode is randomised per process, so the same source changed colour from one run to the next until
        // 4.7.3. The stylesheet draws the bar from it (5.0.0).
        var hue = FlameHues[(int)(Math.Abs((long)SourceHash(rawSource)) % FlameHues.Length)];
''')
edit(RENDERER, '''style=\\"margin-left:{leftPct:F2}%;width:{widthPct:F2}%;background:{color}\\" ''',
     '''style=\\"margin-left:{leftPct:F2}%;width:{widthPct:F2}%;--fh:{hue}\\" ''')
edit(RENDERER, '    private static void RenderFlameNode(StringBuilder sb, SpanNode node, DateTime earliest, double totalMs, int depth)\n',
     '''    // The flame chart's eight hues, as flame-chart-render-script.js has them (5.0.0, plans/OPTION_C_PLAN.md S11, D44)
    private static readonly int[] FlameHues = [212, 262, 188, 338, 152, 32, 290, 8];

    private static void RenderFlameNode(StringBuilder sb, SpanNode node, DateTime earliest, double totalMs)
''')
edit(RENDERER, '            RenderFlameNode(sb, child, earliest, totalMs, depth + 1);\n',
     '            RenderFlameNode(sb, child, earliest, totalMs);\n')
edit(RENDERER, 'RenderFlameNode(sb, root, earliest, totalMs, 0);', 'RenderFlameNode(sb, root, earliest, totalMs);', count=3)
print('ok')
