# S10b-1 (the page): the render shim's half of a diagram source's look (P21, P22; the preview's c-diagram-look.js 74-82,
# its bands lever): every step bar is drawn as a rectangle with its label in bold, and a step bar carrying a table or a
# doc string drops the blank line under it that a report before 5.0.0 wrote. Applied to what the engine draws and to
# the shim's cache key, in render and prefetch alike, never to the source the page keeps. usage: python scripts-s10b-a.py
P = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/plantuml-browser-render-script.js'
s = open(P, encoding='utf-8', newline='').read()


def sub(old, new):
    global s
    assert s.count(old) == 1, (old[:70], s.count(old))
    s = s.replace(old, new)


sub('''        // --- the shim -------------------------------------------------------------------------
        function shimRender(lines, id, opts) {
            start();
            lines = normalizeLines(lines);
            var key = lines.join('\\n');''',
    '''        // --- the shim -------------------------------------------------------------------------
        // The page's half of a source's look (5.0.0, plans/OPTION_C_PLAN.md S10, P21 and P22), applied to what the
        // engine draws and to the cache key, never to the source the page keeps and its filters, menus and notes read:
        // every step bar is drawn as a rectangle with its label in bold, which the role pass gives the band's colours,
        // and a step bar carrying a table or a doc string drops the blank line under it that a report before 5.0.0
        // wrote (the owner: no gap under the table).
        var STEP_BAR = /^hnote across (<<stepDelimiter>>(?: #black:<color:white>|<<stepBody>>: ))/;
        var STEP_BODY = /^hnote across <<stepDelimiter>><<stepBody>>:/;
        function lookLines(lines) {
            var out = null;
            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                if (typeof line !== 'string' || line.indexOf('<<stepDelimiter>>') < 0) continue;
                var drawn = STEP_BODY.test(line) ? line.replace(/(?:\\\\n)+(\\r?)$/, '$1') : line;
                drawn = drawn.replace(STEP_BAR, 'rnote across $1<b>');
                if (drawn !== line) { if (!out) out = lines.slice(); out[i] = drawn; }
            }
            return out || lines;
        }
        function lookSource(source) {
            return source.indexOf('<<stepDelimiter>>') < 0 ? source : lookLines(source.split('\\n')).join('\\n');
        }
        function shimRender(lines, id, opts) {
            start();
            lines = lookLines(normalizeLines(lines));
            var key = lines.join('\\n');''')
sub('''            for (var i = 0; i < sources.length; i++) {
                var src = sources[i];
                if (typeof src !== 'string' || !src) continue;''',
    '''            for (var i = 0; i < sources.length; i++) {
                var src = sources[i];
                if (typeof src !== 'string' || !src) continue;
                src = lookSource(src);''')
open(P, 'w', encoding='utf-8', newline='').write(s)
print('ok')
