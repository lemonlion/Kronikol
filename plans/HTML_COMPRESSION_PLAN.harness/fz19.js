// fzstd issue #19 recipe through fzstd 0.1.1 one-shot decompress, streaming Decompress.push in several chunk sizes,
// and zstddec 0.3.4 (libzstd in Wasm). node fz19.js <dir>
const fs = require('fs'), path = require('path');
const fzstd = require('fzstd');
const dir = process.argv[2];
const same = (a, b) => a.length === b.length && Buffer.compare(Buffer.from(a.buffer, a.byteOffset, a.length), Buffer.from(b.buffer, b.byteOffset, b.length)) === 0;
(async () => {
  const { ZSTDDecoder } = require('zstddec');
  const zd = new ZSTDDecoder(); await zd.init();
  for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.zst')).sort()) {
    const z = new Uint8Array(fs.readFileSync(path.join(dir, f)));
    const raw = new Uint8Array(fs.readFileSync(path.join(dir, f.split('.')[0] + '.raw')));
    const res = [];
    try { res.push('oneshot=' + same(fzstd.decompress(z), raw)); } catch (e) { res.push('oneshot=THROW ' + e.message); }
    for (const chunk of [z.length, 65536, 4096, 1000, 1]) {
      try {
        const parts = []; let n = 0;
        const d = new fzstd.Decompress((c) => { parts.push(c.slice()); n += c.length; });
        for (let i = 0; i < z.length; i += chunk) d.push(z.subarray(i, Math.min(z.length, i + chunk)), i + chunk >= z.length);
        const outb = new Uint8Array(n); let o = 0; for (const p of parts) { outb.set(p, o); o += p.length; }
        res.push(`push${chunk === z.length ? 'All' : chunk}=` + same(outb, raw));
      } catch (e) { res.push(`push${chunk}=THROW ${e.message}`); }
    }
    try { res.push('zstddec=' + same(zd.decode(z, raw.length), raw)); } catch (e) { res.push('zstddec=THROW ' + e.message); }
    console.log(f.padEnd(22), String(raw.length).padStart(9), res.join(' '));
  }
})();
