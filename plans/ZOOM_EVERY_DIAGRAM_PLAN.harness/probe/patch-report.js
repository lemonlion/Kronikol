'use strict';
// Copied from the 5.5.1 work for M9 (the 5.4.4 report with 5.5.2's scripts swapped in): base ref v5.4.4.
// Writes a copy of a report with page scripts swapped for a worktree's edited ones. The generator fills placeholders
// (__NAME__) in some scripts, so each file is cut at its placeholder lines, and every piece that the edit changed is
// found verbatim in the report (once) and replaced by the edited piece.
// usage: node patch-report.js <report.html> <out.html> <worktree> <base-ref> <script under src/Kronikol/Reports> [...]
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const [inFile, outFile, worktree, base, ...scripts] = process.argv.slice(2);
const NL = String.fromCharCode(10);
let html = fs.readFileSync(inFile, 'utf8');
const pieces = (text) => {
  const out = [[]];
  text.split(NL).forEach((l) => { if (/__[A-Z_]+__/.test(l)) out.push([]); else out[out.length - 1].push(l); });
  return out.map((p) => p.join(NL));
};
for (const s of scripts) {
  const rel = 'src/Kronikol/Reports/' + s;
  const before = execFileSync('git', ['-C', worktree, 'show', base + ':' + rel], { encoding: 'utf8', maxBuffer: 64 << 20 }).split('\r\n').join(NL);
  // AFTER_DIR=<dir> reads the edited file from <dir>/<script> (a marked copy for timing) instead of the worktree
  const after = fs.readFileSync(process.env.AFTER_DIR ? path.join(process.env.AFTER_DIR, s) : path.join(worktree, rel), 'utf8').split('\r\n').join(NL);
  const b = pieces(before), a = pieces(after);
  if (b.length !== a.length) { console.error(s + ': the edit changed placeholder lines'); process.exit(4); }
  let changed = 0;
  for (let i = 0; i < b.length; i++) {
    if (b[i] === a[i]) continue;
    // drop a <script> line or a trailing </script> the generator may have rewritten
    let from = b[i], to = a[i], at = html.indexOf(from);
    if (at < 0) {
      const bl = from.split(NL), al = to.split(NL);
      while (bl.length && al.length && /<\/?script/.test(bl[0]) && bl[0] === al[0]) { bl.shift(); al.shift(); }
      while (bl.length && al.length && /<\/?script/.test(bl[bl.length - 1]) && bl[bl.length - 1] === al[al.length - 1]) { bl.pop(); al.pop(); }
      from = bl.join(NL); to = al.join(NL); at = html.indexOf(from);
    }
    if (at < 0) { console.error(s + ': piece ' + i + ' not found in the report'); process.exit(2); }
    if (html.indexOf(from, at + 1) >= 0) { console.error(s + ': piece ' + i + ' found twice'); process.exit(3); }
    html = html.slice(0, at) + to + html.slice(at + from.length);
    changed++;
  }
  console.log('patched ' + s + ': ' + changed + ' piece(s) changed');
}
fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, html);
