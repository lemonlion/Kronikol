'use strict';
// Prints one case of a results file compactly: node peek.js <engine> <case> [run] [proto]
const path = require('path');
const [engine, kase, run = '1', proto] = process.argv.slice(2);
const data = require(path.join(__dirname, 'results', engine + '.json'));
const strip = (v) => JSON.stringify(v, (k, x) => {
  if (k === 'href' || k === 'destUrl' || k === 'oldURL' || k === 'newURL' || k === 'executable') {
    return typeof x === 'string' ? x.replace(/^.*\/pages\//, '…/') : x;
  }
  if (k === 't' || k === 'where') return undefined;
  return x;
});
for (const r of data.results) {
  if (r.case !== kase || String(r.run) !== run || (proto && r.proto !== proto)) continue;
  console.log(`=== ${engine} run${r.run} ${r.proto} ${r.case} (${r.ms} ms)`);
  const res = r.res;
  for (const [k, v] of Object.entries(res)) console.log(k + ': ' + strip(v));
}
