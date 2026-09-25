'use strict';
// DIAGRAM_COLOURS_PLAN harness (third pass): the engine's script loader on the report's REAL render path.
//
// The pinned engine loads four things by appending <script src=…> to document.head and waiting for
// onload/onerror (its loader EK_, called through AUj): themes.js (!theme), openiconic.js (<&name>),
// emoji.js (<:name:>) and a stdlib module (!include <lib/…>). The report's worker host answers none of
// them. This probe builds the page the way worker-probe.js does (the shipped render script, the worker
// host inlined, the pinned engine from the CDN in a Blob worker) and renders one of two sets:
//
//   SET=payload (default)  sources written by Kronikol's own emitter (emit-payload-sources.fsx):
//     d-control   payload-control.puml   a plain call
//     d-rust      payload-rust.puml      a 400 whose JSON body quotes Vec<&str>   (OpenIconic syntax)
//     d-emoji     payload-emoji.puml     a request body carrying <:rocket:>      (emoji syntax)
//     d-rust-esc  payload-rust.puml with <&str> written ~<&str>, what an escaper fix would emit
//     d-emoji-esc payload-emoji.puml with <:rocket:> written ~<:rocket:>
//     d-after     a trivial diagram queued behind them
//   SET=allthemed  six sources that all carry !theme cerulean, the way a report with PlantUmlTheme set
//                  does (every source carries the directive): does ANY diagram get drawn?
//   SET=poison     the payload diagram FIRST, three plain ones behind it: is a hang contained?
//   SET=corpus     the render-bench corpus (22 diagrams); prints a hash per SVG instead of writing files,
//                  so a stock run and a FAILFAST=1 run can be compared
//
// FAILFAST=1 patches the inlined host with the plan's §5.3 hook (a <script> appended to head gets its
// onerror on the next tick). Writes loader-<set>-<id>[.failfast].svg for every SVG and prints each
// diagram's outcome, what an element holds when it has no SVG, the text the SVG paints on its note
// lines, the page console, and the shim's telemetry.
// Usage (from anywhere): node plans/DIAGRAM_COLOURS_PLAN.harness/loader-probe.js
// Needs the E2E project built once (its Playwright driver) and network access to the CDN.
const fs = require('fs'), path = require('path'), url = require('url');
const REPO = path.resolve(__dirname, '../..');
const HERE = path.resolve(__dirname);
const pw = require(REPO + '/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const SET = process.env.SET || 'payload';
const FAILFAST = process.env.FAILFAST === '1';

function res(name) { return fs.readFileSync(REPO + '/src/Kronikol/Reports/' + name, 'utf8'); }
function hostSource() {
    let h = res('plantuml-worker-host.js');
    if (!FAILFAST) return h;
    const anchor = "mockDocument.head = new MockElement('head', null); mockDocument.head.ownerDocument = mockDocument;";
    if (h.indexOf(anchor) < 0) throw new Error('worker host anchor not found');
    return h.replace(anchor, anchor + " mockDocument.head._onAppend = function (c) { if (c && c.tagName === 'script' && typeof c.onerror === 'function') setTimeout(function () { c.onerror(new Error('scripts cannot load in this worker')); }, 0); };");
}
const cdnBase = /PlantUmlJsCdnBase = "([^"]+)"/.exec(fs.readFileSync(REPO + '/src/Kronikol/Constants/TrackingDefaults.cs', 'utf8'))[1];
const hostLiteral = JSON.stringify(hostSource()).replace(/</g, '\\u003C').replace(/>/g, '\\u003E').replace(/&/g, '\\u0026');
const renderScript = '<script>' + res('report-decompress-helper.js') + '</script>' + res('plantuml-browser-render-script.js')
    .replace('__BROWSER_RENDER_WORKERS__', '4')
    .replace('__BROWSER_RENDER_CACHE_MB__', '64')
    .replace('__BROWSER_FRAGMENT_MAX_HEIGHT__', '12000')
    .split('__PLANTUML_CDN_BASE__').join(cdnBase)
    .replace('__PLANTUML_WORKER_HOST_SOURCE__', hostLiteral);
