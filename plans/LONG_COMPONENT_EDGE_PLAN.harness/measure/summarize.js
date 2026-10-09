'use strict';
// Reads logs/*.jsonl and prints, per configuration and shape: the highest length that always drew below the lowest
// length that failed, with the verdict counts at each, plus any length whose repeats disagreed.
const fs = require('fs'), path = require('path');
const LOGS = path.join(__dirname, 'logs');
const filter = process.argv[2] ? new RegExp(process.argv[2]) : null;
const groups = {};
for (const f of fs.readdirSync(LOGS).filter(f => f.endsWith('.jsonl')).sort()) {
  if (filter && !filter.test(f)) continue;
  for (const line of fs.readFileSync(path.join(LOGS, f), 'utf8').trim().split('\n')) {
    if (!line) continue;
    const o = JSON.parse(line);
    if (o.kind !== 'case') continue;
    const key = `${o.label || f.replace(/.jsonl$/, '')} | ${o.shape || 'realN'}`;
    const g = groups[key] = groups[key] || { byL: {}, ua: new Set(), modes: new Set() };
    const L = o.L;
    (g.byL[L] = g.byL[L] || []).push(o.v + (o.n !== undefined ? `(N=${o.n})` : ''));
    if (o.tel && o.tel.ua) g.ua.add((o.tel.ua.match(/(HeadlessChrome|Firefox|Version)\/[\d.]+/) || [''])[0]);
    if (o.tel) g.modes.add(o.tel.mode + '/' + o.tel.workers);
  }
}
for (const [key, g] of Object.entries(groups)) {
  const Ls = Object.keys(g.byL).map(Number).sort((a, b) => a - b);
  const ok = L => g.byL[L].every(v => v.startsWith('drawn'));
  const firstFail = Ls.find(L => !ok(L));
  const below = Ls.filter(L => firstFail === undefined || L < firstFail);
  const lastDraw = below.length ? below[below.length - 1] : undefined;
  const mixed = Ls.filter(L => new Set(g.byL[L].map(v => v.replace(/\(N=\d+\)/, ''))).size > 1);
  const failAboveDrawn = Ls.filter(L => firstFail !== undefined && L > firstFail && ok(L));
  const fmt = L => L === undefined ? '-' : `${L} [${g.byL[L].join(',')}]`;
  console.log(`${key} | ${[...g.ua].join(',')} | ${[...g.modes].join(',')}`);
  console.log(`   last drawn ${fmt(lastDraw)}  first failed ${fmt(firstFail)}${mixed.length ? '  MIXED at ' + mixed.map(fmt).join(' ') : ''}${failAboveDrawn.length ? '  DREW ABOVE FIRST FAIL: ' + failAboveDrawn.map(fmt).join(' ') : ''}`);
  if (key.includes('realN') || key.includes('short') || Ls.length <= 12 && !key.includes('scan')) {
    // Lists: every length.
    if (key.includes('realN') || key.includes('short')) console.log('   ' + Ls.map(fmt).join('  '));
  }
}
