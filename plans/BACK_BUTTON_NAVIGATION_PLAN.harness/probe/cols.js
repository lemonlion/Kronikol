'use strict';
// Prints one engine's two columns (file, http) of every table in RESULTS.md: node cols.js <Chromium|Firefox|WebKit> [width]
const fs = require('fs');
const path = require('path');
const want = process.argv[2] || 'Firefox';
const width = Number(process.argv[3] || 400);
const lines = fs.readFileSync(path.join(__dirname, 'RESULTS.md'), 'utf8').split('\n');
const split = (ln) => ln.split(/(?<!\\)\|/).slice(1, -1).map((c) => c.trim());
let idx = null;
for (const ln of lines) {
  if (ln.startsWith('## ')) { console.log(ln); idx = null; continue; }
  if (ln.startsWith('| Measure')) {
    const h = split(ln);
    idx = [h.indexOf(`${want} file`), h.indexOf(`${want} http`)];
    continue;
  }
  if (!idx || !ln.startsWith('| ') || ln.startsWith('|---')) continue;
  const c = split(ln);
  const f = c[idx[0]] || '';
  const h = c[idx[1]] || '';
  console.log(`  ${c[0].slice(0, 80)}\n     file: ${f.slice(0, width)}\n     http: ${h === f ? '(same)' : h.slice(0, width)}`);
}
