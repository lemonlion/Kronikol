'use strict';
// node showfn.js <engine.js> <name> [<name> ...]  — prints the definition `name=(...)=>{...}` (first 600 chars)
// node showfn.js <engine.js> @<line>:<col> ...      — prints 300 chars either side of a stack position
const fs = require('fs');
const [file, ...names] = process.argv.slice(2);
const s = fs.readFileSync(file, 'utf8');
const lines = s.split('\n');
const esc = (x) => x.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
for (const n of names) {
  if (n.startsWith('@')) {
    const [l, c] = n.slice(1).split(':').map(Number);
    const line = lines[l - 1];
    console.log(`${n}: ...${line.slice(Math.max(0, c - 400), c - 1)}<<HERE>>${line.slice(c - 1, c + 200)}...`);
    console.log('----');
    continue;
  }
  const re = new RegExp('(^|[,;\\s{}])' + esc(n) + '\\s*=\\s*(\\(|[A-Za-z_$][\\w$]*\\s*=>)', 'gm');
  let m, k = 0;
  while ((m = re.exec(s)) && k < 3) { k++; console.log(`${n}#${k}: ${s.slice(m.index + m[1].length, m.index + m[1].length + 700)}`); console.log('----'); }
  if (!k) console.log(n + ': not found');
}
