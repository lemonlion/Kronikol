// payload-syntax-probe.js: which note text makes the engine ask its script loader for a bundle the Node
// renderer's mock DOM never loads, and what that does to a --batch run (every diagram of a NodeJs report).
//   node payload-syntax-probe.js <label>=<engine dir> [...]              the shipped plantuml-render.js
//   node payload-syntax-probe.js --loader-hook <label>=<engine dir> [...] the same script with
//        globalThis.PLANTUML_STDLIB_LOADER answering every request with a failure at once (a temp copy)
// <engine dir> holds plantuml.js and viz-global.js (see README.md). Measured 2026-09-25 on the pinned fork build
// and npm 1.2026.8 (plans/ENGINE_PIN_PLAN.md §1.15; results/payload-syntax-2026-09-25.txt): <&name> (openiconic.js)
// and <:name:> (emoji.js) time out after 20 s on both builds, and in --batch every later diagram times out too;
// with the hook (npm build only) they fail in about 270 ms and the batch isolates each failure.
const fs = require('fs'), path = require('path'), cp = require('child_process'), os = require('os');
const RENDER = path.resolve(__dirname, '../../src/Kronikol/PlantUml/plantuml-render.js');
let args = process.argv.slice(2), script = RENDER;
if (args[0] === '--loader-hook') {
  args = args.slice(1);
  const text = fs.readFileSync(RENDER, 'utf8');
  const marker = '// --- Phase 1: Load viz-global.js';
  if (!text.includes(marker)) throw new Error('marker not found in plantuml-render.js: ' + marker);
  script = path.join(fs.mkdtempSync(path.join(os.tmpdir(), 'hook-')), 'plantuml-render.js');
  fs.writeFileSync(script, text.replace(marker,
    'globalThis.PLANTUML_STDLIB_LOADER = function (name, ok, fail) { fail("Kronikol does not load " + name); return true; };\n' + marker));
}
const engines = args.map(a => { const i = a.indexOf('='); return { label: a.slice(0, i), dir: a.slice(i + 1) }; });
const doc = body => `@startuml\n!pragma teoz true\nparticipant A\nparticipant B\nA -> B : call\nnote right\n${body}\nend note\nB --> A : ok\n@enduml\n`;
const cases = {
  plain: doc('{ "a": 1 }'),
  icon: doc('{ "icon": "<&check> done" }'),
  emoji: doc('{ "e": "<:smile:> done" }'),
  sprite: doc('{ "s": "<$sprite>" }'),
  'icon-escaped': doc('{ "icon": "~<&check> done" }'),
  'emoji-escaped': doc('{ "e": "~<:smile:> done" }'),
  'icon-in-arrow-label': '@startuml\n!pragma teoz true\nA -> B : GET /x?<&check>\n@enduml\n',
  theme: '@startuml\n!pragma teoz true\n!theme cerulean\nA -> B : themed\n@enduml\n',
  'c4-include': '@startuml\n!include <C4/C4_Context>\nPerson(p, "Customer")\n@enduml\n',
};
const bin = dir => [path.join(dir, 'viz-global.js'), path.join(dir, 'plantuml.js')];
console.log('script', script === RENDER ? 'shipped plantuml-render.js' : 'with PLANTUML_STDLIB_LOADER failing at once', 'node', process.version);
for (const e of engines) {
  for (const [name, src] of Object.entries(cases)) {
    const t = Date.now();
    const r = cp.spawnSync(process.execPath, [script, ...bin(e.dir)], { input: src, encoding: 'utf8', timeout: 60000 });
    const err = (r.stderr || '').split(/\r?\n/).filter(l => l && !l.includes('code cache')).slice(-1)[0] || '';
    console.log(JSON.stringify({ engine: e.label, case: name, exit: r.status, ms: Date.now() - t, svgBytes: (r.stdout || '').includes('<svg') ? r.stdout.length : 0, stderr: err.slice(0, 100) }));
  }
  const order = ['plain', 'icon', 'plain', 'emoji', 'plain'];
  const input = order.map((n, i) => JSON.stringify({ id: `${i}-${n}`, source: cases[n] })).join('\n') + '\n';
  const t = Date.now();
  const r = cp.spawnSync(process.execPath, [script, ...bin(e.dir), '--batch'], { input, encoding: 'utf8', timeout: 300000 });
  const results = (r.stdout || '').split('\n').filter(Boolean).map(l => { const o = JSON.parse(l); return `${o.id}: ${o.svg ? 'svg' : 'ERROR ' + String(o.error).slice(0, 60)}`; });
  console.log(JSON.stringify({ engine: e.label, batch: order.join(','), exit: r.status, ms: Date.now() - t, results }));
}
