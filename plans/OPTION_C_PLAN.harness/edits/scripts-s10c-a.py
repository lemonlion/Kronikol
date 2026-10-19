# S10c: a diagram stands in the middle of its box (the owner's round 17 item 19: c-round17.css 68-74, and a diagram
# drawn in fragments too, which the preview's rule did not reach), a request's link takes the report's accent (item 5:
# c-round17.css 79, kept to links painted in the engine's own blue so a theme's link colour stays the theme's), and a
# copied, saved or opened diagram carries the look (Q6, F9): context-menu-script.js writes each drawn element's paint
# into the copy, under the light theme whichever the page shows. usage: python scripts-s10c-a.py
V5 = 'C:/Code/Kronikol-v5/src/Kronikol/Reports/'

css = V5 + 'inline-svg-styles.css'
s = open(css, encoding='utf-8', newline='').read()
assert 'margin-inline: auto' not in s
s = s.rstrip('\n') + '\n' + r'''
/* A diagram narrower than its box stands in the middle of it, drawn whole or in fragments (5.0.0, plans/OPTION_C_PLAN.md
   S10); one as wide as its box still fills it, the auto margins coming to 0. The box held its drawing 1em in from the
   left and 0 from the right, which put a centred drawing half an em right of the box's middle. */
.plantuml-browser > svg, .plantuml-inline-svg > svg, .puml-fragment > svg { display: block; margin-inline: auto; }
.plantuml-inline-svg, .plantuml-browser { padding-right: 1em; }
/* A request's link, drawn on hover (or always, where links are always shown), in the report's accent and not the
   engine's #0000FF, which read as purple beside the rest of the palette. A theme's own link colour stays its own. */
.plantuml-browser svg text[data-iflow-fill="#0000ff" i][text-decoration="underline"] { fill: var(--kron-accent) !important; }
'''
open(css, 'w', encoding='utf-8', newline='').write(s)

js = V5 + 'context-menu-script.js'
s = open(js, encoding='utf-8', newline='').read()
anchor = '    // XMLSerializer writes a C0 control, U+FFFE or U+FFFF as it is'
assert s.count(anchor) == 1 and 'function lookClone' not in s
s = s.replace(anchor, r'''    // A copy carries no page CSS, so the look the page gives a drawn diagram (5.0.0, plans/OPTION_C_PLAN.md S10, Q6: the
    // theme's colours, the role pass's cards, bands and step tables, the fold it hides) is written into the copy: each
    // drawn element's paint where the stylesheet changed it, and the ground, which a copy without transparency takes. A
    // step table's ground, with no fill of its own, was drawn black (F9). A copy takes the light theme's look whichever
    // theme the page shows, as every copy did before: it is pasted into documents, nearly always on white. The theme is
    // put back in the same task, so the page paints nothing in between, and its own picture is left as it is.
    var PAINTED = /^(path|rect|line|polygon|polyline|circle|ellipse|text|tspan)$/i;
    function lookClone(svg) {
        var clone = svg.cloneNode(true);
        var from = svg.querySelectorAll('*'), to = clone.querySelectorAll('*');
        if (from.length !== to.length || !window.getComputedStyle) return { svg: clone, ground: null };
        var root = document.documentElement, theme = root.getAttribute('data-theme');
        var probe = document.createElement('i');
        probe.style.display = 'none';
        document.body.appendChild(probe);
        root.setAttribute('data-theme', 'light');
        var ground = null, seen = {};
        // a colour as an attribute writes it, as the browser computes it ('none' as it is)
        function computed(v) {
            if (!v) return null;
            if (!(v in seen)) {
                probe.style.color = '';
                probe.style.color = v;
                seen[v] = probe.style.color ? getComputedStyle(probe).color : v;
            }
            return seen[v];
        }
        try {
            for (var i = 0; i < from.length; i++) {
                if (!PAINTED.test(from[i].tagName)) continue;
                var cs = getComputedStyle(from[i]);
                for (var k = 0; k < 2; k++) {
                    var p = k ? 'stroke' : 'fill', v = cs.getPropertyValue(p);
                    if (v && v !== computed(from[i].getAttribute(p))) to[i].style.setProperty(p, v);
                }
                if (cs.visibility === 'hidden') to[i].style.setProperty('visibility', 'hidden');
            }
            var bg = getComputedStyle(svg).backgroundColor;
            if (bg && bg !== 'transparent' && !/^rgba\(.*,\s*0\)$/.test(bg)) ground = bg;
        } finally {
            if (theme === null) root.removeAttribute('data-theme'); else root.setAttribute('data-theme', theme);
            probe.remove();
        }
        return { svg: clone, ground: ground };
    }

''' + anchor, 1)
old = '        return new XMLSerializer().serializeToString(svg).replace('
assert s.count(old) == 1
s = s.replace(old, '        return new XMLSerializer().serializeToString(svg.isConnected ? lookClone(svg).svg : svg).replace(')
old = '''    function svgToCanvasWithBg(svg, callback) {
        var bg = getBackgroundColor(svg);
        var clone = svg.cloneNode(true);
'''
assert s.count(old) == 1
s = s.replace(old, '''    function svgToCanvasWithBg(svg, callback) {
        var look = svg.isConnected ? lookClone(svg) : null;
        var bg = look && look.ground ? look.ground : getBackgroundColor(svg);
        var clone = look ? look.svg : svg.cloneNode(true);
''')
open(js, 'w', encoding='utf-8', newline='').write(s)
print('ok')
