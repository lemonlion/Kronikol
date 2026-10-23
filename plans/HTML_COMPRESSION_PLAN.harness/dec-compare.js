// fzstd 0.1.1 against zstddec 0.3.4 (libzstd in Wasm) on real frames: same output, time (interleaved, 3 runs), and
// whether a bad checksum is noticed. node dec-compare.js <frame.zst> ...
const fs = require('fs');
const fzstd = require('fzstd');
const { ZSTDDecoder } = require('zstddec');
const crypto = require('crypto');
const h = b => crypto.createHash('sha256').update(b).digest('hex').slice(0, 16);
(async () => {
  const zd = new ZSTDDecoder(); await zd.init();
  for (const f of process.argv.slice(2)) {
    const z = new Uint8Array(fs.readFileSync(f));
    const a = fzstd.decompress(z);
    const b = zd.decode(z, a.length);
    const t = { fz: [], zd: [] };
    for (let r = 0; r < 3; r++) {
      let s = performance.now(); fzstd.decompress(z); t.fz.push(Math.round(performance.now() - s));
      s = performance.now(); zd.decode(z, a.length); t.zd.push(Math.round(performance.now() - s));
    }
    // A frame whose stored checksum (its last 4 bytes) is wrong
    const bad = z.slice(); bad[bad.length - 1] ^= 0xff;
    let fzBad, zdBad;
    try { fzBad = h(fzstd.decompress(bad)) === h(a) ? 'decoded, not noticed' : 'other output'; } catch (e) { fzBad = 'threw'; }
    try { zdBad = h(zd.decode(bad, a.length)) === h(a) ? 'decoded, not noticed' : 'other output'; } catch (e) { zdBad = 'threw: ' + String(e.message || e).slice(0, 60); }
    console.log(f.split(/[\/]/).pop(), `in ${z.length} out ${a.length}`, `same=${h(a) === h(b)}`,
      `fzstd ms ${t.fz.join('/')}`, `zstddec ms ${t.zd.join('/')}`, `| bad checksum: fzstd ${fzBad}; zstddec ${zdBad}`);
  }
})();
