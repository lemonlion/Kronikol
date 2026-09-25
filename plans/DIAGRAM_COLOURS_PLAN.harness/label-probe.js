'use strict';
// DIAGRAM_COLOURS_PLAN harness (audit, 3.30.2): request labels carrying captured URL paths, with internal
// flow tracking off (a plain label), through the pinned engine with Kronikol's prefix. Prints the painted
// text of each label (the texts between the arrow's autonumber and the next one) and any text the engine
// styled. SAME means the label painted as written. Pass labels as arguments to try others.
// Usage (from the repo root): node plans/DIAGRAM_COLOURS_PLAN.harness/label-probe.js [label ...]
const cp = require('child_process'), path = require('path');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const REPO = path.resolve(__dirname, '../..');
const RENDERER = path.join(REPO, 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const labels = process.argv.slice(2).length ? process.argv.slice(2) : [
  'GET: /users/~john/files', 'GET: /~/x', 'GET: /api/__internal__/x', 'GET: /posts/my--first--post',
  'GET: /static/**/*.js', 'GET: //cdn//x', 'GET: /a/~~b~~', 'GET: /x?q=a%20b', 'GET: /x/%date()',
  'GET: /p?filter[a]=1', 'GET: /wiki/==Title==', 'GET: /a/..b..', 'Publish: my__topic__v1'];
const lines = labels.map((l, i) => JSON.stringify({ id: 'L' + i, source: ['@startuml', '!pragma teoz true',
  'skinparam wrapWidth 800', 'autonumber 1', 'actor "Caller" as caller', 'participant "Svc" as svc',
  'caller -[#438DD5]> svc: ' + l, 'svc -[#438DD5]-> caller: 200 OK', '@enduml'].join('\n') })).join('\n') + '\n';
const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'], { input: lines, encoding: 'utf8', maxBuffer: 1 << 28 });
if (r.status !== 0 && !r.stdout) { console.log('renderer failed', r.status, r.stderr.slice(0, 500)); process.exit(1); }
const out = r.stdout.trim().split('\n').map(l => { try { return JSON.parse(l); } catch { return null; } }).filter(Boolean);
for (const o of out) {
  const i = +String(o.id).slice(1); const svg = o.svg || '';
  const texts = [...svg.matchAll(/<text[^>]*>([^<]*)<\/text>/g)].map(m => m[1]);
  const dec = s => s.replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&quot;/g,'"').replace(/&amp;/g,'&').replace(/&#160;|\u00a0/g,' ');
  const styled = [...svg.matchAll(/<text([^>]*)>([^<]*)<\/text>/g)].filter(m => /font-weight="bold"|font-style="italic"|text-decoration/.test(m[1])).map(m => dec(m[2]));
  const i1 = texts.indexOf('1'), i2 = texts.indexOf('2');
  const painted = (i1 >= 0 && i2 > i1 ? texts.slice(i1 + 1, i2) : texts).map(dec).join('');
  console.log((painted === labels[i].replace(/ /g,'') || painted === labels[i] ? 'SAME   ' : 'DIFFERS') + '  ' + JSON.stringify(labels[i]) + '  ->  ' + JSON.stringify(painted) + (styled.length ? '  styled: ' + JSON.stringify(styled) : '') + (o.error ? ' ERROR ' + o.error.slice(0,120) : ''));
}
