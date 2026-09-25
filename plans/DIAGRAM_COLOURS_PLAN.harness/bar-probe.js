'use strict';
// DIAGRAM_COLOURS_PLAN harness (audit, 3.30.2): which escape forms paint as captured in a step bar's body
// (doc string and table cells), on the pinned engine through the shipped Node renderer. Each case replaces
// the bar of bar-template.puml, a source Kronikol's emitter wrote, with display lines joined by a literal
// backslash-n, as StepBarPlantUml writes them (one source line). `as-is` is what 3.30.1 wrote, `escaped`
// the form 3.30.2 writes; OK means the case paints its text as written, WRONG that it does not.
// The last case is the render-error placeholder as 3.30.1 wrote it (placeholder-probe.js has the fix).
// Usage (from the repo root): node plans/DIAGRAM_COLOURS_PLAN.harness/bar-probe.js
const cp = require('child_process'), path = require('path'), fs = require('fs');
const C = path.join(process.env.LOCALAPPDATA, 'Kronikol', 'plantuml-js', 'v1.2026.8beta1-0e4f452');
const REPO = path.resolve(__dirname, '../..');
const RENDERER = path.join(REPO, 'src', 'Kronikol', 'PlantUml', 'plantuml-render.js');
const BS = String.fromCharCode(92);
const NL = BS + 'n';
const tpl = fs.readFileSync(path.join(__dirname, 'bar-template.puml'), 'utf8');
const barLine = tpl.split('\n').find(l => l.startsWith('hnote across <<stepDelimiter>>'));
const bar = lines => tpl.split(barLine).join('hnote across <<stepDelimiter>><<stepBody>>: Given x' + NL + NL + lines.join(NL) + NL);
const cases = [
  ['bar-raw-control', bar(['home ~/.bashrc']), 'home ~/.bashrc', false],
  ['bar-tilde-cp', bar(['home <U+007E>/.bashrc']), 'home ~/.bashrc', true],
  ['bar-pct-raw', bar(['d %date() u %upper("x")']), 'd %date() u %upper("x")', false],
  ['bar-pct-cp', bar(['d <U+0025>date() u <U+0025>upper("x")']), 'd %date() u %upper("x")', true],
  ['bar-ref-raw', bar(['ref <U+0026>#39; end']), 'ref &#39; end', false],
  ['bar-ref-amp-zwsp', bar(['ref &<U+200B>#39; end']), 'ref &#39; end', true],
  ['bar-ref-cp-zwsp', bar(['ref <U+0026><U+200B>#39; end']), 'ref &#39; end', true],
  ['bar-sep-raw', bar(['a', '..sep..', 'b']), 'a..sep..b', false],
  ['bar-sep-cp', bar(['a', '<U+002E>.sep..', 'b']), 'a..sep..b', true],
  ['bar-rule-raw', bar(['a', '....', 'b']), 'a....b', false],
  ['bar-rule-cp', bar(['a', '<U+002E>...', 'b']), 'a....b', true],
  ['bar-wave-raw', bar(['~~w~~ t']), '~~w~~ t', false],
  ['bar-wave-cp', bar(['<U+007E><U+007E>w<U+007E><U+007E> t']), '~~w~~ t', true],
  ['bar-guillemet', bar(['a <U+003C><U+003C>b>> c']), 'a <<b>> c', true],
  ['bar-dashrule-zwsp', bar(['a', '-<U+200B>-', 'b']), 'a--b', true],
  ['bar-underrule-zwsp', bar(['a', '_<U+200B>_<U+200B>_', 'b']), 'a___b', true],
  ['bar-heading-cp', bar(['a', '<U+003D>= h ==', 'b']), 'a== h ==b', true],
  ['cell-raw', bar(['|= C |', '| ~/.bashrc |', '| %date() |', '| <U+0026>#39; |']), 'C~/.bashrc%date()&#39;', false],
  ['cell-cp', bar(['|= C |', '| <U+007E>/.bashrc |', '| <U+0025>date() |', '| <U+0026><U+200B>#39; |', '| <U+007E><U+007E>w<U+007E><U+007E> |']), 'C~/.bashrc%date()&#39;~~w~~', true],
  ['placeholder-real', '@startuml\nhnote across <<renderError>> #ffdddd\n\u26a0 diagram could not be generated: JsonException: \'<\' is an invalid start. ~/x __a__ **b**\nend note\n@enduml',
    "diagram could not be generated: JsonException: '<' is an invalid start. ~/x __a__ **b**", false],
];
const input = cases.map(([id, source]) => JSON.stringify({ id, source })).join('\n') + '\n';
const r = cp.spawnSync('node', [RENDERER, path.join(C, 'viz-global.js'), path.join(C, 'plantuml.js'), '--batch'], { input, encoding: 'utf8', maxBuffer: 1 << 28 });
const dec = s => s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&').replace(/\u00a0/g, ' ');
const byId = {};
for (const line of r.stdout.trim().split('\n')) { try { const o = JSON.parse(line); byId[o.id] = o; } catch { } }
for (const [id, , want, fixed] of cases) {
  const o = byId[id] || {}; const svg = o.svg || '';
  const texts = [...svg.matchAll(/<text([^>]*)>([^<]*)<\/text>/g)].map(m => ({ a: m[1], t: dec(m[2]).replace(/\u200b/g, '') }));
  const all = texts.map(x => x.t).join('');
  const broken = /^PlantUML (version|\d)/.test((texts[0] || {}).t || '');
  const styled = texts.filter(x => /font-style="italic"|text-decoration="(line-through|wavy|underline)"|font-weight="bold"/.test(x.a)).map(x => x.t).filter(t => t.trim() && !/^\d+$/.test(t));
  const ok = !broken && all.replace(/\s+/g, '').includes(want.replace(/\s+/g, ''));
  const i = all.indexOf('Given x'); const seg = i >= 0 ? all.slice(i + 7, i + 110) : all.slice(0, 150);
  console.log((broken ? 'BROKEN ' : ok ? 'OK     ' : 'WRONG  ') + (fixed ? 'escaped ' : 'as-is   ') + id.padEnd(20) + ' painted: ' + JSON.stringify(seg) + (styled.length ? '  styled: ' + JSON.stringify(styled) : '') + (o.error ? ' ERR ' + String(o.error).slice(0, 100) : ''));
}
