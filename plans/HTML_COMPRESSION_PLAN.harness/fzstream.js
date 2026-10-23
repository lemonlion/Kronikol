// fzstd's streaming Decompress over fzstd_inputs.py's frames, fed in 64 KiB pieces, compared with the input's SHA-256
// node fzstream.js <folder written by fzstd_inputs.py>
const fs = require('fs'), crypto = require('crypto'), path = require('path');
const fzstd = require('fzstd');
const dir = process.argv[2];
const expected = fs.readFileSync(path.join(dir, 'expected.sha'), 'utf8').trim();
for (const name of ['w23', 'w27ldm', 'l22']) {
  const z = new Uint8Array(fs.readFileSync(path.join(dir, name + '.zst')));
  const hash = crypto.createHash('sha256'); let n = 0;
  try {
    const d = new fzstd.Decompress((chunk) => { hash.update(chunk); n += chunk.length; });
    for (let i = 0; i < z.length; i += 65536) d.push(z.subarray(i, Math.min(z.length, i + 65536)), i + 65536 >= z.length);
    console.log(name, 'stream decoded', n, hash.digest('hex') === expected ? 'IDENTICAL' : 'DIFFERENT');
  } catch (e) { console.log(name, 'stream THREW after', n, 'bytes:', String(e.message || e).slice(0, 100)); }
}
