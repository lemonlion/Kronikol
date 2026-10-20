'use strict';
// Counts, per engine and restore method, how many Q7/Q7b trials landed on the saved section: node q7-counts.js
const fs = require('fs');
const path = require('path');
const R = path.join(__dirname, 'results');
const same = (saved, o) => !!(o && o.top && saved && saved.top && o.top.id === saved.top.id && Math.abs(o.top.offset - saved.top.offset) <= 2);
const tally = {};
const add = (engine, key, ok) => { const t = (tally[engine] = tally[engine] || {}); const k = (t[key] = t[key] || { same: 0, trials: 0 }); k.trials++; if (ok) k.same++; };
for (const engine of ['chromium', 'chromium-real', 'firefox', 'webkit']) {
  const d = JSON.parse(fs.readFileSync(path.join(R, engine + '.json'), 'utf8'));
  for (const x of d.results.filter((r) => r.case === 'q7')) {
    for (const [variant, v] of Object.entries(x.res)) {
      if (!v || v.harnessError) continue;
      // Firefox's page.reload() over http is not a reload (Q11c): skip those trials.
      if (engine === 'firefox' && x.proto === 'http' && v.leave === 'reload') continue;
      const where = v.leave === 'back' ? (v.detour ? 'back-detour' : 'back-top') : 'reload';
      if (v.restore) add(engine, `${where} (${v.restore})`, same(v.saved2, v.restoredLate));
      else add(engine, `${where} browser-auto`, same(v.saved2, v.browser));
    }
  }
  const xf = path.join(R, `extras-${engine}-q7reload.json`);
  if (fs.existsSync(xf)) {
    for (const x of JSON.parse(fs.readFileSync(xf, 'utf8')).results) {
      for (const [variant, v] of Object.entries(x.res)) {
        const key = variant === 'reloadAuto' ? 'location.reload browser-auto' : `location.reload (${variant.endsWith('A') ? 'a' : 'b'})`;
        add(engine, key, variant === 'reloadAuto' ? same(v.saved, v.browser) : same(v.saved, v.restoredLate));
      }
    }
  }
}
const totals = {};
for (const [engine, t] of Object.entries(tally)) {
  console.log(engine);
  for (const [k, v] of Object.entries(t)) {
    console.log(`  ${k.padEnd(32)} ${v.same}/${v.trials}`);
    const m = k.endsWith('(b)') ? 'b' : k.endsWith('(a)') ? 'a' : 'auto';
    const tt = (totals[m] = totals[m] || { same: 0, trials: 0 }); tt.same += v.same; tt.trials += v.trials;
  }
}
console.log('totals', JSON.stringify(totals));
