// R30: the same twelve sources as loader-probe.js SET=statement, as one NDJSON batch for the Node renderer.
// Usage (from the harness directory): node statement-node.js, then
//   node $C/plantuml-render.js $C/viz-global.js $C/plantuml.js --batch < <temp>/statement.ndjson
const fs = require('fs'), path = require('path');
const base = fs.readFileSync(path.join(__dirname, 'payload-longpath.puml'), 'utf8').replace(/\r\n/g, '\n');
const st = base.split('\n').find(l => l.includes('[[#iflow-'));
const m = /^(.*?: )\[\[#iflow-[0-9a-f-]+ (.*)\]\]$/.exec(st);
const label = m[2].replace(/…$/, '') + Array.from({ length: 60 }, (_, i) => '&extra' + i + '=value' + i).join('');
const cut = (s, n) => s.slice(0, n - 1) + '…';
const withLink = n => { const head = st.slice(0, st.indexOf(m[2])); return head + cut(label, n - head.length - 2) + ']]'; };
const noLink = n => m[1] + cut(label, n - m[1].length);
const lines = [];
for (const n of [1000, 1200, 1400, 1500, 1600, 1700, 1800, 1900, 2000]) lines.push(JSON.stringify({ id: 'link' + n, source: base.replace(st, withLink(n)) }));
for (const n of [1500, 1800, 2000]) lines.push(JSON.stringify({ id: 'nolink' + n, source: base.replace(st, noLink(n)) }));
fs.writeFileSync(path.join(require('os').tmpdir(), 'statement.ndjson'), lines.join('\n') + '\n');
