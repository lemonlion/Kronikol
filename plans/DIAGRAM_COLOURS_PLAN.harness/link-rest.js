'use strict';
// Second P3 audit: renders a source through the shipped Node renderer on the pin and applies bindIflowLinks' rest
// rule (plantuml-browser-render-script.js, restFillBefore) to the painted text, in DOM order: for each run of
// link-coloured text, the nearest earlier text fill that is not the link colour, else the most common.
// Usage: node link-rest.js <source.puml>
const fs = require('fs'), path = require('path'), cp = require('child_process');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const RENDERER = process.env.RENDERER || path.join(path.resolve(__dirname, '..', '..'), 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const src = fs.readFileSync(process.argv[2], 'utf8');
const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js')], { input: src, maxBuffer: 1 << 26 });
if (r.status !== 0) { console.log('render failed: ' + r.stderr); process.exit(1); }
const svg = r.stdout.toString();
const texts = [...svg.matchAll(/<text\b([^>]*)>([\s\S]*?)<\/text>/g)].map(m => ({ fill: ((m[1].match(/\bfill="([^"]*)"/) || [])[1] || ''), text: m[2] }));
const isLink = f => f.toLowerCase() === '#0000ff';
const counts = {};
texts.forEach(t => { if (t.fill && !isLink(t.fill)) counts[t.fill] = (counts[t.fill] || 0) + 1; });
const common = Object.entries(counts).sort((a, b) => b[1] - a[1])[0]?.[0] || '#000000';
const lum = h => { const c = [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16) / 255).map(c => c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]; };
const ratio = (a, b) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return ((x + 0.05) / (y + 0.05)).toFixed(2); };
console.log('text in DOM order (fill, content):');
texts.forEach((t, i) => console.log('  ' + String(i).padStart(2) + ' ' + t.fill.padEnd(8) + ' ' + JSON.stringify(t.text)));
console.log('\nlinks:');
for (let i = 0; i < texts.length; i++) {
    if (!isLink(texts[i].fill) || (i > 0 && isLink(texts[i - 1].fill))) continue;
    let j = i; while (j + 1 < texts.length && isLink(texts[j + 1].fill)) j++;
    let rest = null;
    for (let k = i - 1; k >= 0; k--) if (texts[k].fill && !isLink(texts[k].fill)) { rest = texts[k].fill; break; }
    rest = rest || common;
    const label = texts.slice(i, j + 1).map(t => t.text).join('');
    console.log('  ' + JSON.stringify(label).padEnd(28) + ' rests ' + rest + ' (' + ratio(rest, '#FFFFFF') + ' : 1 on white)');
    i = j;
}
