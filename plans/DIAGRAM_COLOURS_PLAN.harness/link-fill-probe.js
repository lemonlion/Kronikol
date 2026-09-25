'use strict';
// DIAGRAM_COLOURS_PLAN harness (audit, 3.30.2): is an internal-flow link whose label carries code points
// still drawn as a link? Prints each case's painted texts with their fills: a link paints #0000FF, and a
// link the engine could not read is drawn as black text, markup and all.
// Usage (from the repo root): node plans/DIAGRAM_COLOURS_PLAN.harness/link-fill-probe.js
const cp = require('child_process'), path = require('path');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const REPO = path.resolve(__dirname, '../..');
const RENDERER = path.join(REPO, 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const pre = ['@startuml', '!pragma teoz true', 'skinparam wrapWidth 800', 'autonumber 1', 'participant a', 'participant b'];
const cases = {
  plain: 'a -> b: [[#iflow-1 GET: /x]]',
  tildeCp: 'a -> b: [[#iflow-1 GET: /<U+007E>/x]]',
  bracketCp: 'a -> b: [[#iflow-1 GET: /p?s<U+005B>z<U+005D>=1]]',
  closeOnlyCp: 'a -> b: [[#iflow-1 GET: /p?s[z<U+005D>=1]]',
  amp: 'a -> b: [[#iflow-1 GET: /p?a=1&b=2]]',
  full: 'a -> b: [[#iflow-1 POST: /api/articles?page<U+005B>size<U+005D>=10&q=a<U+007E>.b]]',
};
const input = Object.entries(cases).map(([id, l]) => JSON.stringify({ id, source: [...pre, l, '@enduml'].join('\n') })).join('\n') + '\n';
const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'], { input, encoding: 'utf8' });
for (const line of r.stdout.trim().split('\n')) {
  const o = JSON.parse(line);
  const t = [...(o.svg || '').matchAll(/<text[^>]*fill="([^"]*)"[^>]*>([^<]*)<\/text>/g)].map(m => m[1] + ':' + m[2]).filter(s => !/^#[0-9A-Fa-f]+:(a|b|1)$/.test(s));
  console.log(o.id.padEnd(10), t.join(' | ').slice(0, 200));
}
