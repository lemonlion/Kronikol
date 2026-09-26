'use strict';
// Second P3 audit: a differential check of Kronikol's escapers against the engine. Each case is captured text;
// escape-oracle.fsx escapes it with the real escaper for its context, the shipped Node renderer paints it on the
// pinned engine (or SVG_IN reads SVGs the Java engine drew), and the painted text is compared with the captured
// text, whitespace aside. Any difference is text a report does not draw as captured.
// Usage: node fuzz-probe.js <Kronikol.dll> [kinds=body,label,labellink,doc,cell] [filter]
//   SOURCES_OUT=<dir> writes the sources and stops; SVG_IN=<dir> reads <id>.svg / <id>.err instead of rendering.
const fs = require('fs'), path = require('path'), cp = require('child_process'), os = require('os');
const HERE = __dirname;
const REPO = process.env.REPO || path.resolve(__dirname, '..', '..');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const RENDERER = process.env.RENDERER || path.join(REPO, 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const dll = process.argv[2];
const kinds = (process.argv[3] || 'body,label,labellink,doc,cell').split(',');
const filter = process.argv[4];
const ch = n => String.fromCharCode(n);
const BS = ch(92);

// ---- The corpus -------------------------------------------------------------------------------------------
const S = ['~', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '_', '+', '-', '=', '[', ']', '{', '}', '|', BS,
    ':', ';', '"', "'", '<', '>', ',', '.', '/', '?', '`'];
const raws = new Map(); // name -> raw text
const add = (name, raw) => { if (!raws.has(name)) raws.set(name, raw); };
S.forEach((a, i) => {
    add(`s${i}-start`, a + 'x');
    add(`s${i}-mid`, 'a' + a + 'b');
    add(`s${i}-end`, 'x' + a);
    add(`s${i}-alone`, a);
    add(`s${i}-pair`, a + a + 'x' + a + a);
    add(`s${i}-pairmid`, 'a' + a + a + 'b' + a + a + 'c');
    add(`s${i}-wrap`, a + 'x' + a);
    add(`s${i}-triple`, a + a + a);
    add(`s${i}-quad`, a + a + a + a);
    add(`s${i}-tag`, '<' + a + 'x>');
    add(`s${i}-tagclose`, '<x' + a + '>');
    S.forEach((b, j) => {
        add(`d${i}_${j}-start`, a + b + 'x');
        add(`d${i}_${j}-mid`, 'a' + a + b + 'c');
    });
});
const snippets = {
    // Code points and references, captured literally.
    cp1: '<U+0041>', cp2: 'x<U+0041>y', cp3: '<U+1F600>', cp4: '<U+10FFFF>', cp5: '<U+110000>', cp6: '<u+0041>', cp7: '<U+41>',
    cp8: '<U+D800>', cp9: '<U+003C>b>', cp10: '<U+007E>', cp11: '<U+200B>', cp12: 'R says <U+00E9>t<U+00E9>', cp13: '<U+0041',
    cp14: 'U+0041>', cp15: '<U+00041>', cp16: '<U+000041>', cp17: '<U+0000041>', cp18: '<U+0000>', cp19: '<U+000A>', cp20: '<U+0022><U+0022>x<U+0022><U+0022>',
    ref1: '&#65;', ref2: '&#x41;', ref3: '&amp;', ref4: '&lt;b&gt;', ref5: '&nbsp;', ref6: '&#128512;', ref7: '&#0;', ref8: '&#;', ref9: '&#65',
    ref10: '&#065;', ref11: '&#x;', ref12: '&quot;', ref13: '&apos;', ref14: '&copy;', ref15: 'a&#39;s', ref16: '&#9999999;', ref17: '&&#65;', ref18: '&#&#65;',
    // Backslash sequences.
    bs1: BS + 't', bs2: BS + 'n', bs3: BS + 'r', bs4: BS + BS, bs5: BS + BS + 't', bs6: BS + BS + BS + 't', bs7: 'C:' + BS + 'temp' + BS + 'new',
    bs8: 'C:' + BS + BS + 'temp' + BS + BS + 'new', bs9: BS + 'u0041', bs10: BS + 'x41', bs11: BS + '0', bs12: BS + '"', bs13: BS + "'", bs14: BS + 'b',
    bs15: BS + 'f', bs16: BS + 'v', bs17: BS + 'a', bs18: BS + 'e', bs19: BS + 's', bs20: BS + 'l', bs21: 'a' + BS, bs22: 'a' + BS + BS, bs23: 'a' + BS + BS + BS,
    bs24: '{"msg": "line1' + BS + 'nline2' + BS + 'tend"}', bs25: '"path": "C:' + BS + BS + 'temp' + BS + BS + 'x.txt"', bs26: BS + 'T', bs27: BS + 'N',
    bs28: 'a' + BS + 'tb' + BS + 'tc', bs29: BS + 't' + BS + 't', bs30: '<' + BS + 't>', bs31: '~' + BS + 't', bs32: BS + '~', bs33: BS + '<b>',
    bs34: BS + '%date()', bs35: BS + '[[x]]', bs36: BS + BS + 'n', bs37: BS + 'n' + BS + 'n', bs38: 'tab' + BS + 'there',
    // Preprocessor forms.
    pp1: '%date()', pp2: '%n()', pp3: '$x', pp4: '${x}', pp5: '$(x)', pp6: '%PATH%', pp7: '%s', pp8: '%d%%', pp9: '100%', pp10: '%upper("x")',
    pp11: '!define', pp12: '@startuml', pp13: "'comment", pp14: "/'", pp15: "'/", pp16: '{{', pp17: '}}', pp18: '{{json', pp19: 'end note', pp20: 'endnote',
    pp21: '%%date()', pp22: '%' + BS + 'date()', pp23: '%date ()', pp24: '!$x = 1', pp25: '!unquoted', pp26: '%_date()', pp27: '%1()',
    pp28: '%get_variable_value("$x")', pp29: '%invoke_procedure("$p")', pp30: '%call_user_func("f")', pp31: '%load_json("x")', pp32: '%filename()',
    pp33: '%dirpath()', pp34: '%getenv("PATH")', pp35: '%version()', pp36: '%newline()', pp37: '%chr(65)', pp38: '%dec2hex(255)',
    pp39: '%str2json("x")', pp40: '%random()', pp41: '%feature("x")', pp42: '%function_exists("x")', pp43: '%lower(%upper("x"))',
    // Creole forms.
    cr1: '**b**', cr2: '//i//', cr3: '""m""', cr4: '--s--', cr5: '__u__', cr6: '~~w~~', cr7: '[[x]]', cr8: '[[x y]]', cr9: '[[http://x y]]', cr10: '<<x>>',
    cr11: '<b>x</b>', cr12: '<color:red>x</color>', cr13: '<size:20>x', cr14: '<img:x.png>', cr15: '<#red>x', cr16: '<&x>', cr17: '<:x:>', cr18: '<$x>',
    cr19: '= h', cr20: '== h ==', cr21: '| a | b |', cr22: '|= a |', cr23: '..x..', cr24: '....', cr25: '----', cr26: '____', cr27: '====', cr28: '* x',
    cr29: '** x', cr30: '# x', cr31: '## x', cr32: '<U+0041>**b**', cr33: '^^x^^', cr34: ',,x,,', cr35: '<sub>x</sub>', cr36: '<sup>x</sup>', cr37: '<s>x</s>',
    cr38: '<u>x</u>', cr39: '<w>x</w>', cr40: '<i>x</i>', cr41: '<plain>x</plain>', cr42: '<font color=red>x</font>', cr43: '<back:red>x</back>',
    cr44: '<code>x</code>', cr45: '<strike>x</strike>', cr46: '<U>x</U>', cr47: '<B>x</B>', cr48: '<br>', cr49: '<latex>x</latex>', cr50: '<math>x</math>',
    cr51: '<Color:red>x', cr52: '<COLOR:red>x', cr53: '<#FF0000>x', cr54: '<#FF0000,#00FF00>x', cr55: '<&x><&y>', cr56: '**a** **b**', cr57: '~~~~',
    cr58: '[[{tip}x]]', cr59: '[[x{tip}]]', cr60: '{{{', cr61: '<<<x>>>', cr62: '<< x >>', cr63: '<<', cr64: '>>', cr65: '|a|', cr66: '| a |b',
    cr67: '..', cr68: '...', cr69: '.....', cr70: '---', cr71: '--', cr72: '___', cr73: '__', cr74: '==', cr75: '=', cr76: '----x----', cr77: '....x....',
    cr78: '__x__y__', cr79: '**x**y**', cr80: '//x//y//', cr81: '""x""y""', cr82: '--x--y--', cr83: '~~x~~y~~',
    // Real-world text.
    rw1: "SELECT * FROM t WHERE a = 'x' -- note -- more", rw2: '^[a-z]+$', rw3: 'a*b*c', rw4: 'https://a.example/b//c https://d.example/e',
    rw5: '<?xml version="1.0"?>', rw6: '<!-- comment -->', rw7: '<![CDATA[x]]>', rw8: '~/x/y', rw9: '$HOME/x', rw10: '${VAR}', rw11: '$(cmd)',
    rw12: 'List<List<int>>', rw13: 'a->b', rw14: 'a<-b', rw15: 'a<->b', rw16: 'x=>y', rw17: 'a==b', rw18: 'a!=b', rw19: 'a||b', rw20: 'a&&b',
    rw21: 'a<<2', rw22: 'a>>2', rw23: 'a<b>c', rw24: ':) :-) <3', rw25: '-->', rw26: '<--', rw27: '***', rw28: '```code```', rw29: "'''", rw30: '"""',
    rw31: 'a ~ b', rw32: '~', rw33: '~~', rw34: 'x~', rw35: 'Vec<&str>', rw36: 'Map<String, List<&str>>', rw37: '/api/users/{id}/orders?page[size]=10&sort=-date',
    rw38: 'fish & chips', rw39: 'a < b > c', rw40: '5 > 3 && 2 < 4', rw41: 'x <= y >= z', rw42: 'arr[0][1]', rw43: '[[1,2],[3,4]]', rw44: '{{a}}',
    rw45: 'a ** b', rw46: 'a // b // c', rw47: 'C:\\Program Files', rw48: 'e = mc^2', rw49: '#include <stdio.h>', rw50: '@Override',
    rw51: '/* c */', rw52: '// c', rw53: '# c', rw54: '-- c', rw55: 'REM c', rw56: '<!DOCTYPE html>', rw57: '<a href="x">y</a>',
    rw58: 'SELECT 1 /* a */ /* b */', rw59: 'x /* a */ y // b // c', rw60: '==x==', rw61: '== x', rw62: 'a | b | c', rw63: '|x', rw64: 'x|',
    rw65: '__init__.py', rw66: '__proto__', rw67: 'dunder __x__ and __y__', rw68: 'my__var__name', rw69: '--verbose --dry-run', rw70: 'a -- b -- c',
    rw71: 'http://x/a~b~c', rw72: 'ab~~cd~~ef', rw73: 'Bearer abc~def~ghi', rw74: '"~"', rw75: '"~/"', rw76: 'x = "*"; y = "*"',
    rw77: '"//"', rw78: '"--", "--"', rw79: '[x] [y]', rw80: '[[', rw81: ']]', rw82: '[x]]', rw83: '[[x]', rw84: 'a[[b', rw85: 'a]]b',
    rw86: '{"a": "<b>"}', rw87: '{"re": "^\\\\d+$"}', rw88: 'emoji 😀 here', rw89: 'naïve café', rw90: 'שלום', rw91: '日本語', rw92: 'e' + ch(0x301),
    // Control and special characters.
    ...Object.fromEntries([1, 2, 7, 8, 11, 12, 14, 27, 31, 127, 0x85, 0xa0, 0x2028, 0x2029, 0xfeff, 0xfffe, 0xffff, 0xd800, 0xdc00, 0x202e, 0x200b, 0x200d]
        .map(n => ['cc' + n.toString(16), 'a' + ch(n) + 'b'])),
    cc0: 'a' + ch(0) + 'b', ccesc: ch(27) + '[31mred' + ch(27) + '[0m', cctab: 'a' + ch(9) + 'b', cctablead: ch(9) + "'x", ccr: 'a' + ch(13) + 'b',
};
for (const [k, v] of Object.entries(snippets)) add(k, v);

// ---- The sources ------------------------------------------------------------------------------------------
const PREFIX = ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', '',
    'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'];