const enc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const src = f => fs.readFileSync(path.join(HERE, f), 'utf8').replace(/\r\n/g, '\n');

let diagrams;
if (SET === 'corpus') {
    // The render-bench corpus corpus-hash.js uses (tools/render-bench/real: every .puml, plus the first ten
    // entries of many-small), through the shipped worker path: run stock and FAILFAST=1 and compare the
    // printed hashes. Does the hook change any render that loads nothing?
    const real = path.join(REPO, 'tools/render-bench/real');
    const files = fs.readdirSync(real).filter(f => f.endsWith('.puml')).map(f => path.join(real, f));
    const small = path.join(real, 'many-small');
    if (fs.existsSync(small)) fs.readdirSync(small).slice(0, 10).filter(f => f.endsWith('.puml')).forEach(f => files.push(path.join(small, f)));
    diagrams = files.map((f, i) => ({ id: 'd-c' + i + '-' + path.basename(f, '.puml').replace(/[^a-z0-9-]/gi, ''), src: fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n') }));
} else if (SET === 'poison') {
    // The payload diagram FIRST: only one worker exists until a render completes, so everything queues
    // behind it. Does that worker render anything after its 150 s host timeout frees it?
    diagrams = [
        { id: 'd-rust', src: src('payload-rust.puml') },
        { id: 'd-control', src: src('payload-control.puml') },
        { id: 'd-after', src: '@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : after\n@enduml' },
        { id: 'd-control2', src: src('payload-control.puml').replace('A1', 'B2') },
    ];
} else if (SET === 'statement') {
    // Found during I3: the message statement at PlantUmlStatementLimits.MaxMessageStatementChars (2000), in
    // the emitter's own shape (payload-longpath.puml, a GET whose query is capped there), cut to each length
    // with the cap's ellipsis, with and without the internal-flow link around the label. Which lengths does
    // the shipped worker draw? (Node draws the full 2000.)
    const base = src('payload-longpath.puml');
    const st = base.split('\n').find(l => l.includes('[[#iflow-'));
    const m = /^(.*?: )\[\[#iflow-[0-9a-f-]+ (.*)\]\]$/.exec(st);
    if (!m) throw new Error('statement shape not found');
    // The captured label stops at the cap; lengthen it with more query pairs so every cut is a real cut.
    const label = m[2].replace(/…$/, '') + Array.from({ length: 60 }, (_, i) => '&extra' + i + '=value' + i).join('');
    const cut = (s, n) => s.slice(0, n - 1) + '…';
    const withLink = n => { const head = st.slice(0, st.indexOf(m[2])); return head + cut(label, n - head.length - 2) + ']]'; };
    const noLink = n => m[1] + cut(label, n - m[1].length);
    diagrams = [];
    for (const n of [1000, 1200, 1400, 1500, 1600, 1700, 1800, 1900, 2000]) diagrams.push({ id: 'd-link' + n, src: base.replace(st, withLink(n)) });
    for (const n of [1500, 1800, 2000]) diagrams.push({ id: 'd-nolink' + n, src: base.replace(st, noLink(n)) });
    diagrams.forEach(d => { const l = d.src.split('\n').find(x => x.startsWith(m[1])); if (Number(d.id.replace(/\D/g, '')) !== l.length) throw new Error(d.id + ' is ' + l.length); });
} else if (SET === 'allthemed') {
    diagrams = [1, 2, 3, 4, 5, 6].map(i => ({ id: 'd-themed' + i,
        src: '@startuml\n!theme cerulean\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : themed call ' + i + '\nnote left\n<color:gray>[k=v]\n{ "n": ' + i + ' }\nend note\n@enduml' }));
} else {
    diagrams = [
        { id: 'd-control', src: src('payload-control.puml') },
        { id: 'd-rust', src: src('payload-rust.puml') },
        { id: 'd-emoji', src: src('payload-emoji.puml') },
        { id: 'd-rust-esc', src: src('payload-rust.puml').replace('Vec<&str>', 'Vec~<&str>') },
        { id: 'd-emoji-esc', src: src('payload-emoji.puml').replace('<:rocket:>', '~<:rocket:>') },
        { id: 'd-after', src: '@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : after\n@enduml' },
    ];
}
const html = `<!DOCTYPE html><html><head><title>loader probe</title>
${renderScript}
</head><body>
${diagrams.map(d => `<div class="plantuml-browser" id="${d.id}" data-plantuml="${enc(d.src)}" data-diagram-type="plantuml"></div>`).join('\n')}
</body></html>`;
const pagePath = path.join(require('os').tmpdir(), 'loader-probe-' + SET + (FAILFAST ? '.failfast' : '') + '.page.html');
fs.writeFileSync(pagePath, html);

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await browser.newPage();
    const consoleLines = [];
    page.on('console', m => { consoleLines.push('+' + (Date.now() - t0) + ' ms [' + m.type() + '] ' + m.text().slice(0, 300)); });
    page.on('pageerror', e => console.log('PAGEERROR ' + e.message));
    const t0 = Date.now();
    await page.goto(url.pathToFileURL(pagePath).href, { waitUntil: 'load' });
    const budget = FAILFAST ? 60000 : (SET === 'payload' ? 200000 : 400000);
    const seen = {};
    while (Date.now() - t0 < budget) {
        const state = await page.evaluate(ids => ids.map(id => { const el = document.getElementById(id); return [id, el.dataset.rendered === '1', !!el.querySelector('svg'), (el.textContent || '').trim().length]; }), diagrams.map(d => d.id));
        for (const [id, rendered, hasSvg, textLen] of state) {
            const key = id + (rendered ? 'R' : '') + (hasSvg ? 'S' : '') + (textLen ? 'T' : '');
            if (!seen[key] && (rendered || hasSvg || textLen)) { seen[key] = 1; console.log('+' + (Date.now() - t0) + ' ms ' + id + ' rendered=' + rendered + ' svg=' + hasSvg + ' textLen=' + textLen); }
        }
        if (state.every(s => s[1] || s[2])) break;
        await page.waitForTimeout(1000);
    }
    const report = await page.evaluate(ids => {
        const out = { telemetry: window.__kronikolRender, diagrams: {} };
        for (const id of ids) {
            const el = document.getElementById(id);
            const svg = el.querySelector('svg');
            const texts = svg ? Array.from(svg.querySelectorAll('text')).map(t => t.textContent) : null;
            out.diagrams[id] = { rendered: el.dataset.rendered || null, hasSvg: !!svg, viewBox: svg && svg.getAttribute('viewBox'),
                noteTexts: texts && texts.filter(t => /Vec|str|rocket|shipped|error|<|\u003E|themed|\{|\}/.test(t)),
                heldText: svg ? null : (el.textContent || '').trim().slice(0, 300), svg: svg ? svg.outerHTML : null, inner: el.innerHTML };
        }
        return out;
    }, diagrams.map(d => d.id));
    console.log('ELAPSED ' + (Date.now() - t0) + ' ms');
    for (const d of diagrams) {
        const r = report.diagrams[d.id];
        if (SET === 'corpus') {
            // Hashes, not files: the corpus is 22 diagrams, one of them 500 messages long.
            r.sha256 = r.svg ? require('crypto').createHash('sha256').update(r.inner).digest('hex').slice(0, 16) : null; r.svgCount = (r.inner.match(/<svg/g) || []).length;
            delete r.noteTexts;
        } else if (SET === 'statement') {
            delete r.noteTexts;
        } else if (r.svg) fs.writeFileSync(path.join(HERE, 'loader-' + SET + '-' + d.id.slice(2) + (FAILFAST ? '.failfast' : '') + '.svg'), r.svg);
        delete r.svg; delete r.inner;
    }
    console.log('TELEMETRY ' + JSON.stringify(report.telemetry));
    console.log('DIAGRAMS ' + JSON.stringify(report.diagrams, null, 1));
    console.log('CONSOLE (' + consoleLines.length + ')');
    consoleLines.forEach(l => console.log('  ' + l));
    await browser.close();
})().catch(e => { console.error('PROBE FAILED ' + (e && e.stack || e)); process.exit(1); });
