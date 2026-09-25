'use strict';
// DIAGRAM_COLOURS_PLAN harness (3.30.1): what the pinned engine does with captured text that reaches its
// preprocessor (comments, directives, builtin calls, the note terminator, the diagram markers) and with a
// literal `~`, and which escapes paint the text as written. Each case is one line of a note body in the
// form Kronikol's emitter writes (an arrow, then `note left` … `end note`), between a BEFORE and an AFTER
// sentinel, so a dropped line, a swallowed character and a broken diagram all show.
// Renders every case in one --batch through the shipped Node renderer on the cached engine pin (the
// same engine the report's render worker runs), and prints each case's painted note lines.
// Usage (from the repo root): node plans/DIAGRAM_COLOURS_PLAN.harness/preproc-probe.js [filter]
const fs = require('fs'), path = require('path'), cp = require('child_process');
const REPO = path.resolve(__dirname, '../..');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const RENDERER = process.env.RENDERER || path.join(REPO, 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
// The file s1 includes: written here, so a Java render shows what `!include` reads without the output
// carrying a real file from the machine. Printed as <sentinel>.
const SENTINEL = path.join(require('os').tmpdir(), 'kronikol-preproc-include-sentinel.txt').replace(/\\/g, '/');
fs.writeFileSync(SENTINEL, 'SENTINEL LINE 1\nSENTINEL LINE 2\n');
const hideSentinel = s => s.split(SENTINEL).join('<sentinel>');

// [id, body lines, note form]. The note form defaults to Kronikol's request note.
const cases = [
    // Line comments.
    ['c1-quote', ["'a',"]], ['c2-indented', ["  'a',"]], ['c3-tab', ["\t'a',"]], ['c4-midline', ["x 'a'"]],
    ['c5-lone', ["'"]], ['c6-double', ["''"]],
    // Block comments.
    ['b1-open', ["/' x", 'y']], ['b2-mid', ["x /' y"]], ['b3-inline', ["x /' y '/ z"]], ['b4-indented', ["  /' x"]],
    ['b5-close', ["'/"]], ['b6-oneline', ["/'x'/"]], ['b7-close-mid', ["x '/ y"]],
    // Directives.
    ['d1-define', ['!define FOO BAR', 'FOO here']], ['d2-include', ['!include foo.puml']], ['d3-theme', ['!theme cerulean']],
    ['d4-assert', ['!assert false']], ['d5-ifdef', ['!ifdef X']], ['d6-important', ['!important']], ['d7-bangs', ['!!']],
    ['d8-indented', ['  !foo']], ['d9-lone', ['!']], ['d10-space', ['! foo']], ['d11-var', ['!$x = 1']],
    ['d12-midline', ['x !define y']], ['d13-log', ['!log hello']], ['d14-pragma', ['!pragma teoz true']],
    ['d15-json', ['  "!key": 1']], ['d16-negation', ['!(x > 5)']], ['d17-endif', ['!endif']],
    // Builtin calls.
    ['f1-upper', ['%upper("x")']], ['f2-unquoted', ['%upper(x)']], ['f3-unknown', ['%foo(1)']], ['f4-printf', ['printf("%s(%d)")']],
    ['f5-urlenc', ['/f%C3(x)']], ['f6-percent-paren', ['100%(approx)']], ['f7-double', ['%%']], ['f8-date', ['%date()']],
    ['f9-getenv', ['%getenv("USERNAME")']], ['f10-true', ['%true()']], ['f11-space', ['%upper ("x")']], ['f12-midword', ['a%upper("x")b']],
    ['f13-underscore', ['%_x(1)']], ['f14-case', ['%Upper("x")']], ['f15-noparen', ['%upper']], ['f16-strlen', ['%strlen("abc")']],
    ['f17-urlenc-hex', ['/a%2Fb%3Fc(1)']], ['f18-digit', ['%1(x)']], ['f19-pct-letter', ['50%off(today)']],
    // Variables.
    ['v1-undefined', ['$x and $name']], ['v2-defined', ['!$x = "Y"', 'got $x']],
    // The note terminator.
    ['e1-end-note', ['end note']], ['e2-upper', ['END NOTE']], ['e3-title', ['End Note']], ['e4-endnote', ['endnote']],
    ['e5-two-spaces', ['end  note']], ['e6-indented', ['  end note']], ['e7-trailing', ['end note  ']], ['e8-rnote', ['end rnote']],
    ['e9-hnote', ['end hnote']], ['e10-endhnote', ['endhnote']], ['e11-suffix', ['end note x']], ['e12-prefix', ['x end note']],
    ['e13-ref', ['end ref']], ['e14-end', ['end']], ['e15-tab', ['end\tnote']],
    // The terminator followed by a line the diagram reads as a statement: an arrow, a delay, a divider.
    ['e16-then-arrow', ['end note', 'OrdersAPI -> Caller: x']], ['e17-then-delay', ['end note', '...']],
    ['e18-then-divider', ['end note', '== x ==']], ['e19-escaped-then-arrow', ['<U+0065>nd note', 'OrdersAPI -> Caller: x']],
    // The body's last line, right above the emitter's own terminator (form 'last': no AFTER line).
    ['e20-last-line', ['end note'], 'last'], ['e21-escaped-last-line', ['<U+0065>nd note'], 'last'],
    ['e22-endnote-last', ['endnote'], 'last'], ['e23-end-hnote-last', ['end hnote'], 'last'], ['e24-suffix-last', ['end note x'], 'last'],
    // The same in the assertion note's form (hnote across … end note).
    ['h1-end-note', ['end note'], 'hnote'], ['h2-endhnote', ['end hnote'], 'hnote'], ['h3-quote', ["'a',"], 'hnote'],
    // Diagram markers.
    ['m1-enduml', ['@enduml']], ['m2-startuml', ['@startuml']], ['m3-indented', ['  @enduml']], ['m4-endjson', ['@endjson']],
    ['m5-end', ['@end']], ['m6-suffix', ['@enduml x']], ['m7-prefix', ['x @enduml']], ['m8-upper', ['@ENDUML']], ['m9-endfoo', ['@endfoo']],
    ['m10-at', ['@x']],
    // A trailing backslash.
    ['k1-backslash', ['abc \\', 'def']],
    // Tilde.
    ['t1-letter', ['~a']], ['t2-digit', ['~1']], ['t3-space', ['~ x']], ['t4-trailing', ['a~']], ['t5-pair', ['~~']],
    ['t6-wave', ['~~x~~']], ['t7-slash', ['~/']], ['t8-tag', ['~<b>x</b>']], ['t9-dot', ['~.']], ['t10-quote', ['~"']],
    ['t11-bracket', ['~]']], ['t12-backslash', ['~\\']], ['t13-pct', ['~%']], ['t14-apos', ["~'"]], ['t15-bang', ['~!']],
    ['t16-at', ['~@']], ['t17-paren', ['~(']], ['t18-eq', ['~=']], ['t19-hash', ['~#']], ['t20-comma', ['~,']],
    ['t21-triple', ['~~~']], ['t22-home', ['~/.bashrc']], ['t23-json', ['"~"']], ['t24-star', ['~*']], ['t25-under', ['~_']],
    ['t26-dash', ['~-']], ['t27-open', ['~[']], ['t28-gt', ['~>']], ['t29-amp', ['~&']], ['t30-colon', ['~:']],
    ['t31-dollar', ['~$']], ['t32-tilde-end', ['x ~']], ['t33-two-singles', ['a~b c~d']],
    // Candidate escapes.
    ['x1-u0027', ["<U+0027>a',"]], ['x2-u0027-indented', ["  <U+0027>a',"]], ['x3-u0021', ['<U+0021>define X Y']],
    ['x4-u0025', ['<U+0025>upper("x")']], ['x5-u0065', ['<U+0065>nd note']], ['x6-u007e', ['<U+007E>/']],
    ['x7-u007e-wave', ['<U+007E><U+007E>x<U+007E><U+007E>']], ['x8-u0040', ['<U+0040>enduml']], ['x9-tilde-block', ["~/' x"]],
    ['x10-u002f', ["<U+002F>' x"]], ['x11-tilde-apos', ["~'a'"]], ['x12-tilde-bang', ['~!define']], ['x13-tilde-pct', ['~%upper("x")']],
    ['x14-lower', ["<u+0027>a"]], ['x15-short', ["<U+27>a"]], ['x16-u0025-url', ['/f<U+0025>C3(x)']], ['x17-u0065-endnote', ['<U+0065>ndnote']],
    ['x18-u007e-tag', ['<U+007E>~<b>x~</b>']], ['x19-u007e-alone', ['<U+007E>']], ['x20-u0021-indented', ['  <U+0021>foo']],
    ['x21-u0027-then-tilde', ["<U+0027>~/"]], ['x22-u0040-startuml', ['<U+0040>startuml']],
    ['x23-u003d', ['<U+003D> x']], ['x24-u007c', ['<U+007C> a | b |']], ['x25-tilde-dots', ['~..x..']],
    // Creole line markers: a heading, a table, a separator, and the same indented.
    ['l1-heading', ['= x']], ['l2-heading-tilde', ['~= x']], ['l3-heading2', ['==x==']], ['l4-indented-eq', ['  = x']],
    ['l5-indented-eq-tilde', ['  ~= x']], ['l6-table', ['| a | b |']], ['l7-table-header', ['|= h |']], ['l8-table-tilde', ['~| a |']],
    ['l9-indented-table', ['  | c | d |']], ['l10-separator', ['..x..']], ['l11-dotted', ['....']], ['l12-midline-dots', ['x..y..z']],
    ['l13-bullet', ['* x']], ['l14-bullet-tilde', ['~* x']], ['l15-indented-bullet', ['  * x']], ['l16-number', ['# x']],
    ['l17-indented-number-tilde', ['  ~# x']], ['l18-rule2', ['--']], ['l19-rule3', ['---']], ['l20-under3', ['___']],
    ['l21-rule-text', ['--- x']], ['l22-double-rule', ['==']],
    // Guillemets.
    ['g1-stereo', ['<<x>>']], ['g2-shift', ['a << b >> c']], ['g3-heredoc', ['cat <<EOF']], ['g4-tilde', ['~<<x>>']],
    ['g5-u003c', ['<U+003C><x>>']],
    // A local file and the environment: what a Java renderer would put in the report.
    ['s1-include-local', ['!include ' + SENTINEL]], ['s2-getenv-java', ['%getenv("USERNAME")']],
    // Line continuation (the Java engine joins a line ending in a backslash with the next).
    ['k2-nospace', ['abc\\', 'def']], ['k3-double', ['abc\\\\', 'def']], ['k4-trailing-space', ['abc\\ ', 'def']],
    ['k5-u005c', ['abc<U+005C>', 'def']], ['k6-last', ['abc\\']],
    // The escaper's own output for guillemets today, and the candidate.
    ['g6-current', ['<~<x>>']], ['g7-current-shift', ['a << b >> c']], ['g8-candidate', ['<U+003C>~<x>>']],
    ['g9-candidate-shift', ['a <U+003C>< b >> c']], ['g10-closing-only', ['List~<List~<int>>']],
    // Entities and embedded diagrams.
    ['n1-amp', ['a &amp; b']], ['n2-numeric', ['&#65;&#x42;']], ['n3-lt', ['&lt;tag&gt;']], ['n4-copy', ['&copy; x']],
    ['n5-braces', ['{{name}}']], ['n6-open', ['{{']], ['n7-close', ['}}']], ['n8-dots-line', ['~....']],
    // Character references under the wrap width: which escape survives the word-by-word layout.
    ['r1-u0026', ['<U+0026>#39; stays']], ['r2-u0023', ['&<U+0023>39; stays']], ['r3-u0033', ['&#<U+0033>9; stays']],
    ['r4-tilde', ['~&#39; stays']], ['r5-plain', ['&#39; stays']], ['r6-amp', ['&amp; &lt; stays']], ['r7-u003b', ['&#39<U+003B> stays']],
    ['r8-zwsp', ['&<U+200B>#39; stays']], ['r9-wj', ['&<U+2060>#39; stays']], ['r10-zwsp-hash', ['&#<U+200B>39; stays']],
    // A rule line as each escaper writes it: the generator escapes a marker only when the line pairs it,
    // the browser's YAML view every doubled one.
    ['j1-rule-generator', ['<U+002D>-']], ['j2-rule3-generator', ['<U+002D>--']], ['j3-under-generator', ['<U+005F>__']],
    ['j4-rule-browser', ['~-~-']], ['j5-rule3-browser', ['~-~--']], ['j6-under-browser', ['~_~__']],
    ['j7-rule4-browser', ['~-~-~-~-']], ['j8-indented-browser', ['  ~-~-']], ['j9-dots-browser', ['<U+002E>...']], ['j10-eq-generator', ['<U+003D>=']],
];

// PREFIX=kronikol adds the lines Kronikol's own prefix carries (CreatePlantUmlPrefix): teoz, and the note wrap
// width, whose word-by-word layout reads a note's text differently from the plain one.
function source(body, form) {
    const lines = ['@startuml'];
    if (process.env.PREFIX === 'kronikol') lines.push('!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1');
    lines.push('participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI', 'Caller -> OrdersAPI: POST: /api/orders');
    if (form === 'hnote') lines.push('hnote across <<assertionNote>> #e6ffe6');
    else lines.push('note left');
    lines.push('BEFORE', ...body, ...(form === 'last' ? [] : ['AFTER']), 'end note', 'OrdersAPI --> Caller: 200', '@enduml');
    return lines.join('\n');
}

function decode(s) {
    return s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'")
        .replace(/&#(\d+);/g, (m, d) => String.fromCodePoint(+d)).replace(/&#x([0-9a-f]+);/gi, (m, h) => String.fromCodePoint(parseInt(h, 16)))
        .replace(/&nbsp;|&#160;/g, '\u00a0').replace(/&amp;/g, '&');
}

// The painted lines: <text> elements grouped by baseline, in x order.
function paintedLines(svg) {
    const rows = new Map();
    for (const m of svg.matchAll(/<text\b([^>]*)>([\s\S]*?)<\/text>/g)) {
        const x = +((m[1].match(/\bx="([\d.]+)"/) || [])[1] || 0), y = Math.round(+((m[1].match(/\by="([\d.]+)"/) || [])[1] || 0));
        if (!rows.has(y)) rows.set(y, []);
        rows.get(y).push([x, decode(m[2])]);
    }
    return [...rows.entries()].sort((a, b) => a[0] - b[0]).map(([, parts]) => parts.sort((a, b) => a[0] - b[0]).map(p => p[1]).join(' '));
}

// SOURCES_OUT=<dir> writes each case's source as <id>.puml and stops. SVG_IN=<dir> reads <id>.svg (or
// <id>.err) rendered elsewhere, by the IKVM package through ikvm-render.cs, instead of rendering here.
const filter = process.argv[2];
const chosen = cases.filter(c => !filter || c[0].includes(filter));
if (process.env.SOURCES_OUT) {
    fs.mkdirSync(process.env.SOURCES_OUT, { recursive: true });
    for (const [id, body, form] of chosen) fs.writeFileSync(path.join(process.env.SOURCES_OUT, id + '.puml'), source(body, form));
    console.log('wrote ' + chosen.length + ' sources to ' + process.env.SOURCES_OUT);
    process.exit(0);
}
const started = Date.now();
let out;
if (process.env.SVG_IN) {
    out = chosen.map(([id]) => {
        const svg = path.join(process.env.SVG_IN, id + '.svg'), err = path.join(process.env.SVG_IN, id + '.err');
        return fs.existsSync(svg) ? { id, svg: fs.readFileSync(svg, 'utf8') } : fs.existsSync(err) ? { id, error: fs.readFileSync(err, 'utf8') } : { id };
    });
    console.log('SVGs read from ' + path.basename(process.env.SVG_IN) + ' (rendered by ikvm-render.cs), ' + chosen.length + ' cases\n');
} else {
    const input = chosen.map(([id, body, form]) => JSON.stringify({ id, source: source(body, form) })).join('\n') + '\n';
    const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'],
        { input, maxBuffer: 1 << 28, timeout: 600000 });
    if (r.status !== 0) console.log('renderer exit ' + r.status + ': ' + (r.stderr || '').toString().slice(0, 400));
    out = (r.stdout || '').toString().split('\n').filter(Boolean).map(l => JSON.parse(l));
    console.log('engine ' + path.basename(C) + ', renderer ' + path.relative(REPO, RENDERER) + ', ' + chosen.length + ' cases in ' + (Date.now() - started) + ' ms\n');
}
for (const [id, body, form] of chosen) {
    const res = out.find(o => o.id === id);
    const shown = JSON.stringify(body.length === 1 ? body[0] : body);
    if (!res || (!res.svg && !res.error)) { console.log(hideSentinel(id.padEnd(22) + shown + '  => NO RESULT')); continue; }
    if (res.error) { console.log(hideSentinel(id.padEnd(22) + shown + '  => ERROR ' + res.error.slice(0, 160))); continue; }
    const lines = paintedLines(res.svg);
    // Form 'last' has no AFTER line: the reply's label (`200`, or `2 200` under autonumber), drawn below the
    // note, closes the window instead.
    const b = lines.indexOf('BEFORE');
    const a = form === 'last' ? lines.findIndex((l, i) => i > b && /(^| )200$/.test(l)) : lines.indexOf('AFTER');
    let verdict;
    const drawn = lines.filter(l => l.trim());
    // The engine's error picture opens with its version line and lists the source, BEFORE included: it
    // shows as where it stopped (its [From …] line) and its last two lines, the engine's message.
    if (/^PlantUML (version|\d)/.test(drawn[0] || '')
        || (lines.some(l => /Syntax Error|Error line|Cannot|not found|Assertion/i.test(l)) && b < 0))
        verdict = 'BROKEN: ' + JSON.stringify([drawn.find(l => /^\[From /.test(l)) || '', ...drawn.slice(-2)]);
    else if (b >= 0 && a > b) verdict = JSON.stringify(lines.slice(b + 1, a));
    else verdict = 'NOTE CUT: ' + JSON.stringify(drawn.slice(0, 10));
    console.log(hideSentinel(id.padEnd(22) + shown + (form ? ' [' + form + ']' : '') + '  => ' + verdict));
}
