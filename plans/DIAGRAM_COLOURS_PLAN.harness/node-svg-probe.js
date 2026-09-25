'use strict';
// DIAGRAM_COLOURS_PLAN harness (third pass): is the Node renderer's SVG well-formed, and does it load
// the way a PlantUmlRendering.NodeJs report embeds it?
//
// The library's NodeJs mode renders through plantuml-render.js, whose serializeElement writes text nodes
// and attribute values unescaped, and (InlineSvgRendering off, the default) embeds each SVG as
// <img src="data:image/svg+xml;base64,…"> (DefaultDiagramsFetcher.GetNodeJsRenderedDiagrams). An image
// SVG is parsed as XML, where a bare `&` or `<` in text is fatal. For each source below this renders the
// SVG with the shipped renderer from the library's cache, then in headless Chromium: parses it with
// DOMParser as image/svg+xml, and loads it as the data-URI <img> the report would carry.
//   payload-control.puml  nothing to escape
//   payload-amp.puml      `&` in the query string of the arrow label and in a JSON string
//   payload-xml.puml      XML bodies, painted as text starting with `<`
// Usage (from the repo root): node plans/DIAGRAM_COLOURS_PLAN.harness/node-svg-probe.js
const fs = require('fs'), path = require('path'), cp = require('child_process'), os = require('os');
const REPO = path.resolve(__dirname, '../..');
const HERE = path.resolve(__dirname);
const pw = require(REPO + '/tests/Kronikol.Tests.EndToEnd/bin/Debug/net10.0/.playwright/package');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const RENDERER = process.env.RENDERER || path.join(C, 'plantuml-render.js');
const files = ['payload-control', 'payload-amp', 'payload-xml'];

const svgs = {};
for (const f of files) {
    const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js')],
        { input: fs.readFileSync(path.join(HERE, f + '.puml')), maxBuffer: 1 << 26 });
    svgs[f] = r.stdout.toString('utf8');
    const texts = (svgs[f].match(/<text[^>]*>[\s\S]*?<\/text>/g) || []).map(t => t.replace(/^<text[^>]*>/, '').replace(/<\/text>$/, ''));
    console.log(f + ': exit ' + r.status + ', ' + svgs[f].length + ' B; texts carrying & or <: ' + JSON.stringify(texts.filter(t => /[&<]/.test(t))));
}

(async () => {
    const browser = await pw.chromium.launch({ headless: true });
    const page = await browser.newPage();
    await page.setContent('<!DOCTYPE html><html><body></body></html>');
    for (const f of files) {
        const res = await page.evaluate(async svg => {
            const doc = new DOMParser().parseFromString(svg, 'image/svg+xml');
            const err = doc.getElementsByTagName('parsererror')[0];
            const img = new Image();
            const loaded = await new Promise(resolve => {
                img.onload = () => resolve('load');
                img.onerror = () => resolve('error');
                img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svg)));
            });
            // Inline, the way a report with InternalFlowTracking on (the default) embeds a NodeJs SVG
            // (ReportGenerator forces InlineSvgRendering for NodeJs then): the HTML parser reads it.
            const host = document.createElement('div');
            document.body.appendChild(host);
            host.innerHTML = svg;
            const inline = Array.from(host.querySelectorAll('text'))
                .filter(t => t.children.length > 0 || /[&<]|A1|true|fish|page=/.test(t.textContent))
                .map(t => ({ text: t.textContent, childElements: Array.from(t.children).map(c => c.localName), paintedWidth: Math.round(t.getComputedTextLength()) }));
            host.remove();
            return { xml: err ? 'parsererror: ' + err.textContent.replace(/\s+/g, ' ').slice(0, 160) : 'well-formed', img: loaded, naturalWidth: img.naturalWidth, inline };
        }, svgs[f]);
        console.log('  ' + f + ': ' + JSON.stringify(res));
    }
    await browser.close();
})().catch(e => { console.error('PROBE FAILED ' + (e && e.stack || e)); process.exit(1); });
