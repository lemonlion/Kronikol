// statement-limits-probe.js: the statement-length probes of NodeJsPlantUmlRendererTests (the pins
// behind PlantUmlStatementLimits) run against any engine build, without changing the pinned
// constant, so an engine move can be measured before it is made. One node process per probe, as
// NodeJsPlantUmlRenderer.RenderMany does for the tests, because the stack-overflow edges depend on
// the process's own stack. Beyond the pins it searches for each edge (the largest length that still
// renders), so the doc comment on PlantUmlStatementLimits can quote the new build's numbers.
//   node statement-limits-probe.js <label>=<dir holding plantuml.js and viz-global.js> [more...]
'use strict';
const fs = require('fs');
const path = require('path');
const cp = require('child_process');

const SCRIPT = path.resolve(__dirname, '../../src/Kronikol/PlantUml/plantuml-render.js');
const engines = process.argv.slice(2).map(function (a) { const i = a.indexOf('='); return { label: a.slice(0, i), dir: a.slice(i + 1) }; });
if (engines.length === 0 || engines.some(function (e) { return !e.label || !fs.existsSync(path.join(e.dir, 'plantuml.js')); })) {
  console.error('usage: node statement-limits-probe.js <label>=<dir> [...]'); process.exit(2);
}

let processes = 0, processMs = 0;
function renderBody(engine, body) {
  const source = '@startuml\n' + body + '\n@enduml';
  const t0 = Date.now();
  const r = cp.spawnSync('node', [SCRIPT, path.join(engine.dir, 'viz-global.js'), path.join(engine.dir, 'plantuml.js'), '--batch'],
    { input: JSON.stringify({ id: '0', source: source }) + '\n', encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, timeout: 120000 });
  processes++; processMs += Date.now() - t0;
  if (r.error) return '';
  for (const line of String(r.stdout || '').split('\n')) {
    const t = line.trim(); if (!t) continue;
    try { const o = JSON.parse(t); if (o.id === '0') return o.svg && o.svg.indexOf('<svg') >= 0 ? o.svg : ''; } catch (e) { /* not ours */ }
  }
  return '';
}
function renders(svg) { return svg.length > 0 && svg.indexOf('Syntax Error') < 0; }
function drewMessage(svg) { return renders(svg) && (svg.match(/>b<\/text>/g) || []).length >= 2; }
function statementOf(prefix, total) { return prefix + 'x'.repeat(total - prefix.length); }

// The largest n in [lo, hi] for which ok(n) holds, given ok(lo) and not ok(hi); one bisection.
function edge(lo, hi, ok) {
  while (hi - lo > 1) { const mid = Math.floor((lo + hi) / 2); if (ok(mid)) lo = mid; else hi = mid; }
  return lo;
}

const MESSAGE_PREFIXES = ['a -> b: ', 'a --> b: ', 'a -[#F39C12]> b: ', 'a -[#F39C12]-> b: ', 'aaaaaaaaaaaaaaaaaaaa -> b: '];
const BAR_PREFIX = 'hnote across <<stepDelimiter>> #black:<color:white>';

const results = {};
for (const engine of engines) {
  const R = results[engine.label] = { pins: [], edges: {} };
  function pin(name, pass) { R.pins.push({ name: name, pass: pass }); console.log((pass ? 'PASS ' : 'FAIL ') + engine.label + ': ' + name); }
  const t0 = Date.now();

  // The message limit: 2000 exactly, on the whole statement.
  for (const p of MESSAGE_PREFIXES) {
    pin('message ' + JSON.stringify(p) + ' at 2000 draws', drewMessage(renderBody(engine, statementOf(p, 2000))));
    pin('message ' + JSON.stringify(p) + ' at 2001 does not', !drewMessage(renderBody(engine, statementOf(p, 2001))));
  }
  pin('leading and trailing whitespace does not count', drewMessage(renderBody(engine, '    ' + statementOf('a -> b: ', 2000) + ' '.repeat(500))));

  // Block openers: the constant parses, a runaway one still takes the diagram down.
  const block = function (opener, n) { return renders(renderBody(engine, 'a -> b: x\n' + statementOf(opener, n) + '\na -> b: y\nend')); };
  pin('loop at 1471 renders', block('loop ', 1471));
  pin('loop at 8000 does not', !block('loop ', 8000));
  for (const opener of ['loop ', 'alt ', 'group ', 'opt ']) {
    R.edges[opener.trim()] = block(opener, 8000) ? '>8000' : edge(1471, 8000, function (n) { return block(opener, n); });
    console.log('edge ' + engine.label + ': ' + opener.trim() + ' ' + R.edges[opener.trim()]);
  }

  // The coloured note bar: renders at the constant, crashes the engine (no SVG at all) far past it.
  const bar = function (n) { return renderBody(engine, 'a -> b: x\n' + BAR_PREFIX + 's'.repeat(n)); };
  pin('coloured bar at 1400 total renders', renders(bar(1400 - BAR_PREFIX.length)));
  pin('coloured bar with 6000 s yields no SVG', bar(6000) === '');
  R.edges['coloured bar (total statement)'] = BAR_PREFIX.length + edge(1400 - BAR_PREFIX.length, 6000, function (n) { return renders(bar(n)); });
  console.log('edge ' + engine.label + ': coloured bar total ' + R.edges['coloured bar (total statement)']);

  // Uncoloured bars and note bodies: no low cap; the ceiling near 16,400.
  const forms = {
    'uncoloured bar': function (n) { return 'a -> b: x\nhnote across #black:' + 'n'.repeat(n); },
    'note body': function (n) { return 'a -> b: x\nnote left\n' + 'n'.repeat(n) + '\nend note'; },
    'one-line note': function (n) { return 'a -> b: x\nnote over a : ' + 'n'.repeat(n); }
  };
  for (const name of Object.keys(forms)) {
    const ok = function (n) { return renders(renderBody(engine, forms[name](n))); };
    pin(name + ' at 6000 renders', ok(6000));
    R.edges[name + ' (payload chars)'] = ok(20000) ? '>20000' : edge(6000, 20000, ok);
    console.log('edge ' + engine.label + ': ' + name + ' ' + R.edges[name + ' (payload chars)']);
  }
  R.seconds = Math.round((Date.now() - t0) / 1000);
}

console.log('\n# ' + processes + ' node processes, ' + Math.round(processMs / processes) + ' ms each on average\n');
console.log('| probe | ' + engines.map(function (e) { return e.label; }).join(' | ') + ' |');
console.log('|---|' + engines.map(function () { return '---'; }).join('|') + '|');
const pinNames = results[engines[0].label].pins.map(function (p) { return p.name; });
for (const name of pinNames) console.log('| ' + name + ' | ' + engines.map(function (e) { const p = results[e.label].pins.find(function (x) { return x.name === name; }); return p ? (p.pass ? 'pass' : 'FAIL') : '-'; }).join(' | ') + ' |');
const edgeNames = Object.keys(results[engines[0].label].edges);
for (const name of edgeNames) console.log('| edge: ' + name + ' | ' + engines.map(function (e) { return results[e.label].edges[name]; }).join(' | ') + ' |');
console.log('| seconds | ' + engines.map(function (e) { return results[e.label].seconds; }).join(' | ') + ' |');
fs.writeFileSync(path.join(__dirname, 'results', 'statement-limits-' + new Date().toISOString().slice(0, 10) + '.json'), JSON.stringify({ script: SCRIPT, node: process.version, engines: engines, results: results }, null, 2));
