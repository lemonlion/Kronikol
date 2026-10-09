'use strict';
// Per label and shape: the highest value that always drew below the lowest that failed, the line lengths there, the
// failure kind and the source line the engine blamed; any value whose repeats disagreed; and the legacy re-check.
const fs = require('fs'), path = require('path');
const LOGS = path.join(__dirname, 'logs');
const filter = process.argv[2] ? new RegExp(process.argv[2]) : null;
for (const f of fs.readdirSync(LOGS).filter(f => f.endsWith('.jsonl')).sort()) {
  if (filter && !filter.test(f)) continue;
  const rows = fs.readFileSync(path.join(LOGS, f), 'utf8').trim().split('\n').map(l => JSON.parse(l));
  const legacy = rows.filter(r => r.kind === 'legacy');
  if (legacy.length) {
    console.log(`${f}  (${legacy[0].browser} ${legacy[0].jsflags})`);
    for (const r of legacy) console.log(`   ${r.case.padEnd(12)} ${String(r.n).padStart(4)}  legacy rule: ${String(r.legacyVerdict).slice(0, 40).padEnd(40)} strict: ${r.strict}${r.detail ? '  [' + r.detail.slice(-90) + ']' : ''}`);
    continue;
  }
  const byShape = {};
  for (const r of rows.filter(r => r.kind === 'case')) (byShape[r.shape] = byShape[r.shape] || []).push(r);
  for (const [shape, rs] of Object.entries(byShape)) {
    const byV = {};
    for (const r of rs) (byV[r.value] = byV[r.value] || []).push(r);
    const vs = Object.keys(byV).map(Number).sort((a, b) => a - b);
    const ok = v => byV[v].every(r => r.v === 'drawn');
    const firstFail = vs.find(v => !ok(v));
    const below = vs.filter(v => firstFail === undefined || v < firstFail);
    const lastDraw = below[below.length - 1];
    const mixed = vs.filter(v => new Set(byV[v].map(r => r.v)).size > 1);
    const above = vs.filter(v => firstFail !== undefined && v > firstFail && ok(v));
    const lens = v => { const r = byV[v][0]; return `max ${r.maxLine} ${r.maxKind}${r.declLine ? ', decl ' + r.declLine : ''}${r.arrowLine ? ', arrow ' + r.arrowLine : ''}`; };
    const fail = firstFail === undefined ? null : byV[firstFail].find(r => r.v !== 'drawn');
    const blamed = fail && fail.detail ? (/line (\d+)/.exec(fail.detail) || [, '?'])[1] : '-';
    const ua = rs[0].tel ? rs[0].tel.ua : '';
    const odd = rs.filter(r => r.srcSame === false || r.frags).length;
    console.log(`${f.replace('.jsonl', '')} ${shape} | ${ua} | ${rs[0].tel ? rs[0].tel.mode : ''}${odd ? ' | ' + odd + ' cases changed or split by the page' : ''}`);
    if (firstFail === undefined) console.log(`   every value drew, to ${vs[vs.length - 1]} [${byV[vs[vs.length - 1]].map(r => r.v).join(',')}] (${lens(vs[vs.length - 1])})`);
    else console.log(`   last drew ${lastDraw} x${byV[lastDraw].length} (${lens(lastDraw)}); first failed ${firstFail} [${byV[firstFail].map(r => r.v).join(',')}] (${lens(firstFail)}), source line ${blamed}${mixed.length ? '; MIXED at ' + mixed.join(',') : ''}${above.length ? '; DREW ABOVE: ' + above.join(',') : ''}`);
    if (fail && fail.detail) console.log(`   ${fail.detail.slice(0, 200)}`);
  }
}
