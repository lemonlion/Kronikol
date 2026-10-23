'use strict';
// Medians of the toggle bench's switches per report copy and scenario: screenMs, doneMs, longest task, long-task total.
const fs = require('fs'), path = require('path');
const dir = process.argv[2];
const med = (a) => { const s = a.filter((x) => typeof x === 'number').sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : '-'; };
for (const f of fs.readdirSync(dir).filter((n) => /^toggles-.*\.jsonl$/.test(n)).sort()) {
  const lines = fs.readFileSync(path.join(dir, f), 'utf8').split('\n').filter((l) => l.startsWith('{'));
  const rows = lines.map((l) => { try { return JSON.parse(l); } catch (e) { return null; } }).filter((r) => r && r.toggle);
  const head = lines.map((l) => { try { return JSON.parse(l); } catch (e) { return null; } }).find((r) => r && r.state);
  const info = head ? head.state.inScen.map((s) => s.drawnInfo.map((d) => d.parts + ' parts/' + d.texts + ' texts').join(', ')).join(' | ') : '';
  const pick = (k) => med(rows.map((r) => r[k]));
  console.log(`${f}: ${rows.length} switches; screen ${pick('screenMs')} ms, done ${pick('doneMs')} ms, longest task ${pick('longest')} ms, long tasks ${pick('longTotal')} ms; diagrams: ${info}`);
  for (const t of ['assertions', 'steps', 'databases', 'setup']) {
    const rs = rows.filter((r) => r.toggle === t);
    if (rs.length) console.log(`    ${t}: screen ${med(rs.map((r) => r.screenMs))}, done ${med(rs.map((r) => r.doneMs))}, longest ${med(rs.map((r) => r.longest))}`);
  }
}
