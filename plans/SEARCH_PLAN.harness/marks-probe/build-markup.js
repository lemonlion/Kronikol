// Builds markup.html from capture.json: the two diagrams drawn on the live demo report (Kronikol 5.4.1), the CSS rules
// that matched them there, the theme variables, a synthetic block of Java-PlantUML-like <text> with textLength /
// lengthAdjust, and a plain HTML control paragraph. Every engine then probes identical markup.
const fs = require('fs');
const path = require('path');
const cap = JSON.parse(fs.readFileSync(path.join(__dirname, 'capture.json'), 'utf8'));

const vars = Object.assign({}, cap.diagrams.muffin.vars, cap.diagrams.openapi.vars);
const seen = new Set();
const rules = [];
for (const d of Object.values(cap.diagrams)) {
  for (const r of d.rules) {
    const key = (r.media || '') + '|' + r.sel + '|' + r.css;
    if (seen.has(key)) continue;
    seen.add(key);
    rules.push(r.media ? `@media ${r.media} { ${r.sel} { ${r.css} } }` : `${r.sel} { ${r.css} }`);
  }
}

const L = '"milk": "Some_Fresh_Milk"';
const synth = [
  ['s0', null, null, 'system-ui'],
  ['s1', 200, 'spacing', 'system-ui'],
  ['s2', 200, 'spacingAndGlyphs', 'system-ui'],
  ['s3', 130, 'spacing', 'system-ui'],
  ['s4', 130, 'spacingAndGlyphs', 'system-ui'],
  ['s5', 150, 'spacing', 'system-ui'],
  ['s6', 150, 'spacing', 'sans-serif'],
].map(([id, tl, la, ff], i) => `<text id="${id}" x="10" y="${30 + i * 30}" font-size="13" fill="#000000" xml:space="preserve" style="white-space: pre" font-family="${ff}"${tl ? ` textLength="${tl}" lengthAdjust="${la}"` : ''}>${L.replace(/"/g, '&quot;')}</text>` +
  `<text x="260" y="${30 + i * 30}" font-size="11" fill="#666666" font-family="monospace">${id}: ${tl ? `textLength=${tl} lengthAdjust=${la}` : 'no textLength'} font-family=${ff}</text>`).join('\n');

const html = `<!doctype html>
<html><head><meta charset="utf-8"><title>Highlight probe markup</title>
<style>
:root { ${Object.entries(vars).map(([k, v]) => `${k}: ${v};`).join(' ')} }
html, body { margin: 0; background: #ffffff; }
body { font-family: system-ui; }
.stage { width: 1300px; margin: 0 auto; padding: 8px 0 400px; }
.label { font: 12px monospace; color: #555; margin: 12px 16px 4px; }
#ctl { font: 13px system-ui; margin: 8px 16px; }
${rules.join('\n')}
</style></head>
<body><div class="report-panels"><div class="stage">
<div class="label">#ctl: plain HTML control</div>
<p id="ctl">Control paragraph in plain HTML: ${L.replace(/"/g, '&quot;')}</p>
<div class="label">#d3: synthetic Java-PlantUML-like text, one &lt;text&gt; per line</div>
<div id="d3" class="plantuml-browser"><svg xmlns="http://www.w3.org/2000/svg" version="1.1" viewBox="0 0 700 230" width="700" height="230"><rect x="0" y="0" width="700" height="230" fill="#fdf6e4"></rect>
${synth}
</svg></div>
<div class="label">#d1: ${cap.diagrams.muffin.scenario} (${cap.diagrams.muffin.containerId}), JSON payloads, expanded</div>
<div id="d1" class="plantuml-browser">${cap.diagrams.muffin.svg}</div>
<div class="label">#d2: ${cap.diagrams.openapi.scenario} (${cap.diagrams.openapi.containerId}), long-note foreignObject box</div>
<div id="d2" class="plantuml-browser">${cap.diagrams.openapi.svg}</div>
</div></div></body></html>
`;
fs.writeFileSync(path.join(__dirname, 'markup.html'), html);
console.log('markup.html', html.length, 'bytes;', rules.length, 'rules;', Object.keys(vars).length, 'vars; source', cap.meta.generator);
