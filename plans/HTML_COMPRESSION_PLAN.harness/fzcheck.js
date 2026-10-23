// fzstd's one-shot decompress over fzstd_inputs.py's frames, compared with the input's SHA-256
// node fzcheck.js <folder written by fzstd_inputs.py>
const fs = require('fs'), crypto = require('crypto'), path = require('path');
const fzstd = require('fzstd');
const dir = process.argv[2];
const expected = fs.readFileSync(path.join(dir, 'expected.sha'), 'utf8').trim();
for (const name of ['w23', 'w27ldm', 'l22']) {
  const z = new Uint8Array(fs.readFileSync(path.join(dir, name + '.zst')));
  const t = Date.now();
  try {
    const out = fzstd.decompress(z);
    const sha = crypto.createHash('sha256').update(out).digest('hex');
    console.log(name, 'decoded', out.length, sha === expected ? 'IDENTICAL' : 'DIFFERENT', (Date.now() - t) + ' ms');
  } catch (e) { console.log(name, 'THREW', String(e.message || e).slice(0, 100)); }
}
