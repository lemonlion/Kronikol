'use strict';
// Reads probe-widths.js output. usage: node analyse.js <widths.json>
const fs = require('fs');
const all = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const { scenarios, vw, vh } = all;
// a box not laid out (a view not displayed) measures 0 and gets no slider until it is shown: counted apart
const hidden = all.rows.filter((r) => !(r.client > 0));
const rows = all.rows.filter((r) => r.client > 0);
console.log(`not laid out (width 0, not counted below): ${hidden.length} (${['sequence', 'activity', 'other'].map((k) => k + ' ' + hidden.filter((r) => r.kind === k).length).join(', ')})`);
const pct = (n, d) => d ? (100 * n / d).toFixed(1) + '%' : '-';
const q = (a, p) => { const s = a.slice().sort((x, y) => x - y); return s.length ? s[Math.min(s.length - 1, Math.floor(p * s.length))] : null; };
const wide = (r) => r.natW > r.client + 1;            // today's rule: a slider
const scaled = (r) => r.natW > r.content + 0.5;      // drawn smaller than natural (max-width: 100% of the content box)
const band = rows.filter((r) => scaled(r) && !wide(r));
console.log(`viewport ${vw}x${vh}; scenarios ${scenarios}; boxes ${rows.length}`);
for (const k of ['sequence', 'activity', 'other']) {
  const rs = rows.filter((r) => r.kind === k);
  if (!rs.length) continue;
  console.log(`  ${k}: ${rs.length} boxes, wide (slider today) ${rs.filter(wide).length} = ${pct(rs.filter(wide).length, rs.length)}, fitting ${rs.filter((r) => !wide(r)).length}`);
}
console.log(`padding band (drawn scaled, no slider): ${band.length}; scale there min ${q(band.map((r) => r.content / r.natW), 0)}`);
const fits = rows.filter((r) => wide(r)).map((r) => Math.round(r.client / r.natW * 100));
console.log(`wide fit% today: min ${q(fits, 0)} p10 ${q(fits, 0.1)} median ${q(fits, 0.5)} p90 ${q(fits, 0.9)}`);
const fitting = rows.filter((r) => !wide(r));
const fw = fitting.map((r) => r.natW);
console.log(`fitting natural width: min ${q(fw, 0)} p10 ${q(fw, 0.1)} median ${q(fw, 0.5)} p90 ${q(fw, 0.9)} max ${q(fw, 1)}`);
const tall = fitting.filter((r) => r.natH > vh);
console.log(`fitting taller than the viewport: ${tall.length} = ${pct(tall.length, fitting.length)}; natural height median ${q(fitting.map((r) => r.natH), 0.5)}, p90 ${q(fitting.map((r) => r.natH), 0.9)}`);
// the owner's case: within a scenario, in page order, a fitting box right after a wide one (or before)
const bySc = new Map();
rows.forEach((r) => { if (!bySc.has(r.si)) bySc.set(r.si, []); bySc.get(r.si).push(r); });
let mixed = 0, wThenF = 0, fThenW = 0, partsMixed = 0, withAny = 0;
for (const [, rs] of bySc) {
  withAny++;
  const seq = rs.map(wide);
  if (seq.includes(true) && seq.includes(false)) mixed++;
  for (let i = 1; i < seq.length; i++) { if (seq[i - 1] && !seq[i]) wThenF++; if (!seq[i - 1] && seq[i]) fThenW++; }
}
const diag = new Map();
rows.filter((r) => r.parts > 1).forEach((r) => { const k = r.si + ':' + r.ci; if (!diag.has(k)) diag.set(k, []); diag.get(k).push(wide(r)); });
for (const [, s] of diag) if (s.includes(true) && s.includes(false)) partsMixed++;
console.log(`scenarios with a diagram ${withAny}; mixing wide and fitting ${mixed} = ${pct(mixed, withAny)}; wide-then-fitting adjacencies ${wThenF}, fitting-then-wide ${fThenW}`);
console.log(`diagrams drawn in parts ${diag.size}; with both wide and fitting parts ${partsMixed}`);
// M6: one scale per diagram drawn in parts (the smallest fit of its parts), against each part's own fit today
{
  const fitOf = (r) => Math.min(1, r.client / r.natW);
  const groups = new Map();
  rows.filter((r) => r.parts > 1).forEach((r) => { const k = r.si + ':' + r.ci; if (!groups.has(k)) groups.set(k, []); groups.get(k).push(r); });
  let parts = 0, smaller = 0, dropping = 0, drops = 0; const loss = [];
  for (const [, rs] of groups) {
    rs.sort((a, b) => a.pi - b.pi);
    const shared = Math.min(...rs.map(fitOf));
    let run = fitOf(rs[0]), d = 0;
    for (let i = 1; i < rs.length; i++) if (fitOf(rs[i]) < run - 0.005) { d++; run = fitOf(rs[i]); }
    if (d) { dropping++; drops += d; }
    rs.forEach((r) => { parts++; if (shared < fitOf(r) - 0.005) { smaller++; loss.push(100 * (fitOf(r) - shared)); } });
  }
  console.log(`one scale per diagram in parts: ${groups.size} diagrams, ${parts} parts; drawn smaller ${smaller}, loss in points median ${q(loss, 0.5)?.toFixed(1)} p90 ${q(loss, 0.9)?.toFixed(1)} max ${q(loss, 1)?.toFixed(1)}; scale drops while first drawn in ${dropping} diagrams (${drops} drops)`);
}
