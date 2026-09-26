'use strict';
// Second P3 audit: candidate escapes, written by hand, in each context the emitter writes captured text into.
// Each case is [context, escaped text, what should paint]. Contexts: body (a payload note line), label (a request
// label, tracking off), link (the same inside [[#iflow-… ]]), doc (a step bar's doc string line, the rich bar form).
// Usage: node cand-probe.js [cases.json]  (default: the list below). Prints what each paints.
const fs = require('fs'), path = require('path'), cp = require('child_process');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const RENDERER = process.env.RENDERER || path.join(path.resolve(__dirname, '..', '..'), 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const cases = process.argv[2] ? JSON.parse(fs.readFileSync(process.argv[2], 'utf8')) : [];
const PREFIX = ['@startuml', '!pragma teoz true', '<style>', ' .stepBody {', '     BackgroundColor black', '     FontColor white', '     LineColor white', ' }', '</style>',
    'skinparam wrapWidth 800', 'autonumber 1', '', 'participant "Caller" as Caller', 'participant "Orders API" as OrdersAPI'];
function source(ctx, text) {
    switch (ctx) {
        case 'body': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: GET: /x', 'note left', 'BEFORE', text, 'AFTER', 'end note', 'OrdersAPI --> Caller: 200', '@enduml'];
        case 'label': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: BEFORE ' + text + ' AFTER', 'OrdersAPI --> Caller: 200', '@enduml'];
        case 'link': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: [[#iflow-abc BEFORE ' + text + ' AFTER]]', 'OrdersAPI --> Caller: 200', '@enduml'];
        case 'doc': return [...PREFIX, 'hnote across <<stepDelimiter>><<stepBody>>: Given x\\n\\nBEFORE\\n' + text + '\\nAFTER\\n', 'Caller -[#438DD5]> OrdersAPI: GET: /x', '@enduml'];
        case 'cell': return [...PREFIX, 'hnote across <<stepDelimiter>><<stepBody>>: Given x\\n\\n|= H |\\n| BEFORE ' + text + ' AFTER |\\n', 'Caller -[#438DD5]> OrdersAPI: GET: /x', '@enduml'];
        case 'bar': return [...PREFIX, 'hnote across <<stepDelimiter>> #black:<color:white>BEFORE ' + text + ' AFTER', 'Caller -[#438DD5]> OrdersAPI: GET: /x', '@enduml'];
        case 'hnote': return [...PREFIX, 'Caller -[#438DD5]> OrdersAPI: GET: /x', 'hnote across <<assertionNote>> #e6ffe6', 'BEFORE', text, 'AFTER', 'end note', '@enduml'];
    }
}
function decode(s) {
    return s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'")
        .replace(/&#(\d+);/g, (m, d) => String.fromCodePoint(+d)).replace(/&#x([0-9a-f]+);/gi, (m, h) => String.fromCodePoint(parseInt(h, 16)))
        .replace(/&nbsp;/g, '\u00a0').replace(/&amp;/g, '&');
}
function painted(svg) {
    const rows = new Map();
    for (const m of svg.matchAll(/<text\b([^>]*)>([\s\S]*?)<\/text>/g)) {
        const x = +((m[1].match(/\bx="([\d.]+)"/) || [])[1] || 0), y = Math.round(+((m[1].match(/\by="([\d.]+)"/) || [])[1] || 0));
        const fill = (m[1].match(/\bfill="([^"]*)"/) || [])[1] || '';
        if (!rows.has(y)) rows.set(y, []);
        rows.get(y).push([x, decode(m[2]), fill]);
    }
    const flat = [...rows.entries()].sort((a, b) => a[0] - b[0]).map(([, p]) => p.sort((a, b) => a[0] - b[0]));
    const all = flat.map(r => r.map(p => p[1]).join(''));
    if (/^PlantUML/.test((all.find(l => l.trim()) || '').trim()) || all.some(l => /Syntax Error/.test(l))) return 'BROKEN';
    const joined = all.join('\n');
    const m = joined.match(/BEFORE([\s\S]*?)AFTER/);
    return m ? m[1].replace(/\u200b/g, '\u2423').trim() : 'CUT ' + all.join(' / ').slice(0, 120);
}
const input = cases.map((c, i) => JSON.stringify({ id: String(i), source: source(c[0], c[1]).join('\n') }).replace(/[\u2028\u2029]/g, m => '\\u' + m.charCodeAt(0).toString(16))).join('\n') + '\n';
// SOURCES_OUT=<dir> writes <i>.puml and stops; SVG_IN=<dir> reads <i>.svg / <i>.err drawn elsewhere (ikvm-render.cs).
if (process.env.SOURCES_OUT) {
    fs.mkdirSync(process.env.SOURCES_OUT, { recursive: true });
    cases.forEach((c, i) => fs.writeFileSync(path.join(process.env.SOURCES_OUT, i + '.puml'), source(c[0], c[1]).join('\n')));
    console.log('wrote ' + cases.length + ' sources');
    process.exit(0);
}
let out;
if (process.env.SVG_IN) {
    out = new Map(cases.map((c, i) => {
        const svg = path.join(process.env.SVG_IN, i + '.svg'), err = path.join(process.env.SVG_IN, i + '.err');
        return [String(i), fs.existsSync(svg) ? { svg: fs.readFileSync(svg, 'utf8') } : { error: fs.existsSync(err) ? fs.readFileSync(err, 'utf8') : 'no result' }];
    }));
} else {
    const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'], { input, maxBuffer: 1 << 28 });
    out = new Map((r.stdout || '').toString().split('\n').filter(Boolean).map(l => JSON.parse(l)).map(o => [o.id, o]));
}
cases.forEach((c, i) => {
    const res = out.get(String(i)) || {};
    const got = res.svg ? painted(res.svg) : 'ERROR ' + (res.error || '').slice(0, 80);
    const ok = got.replace(/\u2423/g, '').replace(/\s+/g, '') === c[2].replace(/\s+/g, '');
    console.log((ok ? 'ok   ' : 'DIFF ') + c[0].padEnd(6) + (c[3] ? c[3].padEnd(12) : '') + JSON.stringify(c[1]).padEnd(46) + ' paints ' + JSON.stringify(got) + (ok ? '' : '  (want ' + JSON.stringify(c[2]) + ')'));
});
