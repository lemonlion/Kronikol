'use strict';
// DIAGRAM_COLOURS_PLAN harness (audit, 3.30.2): candidate sources for the render-error placeholder, on the
// pinned engine through the Node renderer. `current` is the placeholder to 3.30.1, `transparent` the form
// 3.30.2 writes. SOURCES_OUT=<dir> writes each source to <dir>/<id>.puml and stops (for ikvm-render.cs,
// the Java engine); SVG_IN=<dir> reads <dir>/<id>.svg instead of rendering.
// Usage (from the repo root): node plans/DIAGRAM_COLOURS_PLAN.harness/placeholder-probe.js
const cp = require('child_process'), path = require('path'), fs = require('fs');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const REPO = path.resolve(__dirname, '../..');
const RENDERER = path.join(REPO, 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const msg = '\u26a0 diagram could not be generated: TimeoutException: no answer';
const cases = {
  current: ['@startuml', 'hnote across <<renderError>> #ffdddd', msg, 'end note', '@enduml'],
  transparent: ['@startuml', 'hide footbox', 'skinparam ParticipantBorderColor transparent', 'skinparam ParticipantBackgroundColor transparent',
    'skinparam LifeLineBorderColor transparent', 'participant " " as renderError', 'hnote across <<renderError>> #ffdddd', msg, 'end note', '@enduml'],
  over: ['@startuml', 'hide footbox', 'participant "Kronikol" as renderError', 'hnote over renderError <<renderError>> #ffdddd', msg, 'end note', '@enduml'],
};
if (process.env.SOURCES_OUT) {
  fs.mkdirSync(process.env.SOURCES_OUT, { recursive: true });
  for (const [id, lines] of Object.entries(cases)) fs.writeFileSync(path.join(process.env.SOURCES_OUT, id + '.puml'), lines.join('\n'));
  process.exit(0);
}
const input = Object.entries(cases).map(([id, lines]) => JSON.stringify({ id, source: lines.join('\n') })).join('\n') + '\n';
const r = process.env.SVG_IN
  ? { stdout: Object.keys(cases).map(id => JSON.stringify({ id, svg: fs.readFileSync(path.join(process.env.SVG_IN, id + '.svg'), 'utf8') })).join(String.fromCharCode(10)) }
  : cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'], { input, encoding: 'utf8' });
for (const line of r.stdout.trim().split('\n')) {
  const o = JSON.parse(line);
  const texts = [...(o.svg || '').matchAll(/<text[^>]*>([^<]*)<\/text>/g)].map(m => m[1]);
  const vb = ((o.svg || '').match(/viewBox="([^"]+)"/) || [])[1];
  console.log(o.id.padEnd(12), (texts[0] || '').startsWith('PlantUML') ? 'ERROR PICTURE' : 'drawn', 'viewBox', vb, JSON.stringify(texts.slice(0, 6)));
}
