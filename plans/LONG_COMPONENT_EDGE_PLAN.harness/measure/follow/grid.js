// node grid.js <label> <shape> [from] [to]: every value tried, with its verdicts in order and the line lengths.
const fs = require('fs');
const [label, shape, from = 0, to = 1e9] = process.argv.slice(2);
const rows = fs.readFileSync(`${__dirname}/logs/${label}.jsonl`, 'utf8').trim().split('\n').map(JSON.parse).filter(r => r.kind === 'case' && r.shape === shape && r.value >= +from && r.value <= +to);
const by = {};
for (const r of rows) (by[r.value] = by[r.value] || { v: [], r }).v.push(r.v === 'drawn' ? 'd' : r.v === 'ERROR' ? 'E' : r.v === 'STACK' ? 'S' : r.v === 'STACK-TEXT' ? 'T' : r.v);
for (const k of Object.keys(by).map(Number).sort((a, b) => a - b)) {
  const r = by[k].r;
  console.log(`${label} ${shape} ${String(k).padStart(5)}  max ${r.maxLine} ${r.maxKind}${r.declLine ? ' decl ' + r.declLine : ''}  ${by[k].v.join('')}`);
}