const STEP_STYLE = ['<style>', ' .stepBody {', '     BackgroundColor black', '     FontColor white', '     LineColor white', ' }', '</style>'];
function sourceFor(kind, escaped) {
    switch (kind) {
        case 'body': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: GET: /x', 'note left', escaped, 'end note', 'OrdersAPI --> Caller: 200', '@enduml'];
        case 'label': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: ' + escaped, 'OrdersAPI --> Caller: 200', '@enduml'];
        case 'labellink': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: [[#iflow-abc123 ' + escaped + ']]', 'OrdersAPI --> Caller: 200', '@enduml'];
        case 'doc': case 'cell': {
            const p = [...PREFIX];
            p.splice(2, 0, ...STEP_STYLE);
            return [...p, escaped, 'Caller -[#438DD5]> OrdersAPI: GET: /x', 'OrdersAPI --> Caller: 200', '@enduml'];
        }
    }
}

// ---- Painted text -----------------------------------------------------------------------------------------
function decode(s) {
    return s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'")
        .replace(/&#(\d+);/g, (m, d) => String.fromCodePoint(+d)).replace(/&#x([0-9a-f]+);/gi, (m, h) => String.fromCodePoint(parseInt(h, 16)))
        .replace(/&nbsp;/g, ch(0xa0)).replace(/&amp;/g, '&');
}
function rowsOf(svg) {
    const rows = new Map();
    for (const m of svg.matchAll(/<text\b([^>]*)>([\s\S]*?)<\/text>/g)) {
        const x = +((m[1].match(/\bx="([\d.]+)"/) || [])[1] || 0), y = Math.round(+((m[1].match(/\by="([\d.]+)"/) || [])[1] || 0));
        if (!rows.has(y)) rows.set(y, []);
        rows.get(y).push([x, decode(m[2])]);
    }
    return [...rows.entries()].sort((a, b) => a[0] - b[0]).map(([, parts]) => parts.sort((a, b) => a[0] - b[0]).map(p => p[1]));
}
const squash = s => s.replace(/[\s\u00a0\u200b]+/g, '');
function painted(kind, svg) {
    const rows = rowsOf(svg);
    const flat = rows.map(r => r.join(' '));
    if (/^PlantUML (version|\d)/.test(flat.find(l => l.trim()) || '') || flat.some(l => /Syntax Error|Error line \d/.test(l))) return { broken: flat.slice(-2).join(' | ') };
    if (kind === 'body' || kind === 'doc') {
        const b = flat.findIndex(l => l.trim() === 'BEFORE'), a = flat.findIndex((l, i) => i > b && l.trim() === 'AFTER');
        if (b < 0 || a < 0) return { cut: flat.join(' / ').slice(0, 200) };
        return { text: flat.slice(b + 1, a).join('\n') };
    }
    // One row carries BEFORE … AFTER: a label, or a table row (the data row, the last such row: the header row
    // above it reads BEFORE | H | AFTER).
    for (const r of rows.slice().reverse()) {
        const b = r.findIndex(p => p.trim() === 'BEFORE'), a = r.findIndex((p, i) => i > b && p.trim() === 'AFTER');
        if (b >= 0 && a > b) return { text: r.slice(b + 1, a).join(' ') };
        const joined = r.join(' ');
        const m = joined.match(/BEFORE ([\s\S]*) AFTER/);
        if (m) return { text: m[1] };
    }
    return { cut: flat.join(' / ').slice(0, 200) };
}
function expected(kind, raw) {
    if (kind === 'cell') return raw.replace(/\r\n|\r|\n/g, ' ').trim();
    return raw.replace(/\r/g, '');
}

// ---- Run --------------------------------------------------------------------------------------------------
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'kfuzz-'));
const cases = [];
for (const kind of kinds)
    for (const [name, raw] of raws)
        if (!filter || name.includes(filter)) cases.push({ id: kind + '__' + name, kind: kind === 'labellink' ? 'label' : kind, view: kind, raw });
fs.writeFileSync(path.join(tmp, 'cases.json'), JSON.stringify(cases.map(c => ({ id: c.id, kind: c.kind, codes: [...Array(c.raw.length).keys()].map(i => c.raw.charCodeAt(i)) }))));
const esc = cp.spawnSync('dotnet', ['fsi', path.join(HERE, 'escape-oracle.fsx'), dll, path.join(tmp, 'cases.json'), path.join(tmp, 'escaped.json')], { encoding: 'utf8', maxBuffer: 1 << 26 });
if (esc.status !== 0) { console.log('escape-oracle failed: ' + esc.stdout + esc.stderr); process.exit(1); }
const escaped = new Map(JSON.parse(fs.readFileSync(path.join(tmp, 'escaped.json'), 'utf8')).map(e => [e.id, String.fromCharCode(...e.codes)]));
for (const c of cases) { c.escaped = escaped.get(c.id); c.source = sourceFor(c.view, c.escaped).join('\n'); }
if (process.env.SOURCES_OUT) {
    fs.mkdirSync(process.env.SOURCES_OUT, { recursive: true });
    for (const c of cases) fs.writeFileSync(path.join(process.env.SOURCES_OUT, c.id + '.puml'), c.source);
    fs.writeFileSync(path.join(process.env.SOURCES_OUT, 'cases.json'), JSON.stringify(cases));
    console.log('wrote ' + cases.length + ' sources');
    process.exit(0);
}
const started = Date.now();
let results;
if (process.env.SVG_IN) {
    results = new Map(cases.map(c => {
        const s = path.join(process.env.SVG_IN, c.id + '.svg'), e = path.join(process.env.SVG_IN, c.id + '.err');
        return [c.id, fs.existsSync(s) ? { svg: fs.readFileSync(s, 'utf8') } : fs.existsSync(e) ? { error: fs.readFileSync(e, 'utf8') } : {}];
    }));
} else {
    const input = cases.map(c => JSON.stringify({ id: c.id, source: c.source }).replace(/[\u2028\u2029]/g, m => '\\u' + m.charCodeAt(0).toString(16))).join('\n') + '\n';
    const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'], { input, maxBuffer: 1 << 30, timeout: 3600000 });
    if (r.status !== 0) console.log('renderer exit ' + r.status + ': ' + (r.stderr || '').toString().slice(0, 400));
    results = new Map((r.stdout || '').toString().split('\n').filter(Boolean).map(l => JSON.parse(l)).map(o => [o.id, o]));
}
let ok = 0;
const bad = [];
for (const c of cases) {
    const res = results.get(c.id) || {};
    const want = expected(c.kind, c.raw);
    let verdict;
    if (c.escaped.startsWith('!!EXCEPTION')) verdict = 'ESCAPER THREW ' + c.escaped;
    else if (res.error) verdict = 'ERROR ' + res.error.slice(0, 120);
    else if (!res.svg) verdict = 'NO RESULT';
    else {
        const p = painted(c.view, res.svg);
        if (p.broken) verdict = 'BROKEN ' + p.broken.slice(0, 120);
        else if (p.cut) verdict = 'CUT ' + p.cut;
        else if (squash(p.text) === squash(want)) { ok++; continue; }
        else verdict = 'PAINTS ' + JSON.stringify(p.text);
    }
    bad.push(c.view.padEnd(9) + ' ' + c.id.split('__')[1].padEnd(12) + ' ' + JSON.stringify(c.raw).padEnd(28) + ' -> ' + JSON.stringify(c.escaped.length > 160 ? c.escaped.slice(0, 160) + '…' : c.escaped).padEnd(34) + ' => ' + verdict);
}
console.log(`engine ${path.basename(C)}${process.env.SVG_IN ? ' (SVGs from ' + process.env.SVG_IN + ')' : ''}; ${cases.length} cases in ${Date.now() - started} ms; ${ok} drawn as captured, ${bad.length} not\n`);
for (const b of bad) console.log(b);
