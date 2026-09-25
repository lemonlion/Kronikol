'use strict';
// DIAGRAM_COLOURS_PLAN harness — the report's REAL render path, not the bench shell.
//
// Builds a page the way TestPageGenerator.GenerateBrowserJsSequenceDiagramPage does: the shipped
// plantuml-browser-render-script.js with the same placeholder substitutions DiagramContextMenu makes
// (worker host inlined, CDN base from TrackingDefaults.cs), opened from file:// in headless Chromium,
// so the engine is the pinned CDN build running in the shim's Blob Web Worker. Three diagrams:
//   d-plain   probe.puml (an internal-flow link, a <color:blue> field, a hyperlink) with a truthy
//             window.__iflowSegments entry, so bindIflowLinks takes the blue-text path (R8 → RUN, I1)
//   d-themed  probe-themed.puml (!theme cerulean): what a theme does in the worker (R1 in a worker, I5)
//   d-after   a trivial diagram queued behind the themed one: does the queue survive it
//   d-include include-probe.puml (!include <C4/C4_Context>): the stdlib loader takes the same script-tag path
// Prints every console message the page surfaces (worker messages included, if Playwright forwards
// them), the shim's telemetry, each diagram's outcome, and the link/focus fills before and after a
// mouseenter/mouseleave on the link text. Writes worker-<id>.svg beside this file for each SVG.
// Usage (from anywhere): node plans/DIAGRAM_COLOURS_PLAN.harness/worker-probe.js
// Needs the E2E project built once (its Playwright driver) and network access to the CDN.
const fs = require('fs'), path = require('path'), url = require('url');
const REPO = 'C:/Code/Kronikol';
const HERE = path.resolve(__dirname);
const pw = require(REPO + '/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');

function res(name) { return fs.readFileSync(REPO + '/src/Kronikol/Reports/' + name, 'utf8'); }
// FAILFAST=1 prototypes the S3 hardening: the worker host's mock <head> answers a <script> append with
// onerror on the next tick, the way a real document answers a 404, so the engine's theme loader fails
// fast and the engine warns and renders unthemed instead of waiting for a load that never completes.
const FAILFAST = process.env.FAILFAST === '1';
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
const after = '@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : after\n@enduml';
const html = `<!DOCTYPE html><html><head><title>worker probe</title>
<script>window.__iflowSegments = { 'iflow-abc123': { html: '<p>segment</p>' } }; window.__iflowConfig = { hasDataBehavior: 'showLinkOnHover' };</script>
${renderScript}
</head><body>
<div class="plantuml-browser" id="d-plain" data-plantuml="${enc(src('probe.puml'))}" data-diagram-type="plantuml"></div>
<div class="plantuml-browser" id="d-themed" data-plantuml="${enc(src('probe-themed.puml'))}" data-diagram-type="plantuml"></div>
<div class="plantuml-browser" id="d-after" data-plantuml="${enc(after)}" data-diagram-type="plantuml"></div>
<div class="plantuml-browser" id="d-include" data-plantuml="${enc(src('include-probe.puml'))}" data-diagram-type="plantuml"></div>
</body></html>`;
const pagePath = path.join(require('os').tmpdir(), FAILFAST ? 'worker-probe.failfast.page.html' : 'worker-probe.page.html');
fs.writeFileSync(pagePath, html);

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await browser.newPage();
    const consoleLines = [];
    page.on('console', m => { const line = '[' + m.type() + '] ' + m.text().slice(0, 300); consoleLines.push(line); if (m.type() !== 'log' && m.type() !== 'debug') console.log('CONSOLE ' + line); });
    page.on('pageerror', e => console.log('PAGEERROR ' + e.message));
    page.on('worker', w => console.log('WORKER started ' + w.url().slice(0, 40)));
    const t0 = Date.now();
    await page.goto(url.pathToFileURL(pagePath).href, { waitUntil: 'load' });
    const ids = ['d-plain', 'd-themed', 'd-after', 'd-include'];
    for (const id of ids) {
        const deadline = FAILFAST ? 120000 : id === 'd-themed' ? 200000 : id === 'd-include' ? 70000 : 120000;
        try {
            await page.waitForFunction(id => { const el = document.getElementById(id); return el && el.dataset.rendered === '1'; }, id, { timeout: deadline, polling: 200 });
            console.log('RENDERED ' + id + ' at +' + (Date.now() - t0) + ' ms');
        } catch (e) { console.log('NOT RENDERED ' + id + ' within ' + deadline + ' ms'); }
    }
    const report = await page.evaluate(() => {
        const out = { telemetry: window.__kronikolRender, diagrams: {} };
        for (const id of ['d-plain', 'd-themed', 'd-after', 'd-include']) {
            const el = document.getElementById(id);
            const svg = el.querySelector('svg');
            const strokes = {};
            if (svg) svg.querySelectorAll('line').forEach(l => { const s = l.getAttribute('stroke'); strokes[s] = (strokes[s] || 0) + 1; });
            out.diagrams[id] = { rendered: el.dataset.rendered, hasSvg: !!svg, text: svg ? null : el.textContent.slice(0, 200), lineStrokes: strokes, viewBox: svg && svg.getAttribute('viewBox'), svg: svg ? svg.outerHTML : null };
        }
        const plain = document.getElementById('d-plain');
        const fills = () => { const r = {}; plain.querySelectorAll('text').forEach(t => { const c = t.textContent; if (['GET', '/orders', '"focused":', 'docs', '[accept=application/json]', '"other":', '{'].includes(c)) r[c] = t.getAttribute('fill') + (t.getAttribute('text-decoration') ? ' ' + t.getAttribute('text-decoration') : ''); }); return r; };
        out.afterBind = fills();
        const get = Array.from(plain.querySelectorAll('text')).find(t => t.textContent === 'GET');
        if (get) {
            get.dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));
            out.afterMouseEnter = fills();
            get.dispatchEvent(new MouseEvent('mouseleave', { bubbles: false }));
            out.afterMouseLeave = fills();
        }
        out.textOrder = Array.from(plain.querySelectorAll('text')).map(t => t.getAttribute('fill') + '|' + t.textContent);
        return out;
    });
    for (const id of ids) {
        const d = report.diagrams[id];
        if (d.svg) { fs.writeFileSync(path.join(HERE, 'worker-' + id.slice(2) + (FAILFAST ? '.failfast' : '') + '.svg'), d.svg); }
        delete d.svg;
    }
    console.log('TELEMETRY ' + JSON.stringify(report.telemetry));
    console.log('DIAGRAMS ' + JSON.stringify(report.diagrams, null, 1));
    console.log('FILLS after bind       ' + JSON.stringify(report.afterBind));
    console.log('FILLS after mouseenter ' + JSON.stringify(report.afterMouseEnter));
    console.log('FILLS after mouseleave ' + JSON.stringify(report.afterMouseLeave));
    console.log('TEXT ORDER ' + JSON.stringify(report.textOrder));
    console.log('ALL CONSOLE (' + consoleLines.length + ')');
    consoleLines.forEach(l => console.log('  ' + l));
    await browser.close();
})().catch(e => { console.error('PROBE FAILED ' + (e && e.stack || e)); process.exit(1); });
