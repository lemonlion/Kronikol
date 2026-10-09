// node wait-for.js <file> <regex> [timeout s]: returns when the file matches (polls every 2 s), prints the last lines.
const fs = require('fs');
const [file, re, t = 1500] = process.argv.slice(2);
const rx = new RegExp(re, 'm'), end = Date.now() + t * 1000;
(function poll() {
  let s = '';
  try { s = fs.readFileSync(file, 'utf8'); } catch {}
  if (rx.test(s) || Date.now() > end) { console.log((rx.test(s) ? 'MATCHED' : 'TIMEOUT') + '\n' + s.trim().split('\n').slice(-4).join('\n').slice(0, 1500)); return; }
  setTimeout(poll, 2000);
})();
